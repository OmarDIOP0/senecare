using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Development;
using SeneCare.Application.Media;
using SeneCare.Application.Payments;
using SeneCare.Application.Properties;
using SeneCare.Application.Requests;
using SeneCare.Application.Subscriptions;
using SeneCare.Domain.Common;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.Localization;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Controllers;

/// <summary>« Mes logements »: list, add (map + landmark + photos), health record.</summary>
[Authorize]
[Route("/logements")]
public sealed class PropertiesController(PropertyQueries queries, AddPropertyHandler addProperty) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await queries.ListAsync(User.GetUserId()!.Value, cancellationToken));

    [HttpGet("nouveau")]
    public IActionResult Create() =>
        View(new PropertyFormModel { Kind = AppNavigation.IsBusiness(User) ? Domain.Accounts.PropertyKind.Office : Domain.Accounts.PropertyKind.House });

    [HttpPost("nouveau")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Create(PropertyFormModel form, List<IFormFile> photos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var result = await addProperty.HandleAsync(
            new AddPropertyCommand(
                User.GetUserId()!.Value, form.Kind, form.Name, form.NeighborhoodId,
                PropertyFormModel.ParseCoordinate(form.Latitude), PropertyFormModel.ParseCoordinate(form.Longitude),
                form.Landmark, form.Details, [.. photos.Select(p => new UploadedFile(p.OpenReadStream(), p.Length))]),
            cancellationToken);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, this.ErrorMessage(result.Error));
            return View(form);
        }

        TempData["flash.toast"] = this.Localizer()["Property_Added"].Value;
        return RedirectToAction(nameof(Show), new { id = result.Value });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        var book = await queries.HealthBookAsync(User.GetUserId()!.Value, id, cancellationToken);
        return book is null ? NotFound() : View(book);
    }
}

/// <summary>« Signaler » in 3 steps. Works offline: the page is kept on the phone, the form is queued.</summary>
[Authorize]
[Route("/signaler")]
public sealed class ReportController(PropertyQueries properties, CreateRequestHandler create, TimeProvider clock) : Controller
{
    [HttpGet("")]
    [OfflinePage]
    public async Task<IActionResult> Index(Guid? logement, CancellationToken cancellationToken) =>
        View(await BuildAsync(new ReportFormModel { PropertyId = logement }, cancellationToken));

    [HttpPost("")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Index(ReportFormModel form, List<IFormFile> photos, IFormFile? voiceNote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var slots = form.Slots
            .Where(s => s > clock.GetUtcNow())
            .Select(s => new TimeSlot(s, s.AddHours(4)))
            .ToList();
        var result = await create.HandleAsync(
            new CreateRequestCommand(
                User.GetUserId()!.Value, form.PropertyId, form.Domain, form.Description, form.Urgency, slots,
                [.. photos.Select(p => new UploadedFile(p.OpenReadStream(), p.Length))],
                voiceNote is { Length: > 0 } ? new UploadedFile(voiceNote.OpenReadStream(), voiceNote.Length) : null,
                form.ClientReference),
            cancellationToken);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, this.ErrorMessage(result.Error));
            return View(await BuildAsync(form, cancellationToken));
        }

        TempData["flash.toast"] = this.Localizer()["Report_Sent", result.Value.Number].Value;
        return RedirectToAction(nameof(InterventionsController.Show), "Interventions", new { id = result.Value.Id });
    }

    private async Task<ReportPageModel> BuildAsync(ReportFormModel form, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var homes = await properties.ListAsync(userId, cancellationToken);
        var active = homes.Where(h => h.SubscriptionStatus is Domain.Billing.SubscriptionStatus.Active or Domain.Billing.SubscriptionStatus.PastDue).ToList();
        form.PropertyId ??= active.FirstOrDefault()?.Id;
        form.ClientReference ??= Guid.NewGuid().ToString("N");

        string? blocker = homes.Count == 0 ? "Report_NoProperty" : active.Count == 0 ? "Report_NoSubscription" : null;
        return new ReportPageModel
        {
            Properties = active,
            SlotChoices = SlotChoices(clock.GetUtcNow()),
            Form = form,
            Blocker = blocker,
        };
    }

    /// <summary>Next six working days, morning (8 h–12 h) or afternoon (14 h–18 h), Dakar time = UTC.</summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> SlotChoices(DateTimeOffset now)
    {
        var choices = new List<(DateTimeOffset, DateTimeOffset)>();
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        while (choices.Count < 8)
        {
            if (day.DayOfWeek != DayOfWeek.Sunday)
            {
                choices.Add((day.AddHours(8), day.AddHours(12)));
                choices.Add((day.AddHours(14), day.AddHours(18)));
            }

            day = day.AddDays(1);
        }

        return choices;
    }
}

/// <summary>Follow-up of requests: timeline, slot choice, contact, validation, dispute.</summary>
[Authorize]
[Route("/interventions")]
public sealed class InterventionsController(
    RequestQueries queries,
    ClientRequestActions actions,
    RequestSimulator simulator,
    IPdfRenderer pdf,
    IWebHostEnvironment environment) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await queries.ListAsync(User.GetUserId()!.Value, cancellationToken));

    /// <summary>Intervention report (generated from the technician's notes, checklist and parts).</summary>
    [HttpGet("{id:guid}/rapport.pdf")]
    public async Task<IActionResult> Report(Guid id, CancellationToken cancellationToken)
    {
        var detail = await queries.DetailAsync(User.GetUserId()!.Value, id, cancellationToken);
        if (detail?.Report is null)
        {
            return NotFound();
        }

        return File(pdf.FieldReport(InterventionDocuments.Report(detail, this.Localizer())), "application/pdf", $"rapport-{detail.Summary.Number}.pdf");
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        var detail = await queries.DetailAsync(User.GetUserId()!.Value, id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return View(new RequestPageModel
        {
            Detail = detail,
            Timeline = RequestTimeline.Build(detail.Summary.Status, detail.History, detail.Summary.AppointmentAt, this.Localizer()),
            CanSimulate = environment.IsDevelopment(),
        });
    }

    [HttpPost("{id:guid}/creneau")]
    public async Task<IActionResult> ChooseSlot(Guid id, DateTimeOffset slot, CancellationToken cancellationToken) =>
        Done(id, await actions.ChooseSlotAsync(User.GetUserId()!.Value, id, slot, cancellationToken), "Request_SlotChosen");

    [HttpPost("{id:guid}/valider")]
    public async Task<IActionResult> Validate(Guid id, int score, string? comment, bool publish, CancellationToken cancellationToken) =>
        Done(id, await actions.ValidateAsync(User.GetUserId()!.Value, id, score, comment, publish, cancellationToken), "Request_Validated");

    [HttpPost("{id:guid}/contester")]
    public async Task<IActionResult> Dispute(Guid id, string? reason, CancellationToken cancellationToken) =>
        Done(id, await actions.DisputeAsync(User.GetUserId()!.Value, id, reason, cancellationToken), "Request_Disputed");

    [HttpPost("{id:guid}/annuler")]
    public async Task<IActionResult> Cancel(Guid id, string? reason, CancellationToken cancellationToken) =>
        Done(id, await actions.CancelAsync(User.GetUserId()!.Value, id, reason, cancellationToken), "Request_Cancelled");

    /// <summary>Development only: plays the dispatcher / technician side (Phases 8–9 build the real tools).</summary>
    [HttpPost("{id:guid}/simulation/{step}")]
    public async Task<IActionResult> Simulate(Guid id, SimulationStep step, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || await queries.DetailAsync(User.GetUserId()!.Value, id, cancellationToken) is null)
        {
            return NotFound();
        }

        return Done(id, await simulator.RunAsync(id, step, cancellationToken), "Simulation_Done");
    }

    private RedirectToActionResult Done(Guid id, Application.Common.Result<Guid> result, string successKey)
    {
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()[successKey].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Show), new { id });
    }
}

/// <summary>« Mon abonnement »: status, due dates, invoices. Businesses: the manager only.</summary>
[Authorize(Policy = Policies.Billing)]
[Route("/abonnement")]
public sealed class SubscriptionController(
    SubscriptionQueries queries,
    PrepareInvoiceHandler prepareInvoice,
    DeclarePaymentHandler declarePayment,
    SimulatedActivation activation,
    IWebHostEnvironment environment) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var dues = new Dictionary<Guid, InvoiceDue>();
        foreach (var subscription in await queries.ListAsync(userId, cancellationToken))
        {
            if (await prepareInvoice.HandleAsync(userId, subscription.Id, cancellationToken) is { } due)
            {
                dues[subscription.Id] = due;
            }
        }

        // Read again: preparing may have just issued an invoice, which must appear in the list.
        var items = (await queries.ListAsync(userId, cancellationToken))
            .Select(s => dues.TryGetValue(s.Id, out var due)
                ? new SubscriptionPageItem(s, due, due.WaveUrl is { } url ? QrCode.Svg(url) : null)
                : new SubscriptionPageItem(s, null, null))
            .ToList();

        if (items.Count == 0 && AppNavigation.IsBusinessManager(User))
        {
            return Redirect("/entreprise/offre");
        }

        ViewData["CanSimulate"] = environment.IsDevelopment();
        return View(items);
    }

    /// <summary>« J'ai payé » after paying with the Wave link: a Finance admin checks and activates.</summary>
    [HttpPost("factures/{invoiceId:guid}/paiement")]
    public async Task<IActionResult> DeclarePayment(Guid invoiceId, string? reference, CancellationToken cancellationToken)
    {
        var result = await declarePayment.HandleAsync(User.GetUserId()!.Value, invoiceId, reference, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Payment_Declared"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Development only, until payments (Phase 6).</summary>
    [HttpPost("{id:guid}/activer-simulation")]
    public async Task<IActionResult> SimulateActivation(Guid id, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await activation.ActivateAsync(User.GetUserId()!.Value, id, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Subscription_Activated"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Photos, voice notes and documents, only for people allowed to see them.</summary>
[Authorize]
public sealed class MediaController(MediaAccess media, IAuthorizationService authorization) : Controller
{
    [HttpGet("/fichiers/{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        // Technicians are not « back-office »: they only see the files of the customers they work for.
        var isBackOffice = (await authorization.AuthorizeAsync(User, Policies.BackOffice)).Succeeded;
        var content = await media.OpenAsync(User.GetUserId()!.Value, isBackOffice, id, cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        // Private: may be kept by this browser, never by shared caches.
        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(content.Content, content.ContentType);
    }
}

internal static class ControllerLocalizerExtensions
{
    public static IStringLocalizer<SharedResource> Localizer(this Controller controller) =>
        controller.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
}
