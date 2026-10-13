using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Users;
using SeneCare.Infrastructure.Identity;

namespace SeneCare.Web.Controllers;

/// <summary>Content of the topbar notification panel, loaded when the bell is opened.</summary>
[Authorize]
public sealed class NotificationsController(NotificationInbox inbox) : Controller
{
    [HttpGet("/notifications")]
    public async Task<IActionResult> Panel(CancellationToken cancellationToken) =>
        PartialView("Ui/_Notifications", await inbox.OpenAsync(User.GetUserId()!.Value, cancellationToken));
}
