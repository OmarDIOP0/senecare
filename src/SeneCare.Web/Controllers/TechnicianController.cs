using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SeneCare.Application.Common;
using SeneCare.Application.Media;
using SeneCare.Application.Technicians;
using SeneCare.Application.Users;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Hubs;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>
/// Technician app (field use: one hand, full sun, weak network). Pages are kept on the phone for the
/// day; actions done offline are queued by the service worker and replayed (they are idempotent).
/// </summary>
[Authorize(Policy = Policies.Technician)]
[Route("/technicien")]
public sealed class TechnicianController(
    MissionQueries missions,
    MissionActions actions,
    DiagnosticFieldHandler diagnostics,
    TourProgressHandler tours,
    IHubContext<TrackingHub> hub) : Controller
{
    [HttpGet("")]
    [OfflinePage]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        if (!await tours.HasCompletedAsync(userId, TourKeys.Technician, cancellationToken))
        {
            ViewData["TourJson"] = SpaceController.BuildTourJson(TourKeys.Technician, this.Localizer());
        }

        return View(await missions.TodayAsync(userId, cancellationToken));
    }

    [HttpGet("rapports")]
    public async Task<IActionResult> History(CancellationToken cancellationToken) =>
        View(await missions.HistoryAsync(User.GetUserId()!.Value, cancellationToken));

    [HttpGet("missions/{id:guid}")]
    [OfflinePage]
    public async Task<IActionResult> Mission(Guid id, CancellationToken cancellationToken)
    {
        var mission = await missions.DetailAsync(User.GetUserId()!.Value, id, cancellationToken);
        return mission is null ? NotFound() : View(mission);
    }

    [HttpPost("missions/{id:guid}/etape/{step}")]
    public async Task<IActionResult> Step(Guid id, string step, CancellationToken cancellationToken)
    {
        FieldStep? fieldStep = step switch
        {
            "en-route" => FieldStep.OnTheWay,
            "sur-place" => FieldStep.Arrived,
            "en-cours" => FieldStep.Start,
            _ => null,
        };
        if (fieldStep is null)
        {
            return NotFound();
        }

        var result = await actions.StepAsync(User.GetUserId()!.Value, id, fieldStep.Value, cancellationToken);
        if (result.IsSuccess)
        {
            await NotifyCustomerAsync(id, cancellationToken);
        }

        return Back(id, result);
    }

    [HttpPost("missions/{id:guid}/checklist/{itemId:guid}")]
    public async Task<IActionResult> Check(Guid id, Guid itemId, bool done, CancellationToken cancellationToken) =>
        Back(id, await actions.CheckAsync(User.GetUserId()!.Value, id, itemId, done, cancellationToken), anchor: "checklist");

    [HttpPost("missions/{id:guid}/checklist")]
    public async Task<IActionResult> AddCheck(Guid id, string? label, CancellationToken cancellationToken) =>
        Back(id, await actions.AddChecklistItemAsync(User.GetUserId()!.Value, id, label, cancellationToken), anchor: "checklist");

    [HttpPost("missions/{id:guid}/photos")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<IActionResult> Photos(Guid id, string? moment, List<IFormFile> photos, CancellationToken cancellationToken)
    {
        var kind = moment == "apres" ? MediaKind.PhotoAfter : MediaKind.PhotoBefore;
        var result = await actions.AddPhotosAsync(
            User.GetUserId()!.Value, id, kind, [.. photos.Select(p => new UploadedFile(p.OpenReadStream(), p.Length))], cancellationToken);
        return Back(id, result, anchor: "photos");
    }

    [HttpPost("missions/{id:guid}/pieces")]
    public async Task<IActionResult> Part(Guid id, string? name, string? quantity, string? unit, CancellationToken cancellationToken)
    {
        // « 2,5 » (French keyboard) as well as « 2.5 ».
        if (!decimal.TryParse(quantity?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return Back(id, Result<Guid>.Failure("part.quantity"), anchor: "pieces");
        }

        return Back(id, await actions.AddPartAsync(User.GetUserId()!.Value, id, name, amount, unit, cancellationToken), anchor: "pieces");
    }

    [HttpPost("missions/{id:guid}/signature")]
    public async Task<IActionResult> Sign(Guid id, IFormFile? signature, string? signedBy, CancellationToken cancellationToken)
    {
        var file = signature is null ? null : new UploadedFile(signature.OpenReadStream(), signature.Length);
        return Back(id, await actions.SignAsync(User.GetUserId()!.Value, id, file, signedBy, cancellationToken), anchor: "cloture");
    }

    [HttpPost("missions/{id:guid}/terminer")]
    public async Task<IActionResult> Complete(Guid id, string? findings, string? noSignatureReason, CancellationToken cancellationToken)
    {
        var result = await actions.CompleteAsync(User.GetUserId()!.Value, id, findings, noSignatureReason, cancellationToken);
        if (result.IsSuccess)
        {
            await NotifyCustomerAsync(id, cancellationToken);
            TempData["flash.toast"] = this.Localizer()["Tech_Completed"].Value;
            return RedirectToAction(nameof(Index));
        }

        return Back(id, result, anchor: "cloture");
    }

    [HttpGet("diagnostics/{id:guid}")]
    [OfflinePage]
    public async Task<IActionResult> Diagnostic(Guid id, CancellationToken cancellationToken)
    {
        var mission = await diagnostics.DetailAsync(User.GetUserId()!.Value, id, cancellationToken);
        return mission is null ? NotFound() : View(mission);
    }

    [HttpPost("diagnostics/{id:guid}/points")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> AddPoint(
        Guid id, Guid? siteId, ServiceDomain? domain, string? label, ControlPointResult? result, string? comment, IFormFile? photo, CancellationToken cancellationToken)
    {
        var file = photo is null ? null : new UploadedFile(photo.OpenReadStream(), photo.Length);
        var added = await diagnostics.AddPointAsync(User.GetUserId()!.Value, id, siteId, domain, label, result, comment, file, cancellationToken);
        TempData["flash.toast"] = added.IsSuccess ? this.Localizer()["Tech_PointAdded"].Value : this.ErrorMessage(added.Error);
        return Redirect($"/technicien/diagnostics/{id}#points");
    }

    [HttpPost("diagnostics/{id:guid}/remettre")]
    public async Task<IActionResult> Submit(Guid id, string? recommendations, CancellationToken cancellationToken)
    {
        var result = await diagnostics.SubmitAsync(User.GetUserId()!.Value, id, recommendations, cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
            return Redirect($"/technicien/diagnostics/{id}");
        }

        TempData["flash.toast"] = this.Localizer()["Tech_DiagnosticSubmitted"].Value;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>The customer's page listens: it reloads when the technician changes the status.</summary>
    private async Task NotifyCustomerAsync(Guid interventionId, CancellationToken cancellationToken)
    {
        var mission = await missions.DetailAsync(User.GetUserId()!.Value, interventionId, cancellationToken);
        if (mission is not null)
        {
            await hub.Clients.Group(TrackingHub.Group(mission.RequestId)).SendAsync("status", mission.Status.ToString(), cancellationToken);
        }
    }

    private RedirectResult Back(Guid id, Result<Guid> result, string? anchor = null)
    {
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
        }

        return Redirect($"/technicien/missions/{id}" + (anchor is null ? string.Empty : "#" + anchor));
    }
}
