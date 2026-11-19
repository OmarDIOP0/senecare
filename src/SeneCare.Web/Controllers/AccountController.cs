using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SeneCare.Application.Accounts;
using SeneCare.Application.Auth;
using SeneCare.Application.Business;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

[Authorize]
public sealed class AccountController(GetUserProfileHandler getProfile) : Controller
{
    public const string ChangePasswordPath = "/compte/mot-de-passe";

    [HttpGet("/compte")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await getProfile.HandleAsync(User.GetUserId()!.Value, cancellationToken);
        return profile is null ? Challenge() : View(profile);
    }

    /// <summary>« Changer mon mot de passe »; also where a temporary password (admin reset) leads first.</summary>
    [HttpGet(ChangePasswordPath)]
    [Authorize(Policy = Policies.Authenticated)]
    [AllowWithTemporaryPassword]
    public IActionResult ChangePassword() =>
        View(new ChangePasswordViewModel { MustChange = User.MustChangePassword() });

    [HttpPost(ChangePasswordPath)]
    [Authorize(Policy = Policies.Authenticated)]
    [AllowWithTemporaryPassword]
    [EnableRateLimiting(RateLimits.Otp)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordViewModel model, [FromServices] ChangePasswordHandler handler,
        [FromServices] SignInManager<ApplicationUser> signInManager, [FromServices] UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var userId = User.GetUserId()!.Value;
        var result = await handler.HandleAsync(new ChangePasswordCommand(userId, model.CurrentPassword, model.NewPassword, model.ConfirmPassword), cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(result.Error!.Code == "password.current_wrong" ? nameof(model.CurrentPassword) : nameof(model.NewPassword), this.ErrorMessage(result.Error));
            model.MustChange = User.MustChangePassword();
            return View(model);
        }

        // New cookie: without the « temporary password » mark, with the new security stamp.
        var identityUser = await userManager.FindByIdAsync(userId.ToString());
        await signInManager.RefreshSignInAsync(identityUser!);
        TempData["flash.toast"] = this.Localizer()["Password_Changed"].Value;
        return LocalRedirect("/");
    }
}

/// <summary>Business manager: team members and employee invitations.</summary>
[Authorize(Policy = Policies.OrganizationManager)]
[Route("/entreprise/equipe")]
public sealed class TeamController(GetTeamHandler getTeam, InviteMemberHandler inviteMember, TeamManagementHandler management) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await BuildModelAsync(new InviteMemberViewModel(), cancellationToken));

    [HttpPost("")]
    [EnableRateLimiting(RateLimits.Otp)]
    public async Task<IActionResult> Index([Bind(Prefix = "Invite")] InviteMemberViewModel invite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invite);
        var account = ManagedAccount();
        var urlTemplate = Url.ActionLink(nameof(InvitationController.Show), "Invitation", new { token = "TOKEN" })!.Replace("TOKEN", "{0}", StringComparison.Ordinal);
        var result = await inviteMember.HandleAsync(
            new InviteMemberCommand(
                User.GetUserId()!.Value, account.AccountId, invite.Phone, invite.Role,
                invite.Role == AccountRole.Manager ? [] : invite.Sites, urlTemplate),
            cancellationToken);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError("Invite.Phone", this.ErrorMessage(result.Error));
            return View(await BuildModelAsync(invite, cancellationToken));
        }

        // Shown once: the manager sends the link (the token is only stored hashed).
        var sent = result.Value;
        TempData["flash.success"] = sent.Phone.Formatted;
        TempData["invite.link"] = sent.Link;
        TempData["invite.whatsapp"] = $"https://wa.me/{sent.Phone.E164.TrimStart('+')}?text={Uri.EscapeDataString(sent.Message)}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{memberId:guid}")]
    public async Task<IActionResult> Update(Guid memberId, AccountRole role, List<Guid> sites, CancellationToken cancellationToken)
    {
        var result = await management.UpdateMemberAsync(User.GetUserId()!.Value, memberId, role, sites ?? [], cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Team_Updated"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{memberId:guid}/retirer")]
    public async Task<IActionResult> Remove(Guid memberId, CancellationToken cancellationToken)
    {
        var result = await management.RemoveMemberAsync(User.GetUserId()!.Value, memberId, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Team_Removed"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("validation")]
    public async Task<IActionResult> Approval(bool required, CancellationToken cancellationToken)
    {
        var result = await management.SetApprovalAsync(User.GetUserId()!.Value, required, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess
            ? this.Localizer()[required ? "Team_ApprovalOn" : "Team_ApprovalOff"].Value
            : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    private MembershipClaim ManagedAccount() =>
        User.GetMemberships().First(m => m.Kind == AccountKind.Business && m.Role == AccountRole.Manager);

    private async Task<TeamViewModel> BuildModelAsync(InviteMemberViewModel invite, CancellationToken cancellationToken)
    {
        var team = await getTeam.HandleAsync(ManagedAccount().AccountId, cancellationToken);
        return new TeamViewModel
        {
            AccountName = team?.AccountName ?? string.Empty,
            Members = team?.Members ?? [],
            Invite = invite,
            Sites = team?.Sites ?? [],
            RequiresApproval = team?.RequiresApproval ?? false,
            CurrentUserId = User.GetUserId()!.Value,
        };
    }
}
