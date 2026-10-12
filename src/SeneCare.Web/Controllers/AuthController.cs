using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SeneCare.Application.Accounts;
using SeneCare.Application.Auth;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

/// <summary>Sign-in by phone number + one-time code (no password). Anonymous except sign-out.</summary>
public sealed class AuthController(
    RequestOtpHandler requestOtp,
    VerifyOtpHandler verifyOtp,
    AcceptInvitationHandler acceptInvitation,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : Controller
{
    private const string ChallengeKey = "otp.challenge";
    private const string PhoneKey = "otp.phone";
    private const string ChannelKey = "otp.channel";
    private const string ReturnUrlKey = "auth.returnUrl";

    [HttpGet("/connexion")]
    public IActionResult SignIn(string? returnUrl, string? phone)
    {
        if (User.Identity?.IsAuthenticated == true && TempData.Peek(InvitationController.InvitationKey) is null)
        {
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        TempData[ReturnUrlKey] = SafeReturnUrl(returnUrl);
        return View(new SignInViewModel { Phone = phone });
    }

    [HttpPost("/connexion")]
    [EnableRateLimiting(RateLimits.Otp)]
    public async Task<IActionResult> SignIn(SignInViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var result = await requestOtp.HandleAsync(new RequestOtpCommand(model.Phone, model.Channel, ClientIp), cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(nameof(model.Phone), this.ErrorMessage(result.Error));
            return View(model);
        }

        TempData[ChallengeKey] = result.Value.ChallengeId.ToString();
        TempData[PhoneKey] = result.Value.Phone.NationalNumber;
        TempData[ChannelKey] = result.Value.Channel.ToString();
        return RedirectToAction(nameof(Code));
    }

    [HttpGet("/connexion/code")]
    public IActionResult Code()
    {
        if (!TryGetPendingChallenge(out _, out var phone, out var channel))
        {
            return RedirectToAction(nameof(SignIn));
        }

        return View(new CodeViewModel { Phone = phone, Channel = channel });
    }

    [HttpPost("/connexion/code")]
    [EnableRateLimiting(RateLimits.Otp)]
    public async Task<IActionResult> Code(CodeViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!TryGetPendingChallenge(out var challengeId, out var phone, out var channel))
        {
            return RedirectToAction(nameof(SignIn));
        }

        model.Phone = phone;
        model.Channel = channel;
        var result = await verifyOtp.HandleAsync(new VerifyOtpCommand(challengeId, model.Code), cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(nameof(model.Code), this.ErrorMessage(result.Error));
            return View(model);
        }

        TempData.Remove(ChallengeKey);
        TempData.Remove(PhoneKey);
        TempData.Remove(ChannelKey);

        var identityUser = await userManager.FindByIdAsync(result.Value.UserId.ToString())
            ?? throw new InvalidOperationException("Identity user missing after OTP verification.");

        if (AsGuid(TempData[InvitationController.InvitationKey]) is { } id)
        {
            var accepted = await acceptInvitation.HandleAsync(new AcceptInvitationCommand(id, identityUser.Id), cancellationToken);
            if (!accepted.IsSuccess)
            {
                TempData["flash.error"] = this.ErrorMessage(accepted.Error);
            }
        }

        // Persistent cookie: on mobile data, nobody wants a new SMS every visit.
        await signInManager.SignInAsync(identityUser, isPersistent: true);

        var returnUrl = TempData[ReturnUrlKey] as string;
        return await IsRegisteredAsync(identityUser)
            ? LocalRedirect(SafeReturnUrl(returnUrl))
            : RedirectToAction(nameof(RegistrationController.Index), "Registration");
    }

    [HttpPost("/connexion/renvoyer")]
    [EnableRateLimiting(RateLimits.Otp)]
    public async Task<IActionResult> Resend(OtpChannel channel, CancellationToken cancellationToken)
    {
        if (!TryGetPendingChallenge(out _, out var phone, out _))
        {
            return RedirectToAction(nameof(SignIn));
        }

        var result = await requestOtp.HandleAsync(new RequestOtpCommand(phone, channel, ClientIp), cancellationToken);
        if (result.IsSuccess)
        {
            TempData[ChallengeKey] = result.Value.ChallengeId.ToString();
            TempData[ChannelKey] = result.Value.Channel.ToString();
        }
        else
        {
            TempData["flash.error"] = this.ErrorMessage(result.Error);
        }

        return RedirectToAction(nameof(Code));
    }

    [HttpPost("/deconnexion")]
    [Authorize(Policy = Policies.Authenticated)]
    public async Task<IActionResult> SignOutUser()
    {
        await signInManager.SignOutAsync();

        // Shared phones: the browser erases cached pages, offline queue and storage of this site,
        // even if the service worker message never arrives.
        Response.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";
        return LocalRedirect("/");
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<bool> IsRegisteredAsync(ApplicationUser user)
    {
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return principal.IsRegistered();
    }

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    private bool TryGetPendingChallenge(out Guid challengeId, out string phone, out OtpChannel channel)
    {
        phone = TempData.Peek(PhoneKey) as string ?? string.Empty;
        channel = Enum.TryParse<OtpChannel>(TempData.Peek(ChannelKey) as string, out var c) ? c : OtpChannel.Sms;
        challengeId = AsGuid(TempData.Peek(ChallengeKey)) ?? Guid.Empty;
        return challengeId != Guid.Empty && phone.Length > 0;
    }

    /// <summary>TempData's serializer turns GUID-looking strings back into <see cref="Guid"/> values.</summary>
    private static Guid? AsGuid(object? value) => value switch
    {
        Guid guid => guid,
        string text when Guid.TryParse(text, out var guid) => guid,
        _ => null,
    };
}
