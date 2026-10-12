using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Auth;
using SeneCare.Application.Common;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Identity;

namespace SeneCare.Api.Endpoints;

public sealed record OtpRequest(string? Phone, OtpChannel Channel = OtpChannel.Sms);

public sealed record OtpResponse(Guid ChallengeId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);

public sealed record VerifyRequest(Guid ChallengeId, string? Code);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool IsNewUser,
    bool IsRegistrationComplete);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record RegisterRequest(string? FullName, AccountKind? Profile, string? CompanyName, bool AcceptsPrivacyPolicy, bool AcceptsWhatsApp);

/// <summary>/api/v1/auth: phone + OTP sign-in for the mobile app, JWT access token + rotating refresh token.</summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "otp";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/otp", RequestOtpAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/otp/verify", VerifyOtpAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/refresh", RefreshAsync);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapPost("/register", RegisterAsync).RequireAuthorization(Policies.Authenticated);

        api.MapGet("/me", GetMeAsync).RequireAuthorization(Policies.Authenticated).WithTags("Account");
        return api;
    }

    private static async Task<Results<Accepted<OtpResponse>, ProblemHttpResult>> RequestOtpAsync(
        OtpRequest request, RequestOtpHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RequestOtpCommand(request.Phone, request.Channel, http.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null, new OtpResponse(result.Value.ChallengeId, result.Value.ExpiresAt, result.Value.ResendAvailableAt))
            : Problem(result.Error);
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> VerifyOtpAsync(
        VerifyRequest request, VerifyOtpHandler handler, IAuthTokenService tokens, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new VerifyOtpCommand(request.ChallengeId, request.Code), cancellationToken);
        if (!result.IsSuccess)
        {
            return Problem(result.Error);
        }

        var issued = await tokens.IssueAsync(result.Value.UserId, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return TypedResults.Ok(ToResponse(issued, result.Value.IsNewUser, result.Value.IsRegistrationComplete));
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> RefreshAsync(
        RefreshRequest request, IAuthTokenService tokens, HttpContext http, CancellationToken cancellationToken)
    {
        var issued = await tokens.RefreshAsync(request.RefreshToken ?? string.Empty, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return issued is null ? TypedResults.Unauthorized() : TypedResults.Ok(ToResponse(issued, false, true));
    }

    private static async Task<NoContent> LogoutAsync(RefreshRequest request, IAuthTokenService tokens, CancellationToken cancellationToken)
    {
        await tokens.RevokeAsync(request.RefreshToken ?? string.Empty, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Completes sign-up, then returns fresh tokens carrying the new claims.</summary>
    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterRequest request, ClaimsPrincipal user, CompleteRegistrationHandler handler, IAuthTokenService tokens, HttpContext http, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId()!.Value;
        var result = await handler.HandleAsync(
            new CompleteRegistrationCommand(userId, request.FullName, request.Profile, request.CompanyName, request.AcceptsPrivacyPolicy, request.AcceptsWhatsApp),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return Problem(result.Error);
        }

        var issued = await tokens.IssueAsync(userId, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return TypedResults.Ok(ToResponse(issued, false, true));
    }

    private static async Task<Results<Ok<UserProfileDto>, NotFound>> GetMeAsync(ClaimsPrincipal user, GetUserProfileHandler handler, CancellationToken cancellationToken)
    {
        var profile = await handler.HandleAsync(user.GetUserId()!.Value, cancellationToken);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }

    private static TokenResponse ToResponse(AuthTokens tokens, bool isNewUser, bool isRegistrationComplete) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt, isNewUser, isRegistrationComplete);

    /// <summary>RFC 7807 problem with a stable « code » for the app to translate.</summary>
    private static ProblemHttpResult Problem(AppError? error)
    {
        var code = error?.Code ?? "error";
        var status = code is "otp.cooldown" or "otp.too_many_requests" ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest;
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (error?.RetryAt is { } retryAt)
        {
            extensions["retryAt"] = retryAt;
        }

        return TypedResults.Problem(statusCode: status, title: code, extensions: extensions);
    }
}
