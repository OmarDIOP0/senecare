using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Auth;
using SeneCare.Application.Neighborhoods;
using SeneCare.Application.Users;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

/// <summary>Last sign-up step: name, « Ma maison » / « Mon entreprise », consent (law 2008-12).</summary>
[Authorize(Policy = Policies.Authenticated)]
[Route("/inscription")]
public sealed class RegistrationController(
    CompleteRegistrationHandler completeRegistration,
    GetUserProfileHandler getProfile,
    ReferralService referrals,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await getProfile.HandleAsync(User.GetUserId()!.Value, cancellationToken);
        if (profile is null)
        {
            return Challenge();
        }

        if (profile.IsRegistrationComplete)
        {
            return LocalRedirect("/");
        }

        return View(new RegistrationViewModel
        {
            FullName = profile.FullName,
            JoiningAccountName = (profile.Memberships.Count > 0 ? profile.Memberships[0].AccountName : null),
            AcceptsWhatsApp = true,
            IsStaff = IsStaff(profile),
        });
    }

    [HttpPost("")]
    public async Task<IActionResult> Index(RegistrationViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var userId = User.GetUserId()!.Value;
        var result = await completeRegistration.HandleAsync(
            new CompleteRegistrationCommand(userId, model.FullName, model.Profile, model.CompanyName, model.AcceptsPrivacyPolicy, model.AcceptsWhatsApp),
            cancellationToken);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, this.ErrorMessage(result.Error));
            var profile = await getProfile.HandleAsync(userId, cancellationToken);
            model.JoiningAccountName = profile?.Memberships.Count > 0 ? profile.Memberships[0].AccountName : null;
            model.IsStaff = profile is not null && IsStaff(profile);
            return View(model);
        }

        // Arrived through a neighbour's shared link: credit them (Programme Quartiers).
        if (Request.Cookies.TryGetValue(CommunityController.ReferralCookie, out var code))
        {
            await referrals.AttachAsync(userId, code, cancellationToken);
            Response.Cookies.Delete(CommunityController.ReferralCookie);
        }

        // Claims changed (name, roles, membership): re-issue the cookie.
        var identityUser = await userManager.FindByIdAsync(userId.ToString());
        await signInManager.RefreshSignInAsync(identityUser!);
        return LocalRedirect("/");
    }

    private static bool IsStaff(UserProfileDto profile) =>
        profile.Roles.Any(r => r != SeneCare.Domain.Users.PlatformRole.Customer);
}
