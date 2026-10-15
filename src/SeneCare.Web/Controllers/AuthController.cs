using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SeneCare.Application.Accounts;
using SeneCare.Application.Auth;
using SeneCare.Domain.Accounts;
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
    UserManager<ApplicationUser> userManager,
    Microsoft.Extensions.Options.IOptions<OtpOptions> otpOptions) : Controller
{
    private const string ChallengeKey = "otp.challenge";
    private const string PhoneKey = "otp.phone";
    private const string ChannelKey = "otp.channel";
    private const string ReturnUrlKey = "auth.returnUrl";

    /// <summary>Set while the number is verified as step 2 of « Créer mon compte » (profile kept for step 3).</summary>
    public const string SignupKey = "signup";
    public const string SignupProfileKey = "signup.profile";

    /// <summary>Development only (Otp:ShowCodeOnPage): the code, shown under the boxes.</summary>
    private const string DevelopmentCodeKey = "otp.dev";

    [HttpGet("/connexion")]
    public IActionResult SignIn(string? returnUrl, string? phone)
    {
        if (User.Identity?.IsAuthenticated == true && User.IsRegistered() && TempData.Peek(InvitationController.InvitationKey) is null)
        {
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        TempData[ReturnUrlKey] = SafeReturnUrl(returnUrl);

        // Number typed in a quick form (home, trade page): say at once when it is not a Senegalese mobile.
        if (!string.IsNullOrWhiteSpace(phone))
        {
            if (PhoneNumber.TryParse(phone, out var parsed))
            {
                phone = parsed.Formatted;
            }
            else
            {
                ModelState.AddModelError(nameof(SignInViewModel.Phone), this.ErrorMessage(new SeneCare.Application.Common.AppError("phone.invalid")));
            }
        }

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
            return model.Signup ? View("~/Views/Registration/Start.cshtml", model) : View(model);
        }

        if (model.Signup)
        {
            TempData[SignupKey] = "1";
            TempData[SignupProfileKey] = (model.Profile ?? AccountKind.Household).ToString();
        }
        else
        {
            TempData.Remove(SignupKey);
        }

        // Development without SMS: the code is checked at once, no code page.
        if (result.Value.DevelopmentCode is { } skipped && otpOptions.Value.SkipVerification)
        {
            var signedIn = await verifyOtp.HandleAsync(new VerifyOtpCommand(result.Value.ChallengeId, skipped), cancellationToken);
            if (!signedIn.IsSuccess)
            {
                ModelState.AddModelError(nameof(model.Phone), this.ErrorMessage(signedIn.Error));
                return model.Signup ? View("~/Views/Registration/Start.cshtml", model) : View(model);
            }

            return await SignInVerifiedAsync(signedIn.Value.UserId, cancellationToken);
        }

        TempData[ChallengeKey] = result.Value.ChallengeId.ToString();
        TempData[PhoneKey] = result.Value.Phone.NationalNumber;
        TempData[ChannelKey] = result.Value.Channel.ToString();
        TempData[DevelopmentCodeKey] = result.Value.DevelopmentCode;
        return RedirectToAction(nameof(Code));
    }

    [HttpGet("/connexion/code")]
    public IActionResult Code()
    {
        if (!TryGetPendingChallenge(out _, out var phone, out var channel))
        {
            return AfterCodeOrSignIn();
        }

        return View(new CodeViewModel
        {
            Phone = phone,
            Channel = channel,
            Signup = TempData.Peek(SignupKey) is not null,
            DevelopmentCode = TempData.Peek(DevelopmentCodeKey) as string,
        });
    }

    [HttpPost("/connexion/code")]
    [EnableRateLimiting(RateLimits.Otp)]
    public async Task<IActionResult> Code(CodeViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!TryGetPendingChallenge(out var challengeId, out var phone, out var channel))
        {
            return AfterCodeOrSignIn();
        }

        model.Phone = phone;
        model.Channel = channel;
        model.Signup = TempData.Peek(SignupKey) is not null;
        model.DevelopmentCode = TempData.Peek(DevelopmentCodeKey) as string;
        var result = await verifyOtp.HandleAsync(new VerifyOtpCommand(challengeId, model.Code), cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(nameof(model.Code), this.ErrorMessage(result.Error));
            return View(model);
        }

        TempData.Remove(ChallengeKey);
        TempData.Remove(PhoneKey);
        TempData.Remove(ChannelKey);
        TempData.Remove(DevelopmentCodeKey);
        return await SignInVerifiedAsync(result.Value.UserId, cancellationToken);
    }

    private async Task<IActionResult> SignInVerifiedAsync(Guid userId, CancellationToken cancellationToken)
    {
        TempData.Remove(SignupKey);
        var identityUser = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Identity user missing after OTP verification.");

        if (AsGuid(TempData[InvitationController.InvitationKey]) is { } id)
        {
            var accepted = await acceptInvitation.HandleAsync(new AcceptInvitationCommand(id, identityUser.Id), cancellationToken);
            if (!accepted.IsSuccess)
            {
                TempData["flash.error"] = this.ErrorMessage(accepted.Error);
            }
        }

        // Customers: persistent cookie, on mobile data nobody wants a new code every visit. A number without an
        // account yet only gets a browser-session cookie, so a typo or an abandoned sign-up does not stick for 30 days.
        var registered = await IsRegisteredAsync(identityUser);
        await signInManager.SignInAsync(identityUser, isPersistent: registered);

        var returnUrl = TempData[ReturnUrlKey] as string;
        if (registered)
        {
            // Already a customer who went through « Créer mon compte »: straight to the space.
            TempData.Remove(SignupProfileKey);
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        return RedirectToAction(nameof(RegistrationController.Index), "Registration");
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
            TempData[DevelopmentCodeKey] = result.Value.DevelopmentCode;
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

    /// <summary>
    /// No code pending: usually the code page sent twice, the first time already signed the person in. Go on to
    /// the sign-up or the space instead of asking for the number again.
    /// </summary>
    private IActionResult AfterCodeOrSignIn()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction(nameof(SignIn));
        }

        return User.IsRegistered() ? LocalRedirect("/") : RedirectToAction(nameof(RegistrationController.Index), "Registration");
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
