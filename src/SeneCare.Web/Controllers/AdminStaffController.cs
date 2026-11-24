using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Admin;
using SeneCare.Application.Common;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>
/// « Équipe SeneCare » (super-admins): who manages the platform, with which rights; a member may also be a
/// technician (their file stays in Admin › Techniciens). Temporary passwords are shown once (TempData).
/// </summary>
[Authorize(Policy = Policies.SuperAdmin)]
[Route("/admin/equipe")]
public sealed class AdminStaffController(StaffManagement staff) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken, int page = 1) =>
        View(this.Paged(SeneCare.Application.Common.Paging.Of(await staff.ListAsync(cancellationToken), page)));

    [HttpPost("")]
    public async Task<IActionResult> Add(string? phone, string? fullName, List<PlatformRole> roles, CancellationToken cancellationToken)
    {
        var result = await staff.AddAsync(Me, phone, fullName, roles ?? [], cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
            return RedirectToAction(nameof(Index));
        }

        TempData["flash.toast"] = this.Localizer()["Staff_Added"].Value;
        Remember(result.Value);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{userId:guid}/roles")]
    public async Task<IActionResult> Roles(Guid userId, List<PlatformRole> roles, CancellationToken cancellationToken)
    {
        var result = await staff.SetRolesAsync(Me, userId, roles ?? [], cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Staff_RolesSaved"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{userId:guid}/retirer")]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken)
    {
        var result = await staff.RemoveAsync(Me, userId, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Staff_Removed"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{userId:guid}/mot-de-passe")]
    public async Task<IActionResult> ResetPassword(Guid userId, CancellationToken cancellationToken)
    {
        var result = await staff.ResetPasswordAsync(Me, userId, cancellationToken);
        if (result.IsSuccess)
        {
            Remember(result.Value);
        }
        else
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
        }

        return RedirectToAction(nameof(Index));
    }

    private Guid Me => User.GetUserId()!.Value;

    /// <summary>For the « temporary password » panel of the next page (shown once).</summary>
    private void Remember(StaffAdded added) => TemporaryPasswordPanel.Remember(TempData, added);
}
