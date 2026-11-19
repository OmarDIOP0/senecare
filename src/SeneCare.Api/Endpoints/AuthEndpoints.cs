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
    bool IsRegistrationComplete,
    bool MustChangePassword = false);

public sealed record LoginRequest(string? Phone, string? Password);

public sealed record SignupRequest(string? Phone, string? Password, string? ConfirmPassword);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword, string? ConfirmPassword);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record RegisterRequest(string? FullName, AccountKind? Profile, string? CompanyName, bool AcceptsPrivacyPolicy, bool AcceptsWhatsApp);

/// <summary>
/// /api/v1/auth for the mobile app: phone + password (default) or phone + one-time code (Auth:Mode = Otp),
/// JWT access token + rotating refresh token.
/// </summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "otp";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/signup", SignupAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/password", ChangePasswordAsync).RequireAuthorization(Policies.Authenticated).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/otp", RequestOtpAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/otp/verify", VerifyOtpAsync).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/refresh", RefreshAsync);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapPost("/register", RegisterAsync).RequireAuthorization(Policies.Authenticated);

        api.MapGet("/me", GetMeAsync).RequireAuthorization(Policies.Authenticated).WithTags("Account");
        return api;
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, PasswordSignInHandler handler, IAuthTokenService tokens, IApplicationDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new PasswordSignInCommand(request.Phone, request.Password), cancellationToken);
        if (!result.IsSuccess)
        {
            return Problem(result.Error);
        }

        var issued = await tokens.IssueAsync(result.Value.UserId, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        var complete = await IsRegistrationCompleteAsync(db, result.Value.UserId, cancellationToken);
        return TypedResults.Ok(ToResponse(issued, false, complete) with { MustChangePassword = result.Value.MustChangePassword });
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> SignupAsync(
        SignupRequest request, PasswordSignUpHandler handler, IAuthTokenService tokens, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new PasswordSignUpCommand(request.Phone, request.Password, request.ConfirmPassword), cancellationToken);
        if (!result.IsSuccess)
        {
            return Problem(result.Error);
        }

        // Then POST /auth/register (name, profile, consent), like after the code in OTP mode.
        var issued = await tokens.IssueAsync(result.Value, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return TypedResults.Ok(ToResponse(issued, true, false));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request, ClaimsPrincipal user, ChangePasswordHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ChangePasswordCommand(user.GetUserId()!.Value, request.CurrentPassword, request.NewPassword, request.ConfirmPassword), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : Problem(result.Error);
    }

    private static async Task<bool> IsRegistrationCompleteAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Users, u => u.Id == userId, cancellationToken);
        var hasAccount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.CustomerAccounts, a => a.Members.Any(m => m.UserId == userId), cancellationToken);
        return user is not null && RegistrationStatus.IsComplete(user, hasAccount);
    }

    private static async Task<Results<Accepted<OtpResponse>, ProblemHttpResult>> RequestOtpAsync(
        OtpRequest request, RequestOtpHandler handler, Microsoft.Extensions.Options.IOptions<AuthOptions> options, HttpContext http, CancellationToken cancellationToken)
    {
        if (!options.Value.UsesOtp)
        {
            return Problem(new AppError("auth.otp_disabled"));
        }

        var result = await handler.HandleAsync(new RequestOtpCommand(request.Phone, request.Channel, http.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null, new OtpResponse(result.Value.ChallengeId, result.Value.ExpiresAt, result.Value.ResendAvailableAt))
            : Problem(result.Error);
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> VerifyOtpAsync(
        VerifyRequest request, VerifyOtpHandler handler, IAuthTokenService tokens, Microsoft.Extensions.Options.IOptions<AuthOptions> options, HttpContext http, CancellationToken cancellationToken)
    {
        if (!options.Value.UsesOtp)
        {
            return Problem(new AppError("auth.otp_disabled"));
        }

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
        var status = code switch
        {
            "otp.cooldown" or "otp.too_many_requests" or "auth.locked" => StatusCodes.Status429TooManyRequests,
            "auth.invalid" => StatusCodes.Status401Unauthorized,
            "auth.otp_disabled" => StatusCodes.Status404NotFound,
            "auth.phone_taken" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (error?.RetryAt is { } retryAt)
        {
            extensions["retryAt"] = retryAt;
        }

        return TypedResults.Problem(statusCode: status, title: code, extensions: extensions);
    }
}
