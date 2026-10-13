using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SeneCare.Application.Admin;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Hubs;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>Back-office home: the figures SENECARE watches every day.</summary>
[Authorize(Policy = Policies.BackOffice)]
[Route("/admin")]
public sealed class AdminHomeController(AdminDashboardQuery dashboard) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await dashboard.GetAsync(cancellationToken));
}

/// <summary>
/// Dispatch: analyse, qualify, propose slots, assign technicians (with suggestions), calendar and map.
/// Desktop first, usable on a tablet.
/// </summary>
[Authorize(Policy = Policies.AdminDispatcher)]
[Route("/admin/dispatch")]
public sealed class DispatchController(
    DispatchQueries queries,
    DispatchActions actions,
    DiagnosticSupervision diagnostics,
    IHubContext<TrackingHub> hub,
    TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(DispatchStage? etape, CancellationToken cancellationToken)
    {
        var queue = await queries.QueueAsync(cancellationToken);
        ViewData["Stage"] = etape;
        ViewData["Counts"] = queue.GroupBy(i => i.Stage).ToDictionary(g => g.Key, g => g.Count());
        return View(etape is { } stage ? [.. queue.Where(i => i.Stage == stage)] : queue.Where(i => i.Stage != DispatchStage.InProgress).ToList());
    }

    [HttpGet("calendrier")]
    public async Task<IActionResult> Calendar(string? semaine, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var start = DateOnly.TryParseExact(semaine, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : today;
        start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7)); // Monday
        ViewData["WeekStart"] = start;
        return View(await queries.CalendarAsync(start, cancellationToken));
    }

    [HttpGet("carte")]
    public async Task<IActionResult> Map(CancellationToken cancellationToken) =>
        View((await queries.QueueAsync(cancellationToken)).Where(i => i.Latitude is not null).ToList());

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        var detail = await queries.DetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : View(detail);
    }

    [HttpPost("{id:guid}/prendre")]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken) =>
        await DoneAsync(id, await actions.StartReviewAsync(Me, id, cancellationToken), "Dispatch_Taken", cancellationToken);

    [HttpPost("{id:guid}/urgence")]
    public async Task<IActionResult> Requalify(Guid id, Urgency urgency, CancellationToken cancellationToken) =>
        await DoneAsync(id, await actions.RequalifyAsync(Me, id, urgency, cancellationToken), "Dispatch_Requalified", cancellationToken);

    /// <summary>Slots from « datetime-local » inputs (Dakar time = UTC).</summary>
    [HttpPost("{id:guid}/creneaux")]
    public async Task<IActionResult> Propose(Guid id, List<string> slots, CancellationToken cancellationToken)
    {
        var starts = (slots ?? [])
            .Select(s => DateTime.TryParseExact(s, ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
                ? new DateTimeOffset(at, TimeSpan.Zero)
                : (DateTimeOffset?)null)
            .OfType<DateTimeOffset>()
            .ToList();
        return await DoneAsync(id, await actions.ProposeSlotsAsync(Me, id, starts, cancellationToken), "Dispatch_SlotsProposed", cancellationToken);
    }

    [HttpPost("{id:guid}/maintenant")]
    public async Task<IActionResult> DispatchNow(Guid id, Guid technicianId, CancellationToken cancellationToken) =>
        await DoneAsync(id, await actions.DispatchNowAsync(Me, id, technicianId, cancellationToken), "Dispatch_Sent", cancellationToken);

    [HttpPost("{id:guid}/affecter")]
    public async Task<IActionResult> Assign(Guid id, Guid technicianId, CancellationToken cancellationToken) =>
        await DoneAsync(id, await actions.AssignAsync(id, technicianId, cancellationToken), "Dispatch_Assigned", cancellationToken);

    [HttpPost("{id:guid}/rouvrir")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken) =>
        await DoneAsync(id, await actions.ReopenAsync(Me, id, cancellationToken), "Dispatch_Reopened", cancellationToken);

    [HttpPost("{id:guid}/annuler")]
    public async Task<IActionResult> Cancel(Guid id, string? reason, CancellationToken cancellationToken) =>
        await DoneAsync(id, await actions.CancelAsync(Me, id, reason, cancellationToken), "Dispatch_Cancelled", cancellationToken);

    // ---- Quarterly diagnostics ----

    [HttpGet("diagnostics")]
    public async Task<IActionResult> Diagnostics(CancellationToken cancellationToken)
    {
        ViewData["Technicians"] = await queries.TechniciansAsync(cancellationToken);
        return View(await diagnostics.ListAsync(cancellationToken));
    }

    [HttpGet("diagnostics/{id:guid}")]
    public async Task<IActionResult> Diagnostic(Guid id, CancellationToken cancellationToken)
    {
        var detail = await diagnostics.DetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Technicians"] = await queries.TechniciansAsync(cancellationToken);
        return View(detail.Value);
    }

    [HttpPost("diagnostics/{id:guid}/affecter")]
    public async Task<IActionResult> AssignDiagnostic(Guid id, Guid technicianId, CancellationToken cancellationToken) =>
        DiagnosticDone(id, await diagnostics.AssignAsync(id, technicianId, cancellationToken), "Dispatch_Assigned");

    [HttpPost("diagnostics/{id:guid}/replanifier")]
    public async Task<IActionResult> Reschedule(Guid id, string? date, CancellationToken cancellationToken)
    {
        if (!DateTime.TryParseExact(date, ["yyyy-MM-ddTHH:mm", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            return DiagnosticDone(id, Result<Guid>.Failure("dispatch.past_date"), "Dispatch_Rescheduled");
        }

        return DiagnosticDone(id, await diagnostics.RescheduleAsync(id, new DateTimeOffset(at, TimeSpan.Zero), cancellationToken), "Dispatch_Rescheduled");
    }

    [HttpPost("diagnostics/{id:guid}/valider")]
    public async Task<IActionResult> ValidateDiagnostic(Guid id, CancellationToken cancellationToken) =>
        DiagnosticDone(id, await diagnostics.ValidateAsync(Me, id, cancellationToken), "Dispatch_DiagnosticValidated", back: false);

    [HttpPost("diagnostics/{id:guid}/renvoyer")]
    public async Task<IActionResult> ReturnDiagnostic(Guid id, CancellationToken cancellationToken) =>
        DiagnosticDone(id, await diagnostics.ReturnAsync(id, cancellationToken), "Dispatch_DiagnosticReturned", back: false);

    private Guid Me => User.GetUserId()!.Value;

    private async Task<IActionResult> DoneAsync(Guid id, Result<Guid> result, string successKey, CancellationToken cancellationToken)
    {
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()[successKey].Value : this.ErrorMessage(result.Error);
        if (result.IsSuccess)
        {
            // The customer's page refreshes by itself (new slots, technician assigned…).
            await hub.Clients.Group(TrackingHub.Group(id)).SendAsync("status", string.Empty, cancellationToken);
        }

        return RedirectToAction(nameof(Show), new { id });
    }

    private RedirectToActionResult DiagnosticDone(Guid id, Result<Guid> result, string successKey, bool back = true)
    {
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()[successKey].Value : this.ErrorMessage(result.Error);
        return back || !result.IsSuccess ? RedirectToAction(nameof(Diagnostic), new { id }) : RedirectToAction(nameof(Diagnostics));
    }
}
