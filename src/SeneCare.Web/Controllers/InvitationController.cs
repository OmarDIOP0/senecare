using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Accounts;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

/// <summary>Link received by an invited employee: shows the company, then signs in with OTP on the invited number.</summary>
[AllowAnonymous]
[Route("/invitation")]
public sealed class InvitationController(
    GetInvitationHandler getInvitation,
    AcceptInvitationHandler acceptInvitation,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : Controller
{
    public const string InvitationKey = "invitation.id";

    [HttpGet("{token}")]
    public async Task<IActionResult> Show(string token, CancellationToken cancellationToken)
    {
        var invitation = await getInvitation.HandleAsync(token, cancellationToken);
        return View(new InvitationViewModel { Token = token, Invitation = invitation });
    }

    [HttpPost("{token}")]
    public async Task<IActionResult> Accept(string token, CancellationToken cancellationToken)
    {
        var invitation = await getInvitation.HandleAsync(token, cancellationToken);
        if (invitation is not { IsUsable: true })
        {
            return RedirectToAction(nameof(Show), new { token });
        }

        // Already signed in (with the invited number): join right away.
        if (User.GetUserId() is { } userId)
        {
            var result = await acceptInvitation.HandleAsync(new AcceptInvitationCommand(invitation.InvitationId, userId), cancellationToken);
            if (!result.IsSuccess)
            {
                TempData["flash.error"] = this.ErrorMessage(result.Error);
                return RedirectToAction(nameof(Show), new { token });
            }

            var identityUser = await userManager.FindByIdAsync(userId.ToString());
            await signInManager.RefreshSignInAsync(identityUser!);
            return LocalRedirect("/inscription");
        }

        TempData[InvitationKey] = invitation.InvitationId.ToString();
        return RedirectToAction(nameof(AuthController.SignIn), "Auth", new { phone = invitation.Phone.Formatted, returnUrl = "/inscription" });
    }
}
