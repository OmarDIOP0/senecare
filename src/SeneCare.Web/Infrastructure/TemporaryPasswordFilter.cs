using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SeneCare.Infrastructure.Identity;

namespace SeneCare.Web.Infrastructure;

/// <summary>Pages that stay open with a temporary password (changing it, signing out).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWithTemporaryPasswordAttribute : Attribute;

/// <summary>
/// Signed in with a temporary password (admin reset, first admin): every page leads to « Choisissez votre mot de
/// passe » until it is replaced, so it cannot stay in use.
/// </summary>
public sealed class TemporaryPasswordFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.HttpContext.User.MustChangePassword()
            || context.ActionDescriptor.EndpointMetadata.OfType<AllowWithTemporaryPasswordAttribute>().Any())
        {
            return;
        }

        context.Result = new LocalRedirectResult(SeneCare.Web.Controllers.AccountController.ChangePasswordPath);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
