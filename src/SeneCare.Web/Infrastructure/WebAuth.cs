using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SeneCare.Application.Common;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Localization;

namespace SeneCare.Web.Infrastructure;

public static class RateLimits
{
    /// <summary>Per-IP limit on code requests / verifications (on top of per-number limits in the use case).</summary>
    public const string Otp = "otp";

    public static IServiceCollection AddSeneCareRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        // 30 sign-in / sign-up attempts per 10 minutes and per network address. Many phones share one address at
        // the mobile operator, and a beginner makes several mistakes: 10 blocked real people. Passwords are also
        // protected per account (locked 15 minutes after 5 mistakes).
        var permitLimit = configuration.GetValue("RateLimits:Otp:PermitLimit", 30);
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // A person sees a message on the page they came from, never the browser's blank « HTTP ERROR 429 ».
            options.OnRejected = (context, cancellationToken) => TooManyAttemptsAsync(context.HttpContext);
            options.AddPolicy(Otp, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(10) }));
        });
    }

    private static ValueTask TooManyAttemptsAsync(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "600";
        var request = http.Request;
        var accept = request.Headers.Accept.ToString();
        if (request.Headers.ContainsKey("HX-Request") || !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.CompletedTask;
        }

        var localizer = http.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        var tempData = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionaryFactory>().GetTempData(http);
        tempData["flash.warning"] = localizer["Error_too_many_attempts"].Value;
        tempData.Save();

        var back = Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
            && string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
                ? referer.PathAndQuery
                : "/connexion";
        http.Response.StatusCode = StatusCodes.Status303SeeOther;
        http.Response.Headers.Location = back;
        return ValueTask.CompletedTask;
    }
}

public static class ControllerErrorExtensions
{
    /// <summary>Translates a use-case error code (« otp.cooldown ») via the resource « Error_otp_cooldown ».</summary>
    public static string ErrorMessage(this Controller controller, AppError? error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var localizer = controller.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        if (error is null)
        {
            return localizer["Error_generic"];
        }

        var message = localizer["Error_" + error.Code.Replace('.', '_')];
        return message.ResourceNotFound ? localizer["Error_generic"] : message.Value;
    }
}

/// <summary>
/// Signed-in users who have not finished registration (name, profile, consent) are sent to /inscription
/// instead of « access denied ».
/// </summary>
public sealed class RegistrationRedirectHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true && !context.User.IsRegistered()
            && !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/inscription");
            return Task.CompletedTask;
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
