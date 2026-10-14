using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using SeneCare.Web.Localization;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// A form sent with an outdated security token (page opened before signing in or out in another tab, browser
/// « Back » after the code, page left open for hours) used to end on a blank « HTTP ERROR 400 ». The person
/// is sent back to the page instead, reloaded with a fresh token, with a short explanation.
/// </summary>
public sealed class AntiforgeryRecoveryFilter(ITempDataDictionaryFactory tempData, IStringLocalizer<SharedResource> localizer)
    : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Result is not IAntiforgeryValidationFailedResult)
        {
            return;
        }

        var request = context.HttpContext.Request;
        // Script calls (HTMX, fetch) keep the 400: the page handles it, a redirect would be swallowed.
        if (request.Headers.ContainsKey("HX-Request") || request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Saved now: the usual « save TempData » filter does not run when the token check stops the request.
        var messages = tempData.GetTempData(context.HttpContext);
        messages["flash.toast"] = localizer["Form_Expired"].Value;
        messages.Save();
        context.Result = new LocalRedirectResult(BackTo(request));
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    /// <summary>The page the form came from when it is ours, the home page otherwise.</summary>
    private static string BackTo(HttpRequest request) =>
        Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
        && string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
        && referer.PathAndQuery.StartsWith('/')
            ? referer.PathAndQuery
            : "/";
}
