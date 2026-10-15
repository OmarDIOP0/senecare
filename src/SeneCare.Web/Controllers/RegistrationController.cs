using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Auth;
using SeneCare.Application.Media;
using SeneCare.Application.Neighborhoods;
using SeneCare.Application.Properties;
using SeneCare.Domain.Accounts;
using SeneCare.Application.Users;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.Localization;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

/// <summary>
/// « Créer mon compte » in 5 steps: profile and phone number (anonymous, the code is checked by
/// <see cref="AuthController"/>), then the home or first site, the offer and consent (law 2008-12).
/// </summary>
[Authorize(Policy = Policies.Authenticated)]
[Route("/inscription")]
public sealed class RegistrationController(
    CompleteRegistrationHandler completeRegistration,
    SeneCare.Application.Public.GetPublicHomeHandler offers,
    GetUserProfileHandler getProfile,
    AddPropertyHandler addProperty,
    ReferralService referrals,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet("")]
    [AllowAnonymous]
    public async Task<IActionResult> Index(AccountKind? profil, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            // Steps 1–2: who it is for, then the number that receives the code.
            var earlier = Enum.TryParse<AccountKind>(TempData.Peek(AuthController.SignupProfileKey) as string, out var kept) ? kept : AccountKind.Household;
            return View("Start", new SignInViewModel { Signup = true, Profile = profil ?? earlier });
        }

        var profile = await getProfile.HandleAsync(User.GetUserId()!.Value, cancellationToken);
        if (profile is null)
        {
            return Challenge();
        }

        if (profile.IsRegistrationComplete)
        {
            return LocalRedirect("/");
        }

        // Profile already chosen before the number (« Créer mon compte »): go on at step 3.
        // Signed in from « Mon espace » without an account: start with « Ma maison / Mon entreprise ».
        var chosenBefore = Enum.TryParse<AccountKind>(TempData[AuthController.SignupProfileKey] as string, out var chosen);
        return View(new RegistrationViewModel
        {
            FullName = profile.FullName,
            JoiningAccountName = (profile.Memberships.Count > 0 ? profile.Memberships[0].AccountName : null),
            AcceptsWhatsApp = true,
            IsStaff = IsStaff(profile),
            Profile = chosenBefore ? chosen : profil ?? AccountKind.Household,
            StartStep = chosenBefore ? 2 : 0,
            Plans = (await offers.HandleAsync(cancellationToken)).Plans,
            Phone = profile.Phone,
            Property = new PropertyFormModel(),
        });
    }

    [HttpPost("")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Index(RegistrationViewModel model, List<IFormFile> photos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(photos);
        var userId = User.GetUserId()!.Value;

        // The home is created right after the account: check the address first so that nothing is half done.
        var withProperty = model.HasProperty && model.JoiningAccountName is null;
        var missing = !withProperty ? null
            : string.IsNullOrWhiteSpace(model.Property.Name) ? "property.name"
            : model.Property.NeighborhoodId is null ? "address.neighborhood"
            : PropertyFormModel.ParseCoordinate(model.Property.Latitude) is null && string.IsNullOrWhiteSpace(model.Property.Landmark) ? "address.locate"
            : null;
        if (missing is not null)
        {
            ModelState.AddModelError(string.Empty, this.ErrorMessage(new SeneCare.Application.Common.AppError(missing)));
            model.StartStep = 2;
            return View(await RebuildAsync(model, userId, cancellationToken));
        }

        var result = await completeRegistration.HandleAsync(
            new CompleteRegistrationCommand(
                userId, model.FullName, model.Profile, model.CompanyName, model.AcceptsPrivacyPolicy, model.AcceptsWhatsApp,
                model.PlanCode, model.Annual),
            cancellationToken);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, this.ErrorMessage(result.Error));
            // Name, consent, offer: the error is shown on the last step, where they are checked.
            model.StartStep = 4;
            return View(await RebuildAsync(model, userId, cancellationToken));
        }

        // Arrived through a neighbour's shared link: credit them (Programme Quartiers).
        if (Request.Cookies.TryGetValue(CommunityController.ReferralCookie, out var code))
        {
            await referrals.AttachAsync(userId, code, cancellationToken);
            Response.Cookies.Delete(CommunityController.ReferralCookie);
        }

        // Claims changed (name, roles, membership): re-issue the cookie.
        var identityUser = await userManager.FindByIdAsync(userId.ToString());
        // Now a customer: the long-lived cookie (the sign-in before was for this browser session only).
        await signInManager.SignInAsync(identityUser!, isPersistent: true);

        if (withProperty)
        {
            var property = model.Property;
            var added = await addProperty.HandleAsync(
                new AddPropertyCommand(
                    userId, property.Kind, property.Name, property.NeighborhoodId,
                    PropertyFormModel.ParseCoordinate(property.Latitude), PropertyFormModel.ParseCoordinate(property.Longitude),
                    property.Landmark, property.Details, [.. photos.Select(p => new UploadedFile(p.OpenReadStream(), p.Length))]),
                cancellationToken);
            if (added.IsSuccess)
            {
                // The chosen offer is now a subscription waiting for its Wave payment.
                TempData["flash.toast"] = this.Localizer()["Register_Done"].Value;
                return LocalRedirect(string.IsNullOrEmpty(model.PlanCode) ? "/" : "/abonnement");
            }

            // Account created, home refused (e.g. a photo): finish it on the usual form.
            TempData["flash.error"] = this.ErrorMessage(added.Error);
        }

        return LocalRedirect(string.IsNullOrEmpty(model.PlanCode) && !withProperty ? "/" : "/logements/nouveau?bienvenue=1");
    }

    private async Task<RegistrationViewModel> RebuildAsync(RegistrationViewModel model, Guid userId, CancellationToken cancellationToken)
    {
        var profile = await getProfile.HandleAsync(userId, cancellationToken);
        model.JoiningAccountName = profile?.Memberships.Count > 0 ? profile.Memberships[0].AccountName : null;
        model.IsStaff = profile is not null && IsStaff(profile);
        model.Plans = (await offers.HandleAsync(cancellationToken)).Plans;
        model.Phone = profile?.Phone;
        return model;
    }

    private static bool IsStaff(UserProfileDto profile) =>
        profile.Roles.Any(r => r != SeneCare.Domain.Users.PlatformRole.Customer);
}
