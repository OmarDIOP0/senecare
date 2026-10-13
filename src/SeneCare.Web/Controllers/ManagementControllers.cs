using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Application.Admin;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Infrastructure.Identity;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>« Gestion » hub: links to the screens the admin's sub-role may open.</summary>
[Authorize(Policy = Policies.BackOffice)]
[Route("/admin/gestion")]
public sealed class ManagementController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}

/// <summary>Households and companies (support, finance): search and customer file.</summary>
[Authorize(Policy = Policies.BackOffice)]
[Route("/admin/clients")]
public sealed class CustomersController(CustomerDirectory directory) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, AccountKind? type, CancellationToken cancellationToken)
    {
        ViewData["Query"] = q;
        ViewData["Type"] = type;
        return View(await directory.SearchAsync(q, type, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        var file = await directory.FileAsync(id, cancellationToken);
        return file is null ? NotFound() : View(file);
    }
}

/// <summary>Technicians: trades, zones, working week, activation, certification.</summary>
[Authorize(Policy = Policies.AdminDispatcher)]
[Route("/admin/techniciens")]
public sealed class TechniciansController(TechnicianManagement technicians, GeographyManagement geography) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Communes"] = await geography.CommunesAsync(null, cancellationToken);
        return View(await technicians.ListAsync(cancellationToken));
    }

    [HttpPost("")]
    public async Task<IActionResult> Create(
        string? phone, string? fullName, List<ServiceDomain> skills, List<Guid> communes, List<DayOfWeek> days, string? start, string? end, CancellationToken cancellationToken)
    {
        var result = await technicians.CreateAsync(Me, Form(phone, fullName, skills, communes, days, start, end), cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Admin_TechCreated"].Value : this.ErrorMessage(result.Error);
        return result.IsSuccess ? RedirectToAction(nameof(Edit), new { id = result.Value }) : RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var technician = (await technicians.ListAsync(cancellationToken)).FirstOrDefault(t => t.Id == id);
        if (technician is null)
        {
            return NotFound();
        }

        ViewData["Communes"] = await geography.CommunesAsync(null, cancellationToken);
        return View(technician);
    }

    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, List<ServiceDomain> skills, List<Guid> communes, List<DayOfWeek> days, string? start, string? end, CancellationToken cancellationToken) =>
        Done(id, await technicians.UpdateAsync(Me, id, Form(null, null, skills, communes, days, start, end), cancellationToken), "Admin_Saved");

    [HttpPost("{id:guid}/statut")]
    public async Task<IActionResult> SetActive(Guid id, bool active, CancellationToken cancellationToken) =>
        Done(id, await technicians.SetActiveAsync(Me, id, active, cancellationToken), "Admin_Saved");

    [HttpPost("{id:guid}/certifier")]
    public async Task<IActionResult> Certify(Guid id, CancellationToken cancellationToken) =>
        Done(id, await technicians.CertifyAsync(Me, id, cancellationToken), "Admin_Saved");

    private Guid Me => User.GetUserId()!.Value;

    private static TechnicianForm Form(string? phone, string? name, List<ServiceDomain> skills, List<Guid> communes, List<DayOfWeek> days, string? start, string? end) =>
        new(phone, name, skills ?? [], communes ?? [], days ?? [], Time(start), Time(end));

    private static TimeOnly? Time(string? value) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private RedirectToActionResult Done(Guid id, Result<Guid> result, string successKey)
    {
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()[successKey].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Edit), new { id });
    }
}

/// <summary>Offers and prices (super-admin): every value is data; existing subscriptions keep their price.</summary>
[Authorize(Policy = Policies.SuperAdmin)]
[Route("/admin/offres")]
public sealed class PlansController(PlanManagement plans) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await plans.ListAsync(cancellationToken));

    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, string? name, long registrationFee, long monthlyPrice, int priority, string? visits,
        int criticalHours, int highHours, int normalHours, List<ServiceDomain> domains, bool isActive, CancellationToken cancellationToken)
    {
        int? included = int.TryParse(visits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
        var result = await plans.UpdateAsync(
            User.GetUserId()!.Value, id,
            new PlanForm(name, registrationFee, monthlyPrice, priority, included, criticalHours, highHours, normalHours, domains ?? [], isActive),
            cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Admin_Saved"].Value : this.ErrorMessage(result.Error);
        return Redirect("/admin/offres#plan-" + id);
    }
}

/// <summary>Geographic reference data (super-admin): communes and their neighborhoods.</summary>
[Authorize(Policy = Policies.SuperAdmin)]
[Route("/admin/referentiel")]
public sealed class GeographyController(GeographyManagement geography) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        ViewData["Query"] = q;
        return View(await geography.CommunesAsync(q, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Commune(Guid id, CancellationToken cancellationToken)
    {
        var commune = await geography.CommuneAsync(id, cancellationToken);
        return commune is null ? NotFound() : View(commune.Value);
    }

    [HttpPost("{id:guid}/quartiers")]
    public async Task<IActionResult> AddNeighborhood(Guid id, string? name, CancellationToken cancellationToken)
    {
        var result = await geography.AddNeighborhoodAsync(User.GetUserId()!.Value, id, name, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Admin_Saved"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Commune), new { id });
    }

    [HttpPost("{id:guid}/quartiers/{neighborhoodId:guid}")]
    public async Task<IActionResult> Rename(Guid id, Guid neighborhoodId, string? name, CancellationToken cancellationToken)
    {
        var result = await geography.RenameNeighborhoodAsync(User.GetUserId()!.Value, neighborhoodId, name, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Admin_Saved"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Commune), new { id });
    }
}

/// <summary>Platform settings (super-admin).</summary>
[Authorize(Policy = Policies.SuperAdmin)]
[Route("/admin/parametres")]
public sealed class SettingsController(SettingsManagement settings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await settings.ListAsync(cancellationToken));

    [HttpPost("")]
    public async Task<IActionResult> Update(string key, string? value, CancellationToken cancellationToken)
    {
        var result = await settings.UpdateAsync(User.GetUserId()!.Value, key, value, cancellationToken);
        TempData["flash.toast"] = result.IsSuccess ? this.Localizer()["Admin_Saved"].Value : this.ErrorMessage(result.Error);
        return RedirectToAction(nameof(Index));
    }
}
