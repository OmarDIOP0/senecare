using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SeneCare.Application.Locations;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>« Prévenez-moi »: someone outside the covered zones leaves a number to be told when SENECARE opens there.</summary>
[AllowAnonymous]
[Route("/zones/attente")]
public sealed class ZonesController(WaitlistHandler waitlist) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? commune, CancellationToken cancellationToken)
    {
        ViewData["Regions"] = await waitlist.RegionsAsync(cancellationToken);
        ViewData["Commune"] = commune is { } id ? await waitlist.CommuneAsync(id, cancellationToken) : null;
        return View();
    }

    [HttpPost("")]
    [EnableRateLimiting(RateLimits.Otp)]
    public async Task<IActionResult> Join(string? phone, string? fullName, Guid regionId, Guid? communeId, string? locality, CancellationToken cancellationToken)
    {
        var result = await waitlist.JoinAsync(new JoinWaitlistCommand(phone, fullName, regionId, communeId, locality), cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.error"] = this.ErrorMessage(result.Error);
            return Redirect(communeId is { } id ? $"/zones/attente?commune={id}" : "/zones/attente");
        }

        TempData["flash.toast"] = this.Localizer()["Zones_Joined"].Value;
        return Redirect("/");
    }
}
