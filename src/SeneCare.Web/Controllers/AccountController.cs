using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SeneCare.Application.Accounts;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

[Authorize]
public sealed class AccountController(GetUserProfileHandler getProfile) : Controller
{
    [HttpGet("/compte")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await getProfile.HandleAsync(User.GetUserId()!.Value, cancellationToken);
        return profile is null ? Challenge() : View(profile);
    }
}

/// <summary>Business manager: team members and employee invitations.</summary>
[Authorize(Policy = Policies.OrganizationManager)]
[Route("/entreprise/equipe")]
public sealed class TeamController(GetTeamHandler getTeam, InviteMemberHandler inviteMember) : Controller
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
            new InviteMemberCommand(User.GetUserId()!.Value, account.AccountId, invite.Phone, invite.Role, [], urlTemplate),
            cancellationToken);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError("Invite.Phone", this.ErrorMessage(result.Error));
            return View(await BuildModelAsync(invite, cancellationToken));
        }

        TempData["flash.success"] = result.Value.Phone.Formatted;
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
        };
    }
}
