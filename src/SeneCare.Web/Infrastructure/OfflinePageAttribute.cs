using Microsoft.AspNetCore.Mvc.Filters;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// Lets the service worker keep this page for offline use (header « X-SeneCare-Offline: page »).
/// Only for pages that must work without network (the report form). Personal pages are not
/// cached by default, and cached pages are erased at sign-out (Clear-Site-Data).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class OfflinePageAttribute : ResultFilterAttribute
{
    public const string HeaderName = "X-SeneCare-Offline";

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HttpContext.Response.Headers[HeaderName] = "page";
    }
}
