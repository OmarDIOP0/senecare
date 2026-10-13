using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Business;
using SeneCare.Application.Development;
using SeneCare.Application.Properties;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>« Choisir mon offre »: Essentiel, Professionnel or Premium, as configured by admins.</summary>
[Authorize(Policy = Policies.OrganizationManager)]
[Route("/entreprise/offre")]
public sealed class BusinessPlanController(PlanCatalog catalog, ChoosePlanHandler choosePlan, BusinessAccess access, PropertyQueries properties) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var member = await access.OfAsync(User.GetUserId()!.Value, cancellationToken);
        if (member is not null && await access.SubscriptionAsync(member.Account.Id, cancellationToken) is not null)
        {
            return Redirect("/abonnement");
        }

        ViewData["SiteCount"] = (await properties.ListAsync(User.GetUserId()!.Value, cancellationToken)).Count;
        return View(await catalog.BusinessOffersAsync(cancellationToken));
    }

    [HttpPost("")]
    public async Task<IActionResult> Choose(string? plan, CancellationToken cancellationToken)
    {
        var result = await choosePlan.HandleAsync(User.GetUserId()!.Value, plan, cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
            return RedirectToAction(nameof(Index));
        }

        TempData["flash.toast"] = this.Localizer()["Offer_Chosen"].Value;
        return Redirect("/abonnement");
    }
}

/// <summary>Site manager / manager approves or refuses an employee's request.</summary>
[Authorize(Policy = Policies.OrganizationMember)]
[Route("/entreprise/demandes")]
public sealed class BusinessApprovalController(RequestApprovalHandler approvals) : Controller
{
    [HttpPost("{id:guid}/approuver")]
    public async Task<IActionResult> Approve(Guid id, string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await approvals.ApproveAsync(User.GetUserId()!.Value, id, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Approval_Approved"].Value : this.ErrorMessage(result.Error);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/espace");
    }

    [HttpPost("{id:guid}/refuser")]
    public async Task<IActionResult> Reject(Guid id, string? reason, string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await approvals.RejectAsync(User.GetUserId()!.Value, id, reason, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Approval_Rejected"].Value : this.ErrorMessage(result.Error);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/espace");
    }
}

/// <summary>Quarterly diagnostics: planning, report (page + PDF), anomaly → request.</summary>
[Authorize(Policy = Policies.OrganizationMember)]
[Route("/entreprise/diagnostics")]
public sealed class BusinessDiagnosticsController(
    DiagnosticQueries queries,
    ConvertAnomalyHandler convert,
    DiagnosticSimulator simulator,
    IPdfRenderer pdf,
    IWebHostEnvironment environment) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["CanSimulate"] = environment.IsDevelopment();
        return View(await queries.ListAsync(User.GetUserId()!.Value, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        var detail = await queries.DetailAsync(User.GetUserId()!.Value, id, cancellationToken);
        return detail is null ? NotFound() : View(detail);
    }

    [HttpGet("{id:guid}/rapport.pdf")]
    public async Task<IActionResult> Report(Guid id, CancellationToken cancellationToken)
    {
        var detail = await queries.DetailAsync(User.GetUserId()!.Value, id, cancellationToken);
        if (detail is not { IsReportAvailable: true })
        {
            return NotFound();
        }

        var bytes = pdf.DiagnosticReport(BusinessDocuments.Diagnostic(detail, this.Localizer()));
        return File(bytes, "application/pdf", $"diagnostic-{detail.Slot.Period.Start:yyyy-MM}.pdf");
    }

    [HttpPost("{id:guid}/points/{pointId:guid}/demande")]
    public async Task<IActionResult> Convert(Guid id, Guid pointId, CancellationToken cancellationToken)
    {
        var result = await convert.HandleAsync(User.GetUserId()!.Value, id, pointId, cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
            return RedirectToAction(nameof(Show), new { id });
        }

        TempData["flash.toast"] = this.Localizer()["Diagnostic_Converted"].Value;
        return Redirect($"/interventions/{result.Value}");
    }

    /// <summary>Development only: the demo technician carries out the planned diagnostic.</summary>
    [HttpPost("simulation")]
    public async Task<IActionResult> Simulate(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await simulator.CompleteAsync(User.GetUserId()!.Value, cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["flash.toast"] = this.ErrorMessage(result.Error);
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Show), new { id = result.Value });
    }
}

/// <summary>CSV (Excel, French separators) and PDF exports of requests and invoices.</summary>
[Authorize(Policy = Policies.OrganizationMember)]
[Route("/entreprise/exports")]
public sealed class BusinessExportsController(BusinessExports exports, IPdfRenderer pdf, TimeProvider clock) : Controller
{
    [HttpGet("demandes.csv")]
    public async Task<IActionResult> RequestsCsv(string? du, string? au, CancellationToken cancellationToken)
    {
        var (since, until) = Range(du, au);
        var data = await exports.RequestsAsync(User.GetUserId()!.Value, since, until, cancellationToken);
        if (data is not { } export)
        {
            return NotFound();
        }

        var csv = BusinessDocuments.RequestsCsv(export.Rows, this.Localizer(), clock.GetUtcNow());
        return File(csv, "text/csv; charset=utf-8", $"demandes-{since:yyyy-MM-dd}-{until.AddDays(-1):yyyy-MM-dd}.csv");
    }

    [HttpGet("demandes.pdf")]
    public async Task<IActionResult> RequestsPdf(string? du, string? au, CancellationToken cancellationToken)
    {
        var (since, until) = Range(du, au);
        var data = await exports.RequestsAsync(User.GetUserId()!.Value, since, until, cancellationToken);
        if (data is not { } export)
        {
            return NotFound();
        }

        var report = BusinessDocuments.Requests(export.AccountName, export.Rows, since, until, this.Localizer(), clock.GetUtcNow());
        return File(pdf.RequestsReport(report), "application/pdf", $"demandes-{since:yyyy-MM-dd}-{until.AddDays(-1):yyyy-MM-dd}.pdf");
    }

    [HttpGet("factures.csv")]
    [Authorize(Policy = Policies.OrganizationManager)]
    public async Task<IActionResult> InvoicesCsv(CancellationToken cancellationToken)
    {
        var data = await exports.InvoicesAsync(User.GetUserId()!.Value, cancellationToken);
        if (data is not { } export)
        {
            return NotFound();
        }

        return File(BusinessDocuments.InvoicesCsv(export.Rows, this.Localizer()), "text/csv; charset=utf-8", "factures.csv");
    }

    /// <summary>Days in « yyyy-MM-dd » (Dakar = UTC); default: the last 3 months, end date included.</summary>
    private (DateTimeOffset Since, DateTimeOffset Until) Range(string? du, string? au)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var end = DateOnly.TryParseExact(au, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : today;
        var start = DateOnly.TryParseExact(du, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s : end.AddMonths(-3);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return (new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }
}

/// <summary>« Mes sites »: the company's buildings (same health book as homes).</summary>
[Authorize(Policy = Policies.OrganizationMember)]
public sealed class BusinessSitesController(PropertyQueries properties) : Controller
{
    [HttpGet("/entreprise/sites")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View("~/Views/Properties/Index.cshtml", await properties.ListAsync(User.GetUserId()!.Value, cancellationToken));

    /// <summary>Invoices live with the subscription (same Wave payment flow as households).</summary>
    [HttpGet("/entreprise/factures")]
    [Authorize(Policy = Policies.OrganizationManager)]
    public IActionResult Invoices() => Redirect("/abonnement");
}
