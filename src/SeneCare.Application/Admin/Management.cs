using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Locations;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Technicians;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Admin;

// ===================================================================== Customers

public sealed record CustomerRow(Guid AccountId, AccountKind Kind, string Name, string? ContactName, string ContactPhone, int Sites, SubscriptionStatus? Status, DateTimeOffset CreatedAt);

public sealed record CustomerMember(Guid UserId, string? Name, string Phone, AccountRole Role);

public sealed record CustomerSite(Guid Id, string Name, PropertyKind Kind, string Place);

public sealed record CustomerSubscription(Guid Id, string Plan, SubscriptionStatus Status, DateOnly? NextDueDate, Money MonthlyPrice);

public sealed record CustomerInvoice(string Number, Money Total, InvoiceStatus Status, DateOnly DueDate);

public sealed record CustomerFile(
    CustomerRow Summary,
    string? Ninea,
    IReadOnlyList<CustomerMember> Members,
    IReadOnlyList<CustomerSite> Sites,
    IReadOnlyList<CustomerSubscription> Subscriptions,
    IReadOnlyList<CustomerInvoice> Invoices,
    int Requests,
    int OpenRequests);

/// <summary>Households and companies, searchable by name or phone (support, finance).</summary>
public sealed class CustomerDirectory(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<CustomerRow>> SearchAsync(string? query, AccountKind? kind, CancellationToken cancellationToken = default)
    {
        var accounts = db.CustomerAccounts.AsNoTracking();
        if (kind is { } k)
        {
            accounts = accounts.Where(a => a.Kind == k);
        }

        var term = query?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // A full number (77 123 45 67) finds its owner; otherwise search names.
            var pattern = "%" + term.ToLowerInvariant().Replace("%", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal) + "%";
            var phone = PhoneNumber.TryParse(term, out var parsed) ? parsed : null;
#pragma warning disable CA1304, CA1311 // Runs in PostgreSQL as lower(): no .NET culture involved.
            var users = phone is null
                ? db.Users.Where(u => u.FullName != null && EF.Functions.Like(u.FullName.ToLower(), pattern))
                : db.Users.Where(u => u.PhoneNumber == phone);
            accounts = accounts.Where(a => EF.Functions.Like(a.DisplayName.ToLower(), pattern)
                || a.Members.Any(m => users.Select(u => u.Id).Contains(m.UserId)));
#pragma warning restore CA1304, CA1311
        }

        var list = await accounts.OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync(cancellationToken);
        return await RowsAsync(list, cancellationToken);
    }

    public async Task<CustomerFile?> FileAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var summary = (await RowsAsync([account], cancellationToken))[0];
        var userIds = account.Members.Select(m => m.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        var sites = await (
                from p in db.Properties
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where p.AccountId == accountId
                orderby p.Name
                select new CustomerSite(p.Id, p.Name, p.Kind, n.Name + ", " + c.Name))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var subscriptions = await (
                from s in db.Subscriptions
                join plan in db.Plans on s.PlanId equals plan.Id
                where s.AccountId == accountId
                orderby s.CreatedAt descending
                select new { s, plan.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.AccountId == accountId && i.Status != InvoiceStatus.Draft)
            .OrderByDescending(i => i.DueDate)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new CustomerFile(
            summary,
            account.Ninea,
            [.. account.Members.Where(m => users.ContainsKey(m.UserId)).Select(m => new CustomerMember(m.UserId, users[m.UserId].FullName, users[m.UserId].PhoneNumber.Formatted, m.Role))],
            sites,
            [.. subscriptions.Select(x => new CustomerSubscription(x.s.Id, x.Name, x.s.Status, x.s.NextDueDate, x.s.MonthlyPrice))],
            [.. invoices.Select(i => new CustomerInvoice(i.Number, i.Total, i.Status, i.DueDate))],
            await db.ServiceRequests.CountAsync(r => r.AccountId == accountId, cancellationToken),
            await db.ServiceRequests.CountAsync(
                r => r.AccountId == accountId && r.Status != Domain.Requests.RequestStatus.ValidatedByCustomer && r.Status != Domain.Requests.RequestStatus.Cancelled,
                cancellationToken));
    }

    private async Task<IReadOnlyList<CustomerRow>> RowsAsync(IReadOnlyList<CustomerAccount> accounts, CancellationToken cancellationToken)
    {
        var ids = accounts.Select(a => a.Id).ToList();
        var contactIds = accounts.Select(a => a.Members.OrderBy(m => m.Role).First().UserId).ToList();
        var contacts = await db.Users.AsNoTracking().Where(u => contactIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        var sites = await db.Properties.AsNoTracking().Where(p => ids.Contains(p.AccountId))
            .GroupBy(p => p.AccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var statuses = await db.Subscriptions.AsNoTracking().Where(s => ids.Contains(s.AccountId))
            .OrderBy(s => s.CreatedAt).Select(s => new { s.AccountId, s.Status }).ToListAsync(cancellationToken);

        return [.. accounts.Select(a =>
        {
            var contact = contacts[a.Members.OrderBy(m => m.Role).First().UserId];
            return new CustomerRow(
                a.Id, a.Kind, a.DisplayName, contact.FullName, contact.PhoneNumber.Formatted, sites.GetValueOrDefault(a.Id),
                statuses.LastOrDefault(s => s.AccountId == a.Id)?.Status, a.CreatedAt);
        })];
    }
}

// ===================================================================== Technicians

public sealed record TechnicianRow(
    Guid Id, string Name, string Phone, bool IsActive, bool IsCertified, IReadOnlyList<ServiceDomain> Skills,
    IReadOnlyList<Guid> CommuneIds, IReadOnlyList<WeeklyAvailability> Week, double? Rating, int OpenMissions);

public sealed record TechnicianForm(
    string? Phone,
    string? FullName,
    IReadOnlyCollection<ServiceDomain> Skills,
    IReadOnlyCollection<Guid> CommuneIds,
    IReadOnlyCollection<DayOfWeek> Days,
    TimeOnly? DayStart,
    TimeOnly? DayEnd);

/// <summary>Technician files: trades, zones (communes), working week, activation and certification.</summary>
public sealed class TechnicianManagement(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<TechnicianRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from t in db.Technicians
                join u in db.Users on t.UserId equals u.Id
                orderby t.IsActive descending, u.FullName
                select new { Technician = t, u.FullName, u.PhoneNumber })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var load = await db.Interventions.AsNoTracking().Where(i => i.CompletedAt == null)
            .GroupBy(i => i.LeadTechnicianId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return [.. rows.Select(r => new TechnicianRow(
            r.Technician.Id, r.FullName ?? string.Empty, r.PhoneNumber.Formatted, r.Technician.IsActive, r.Technician.IsCertified,
            [.. r.Technician.Skills], [.. r.Technician.CommuneIds], [.. r.Technician.WeeklyAvailability], r.Technician.AverageRating,
            load.GetValueOrDefault(r.Technician.Id)))];
    }

    /// <summary>Creates (or promotes) the user with this phone and gives them the technician role.</summary>
    public async Task<Result<Guid>> CreateAsync(Guid adminId, TechnicianForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!PhoneNumber.TryParse(form.Phone, out var phone))
        {
            return Result<Guid>.Failure("phone.invalid");
        }

        var now = clock.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
        if (user is not null && await db.Technicians.AnyAsync(t => t.UserId == user.Id, cancellationToken))
        {
            return Result<Guid>.Failure("technician.exists");
        }

        Technician technician;
        try
        {
            if (user is null)
            {
                user = User.Register(phone, now);
                db.Users.Add(user);
            }

            if (!string.IsNullOrWhiteSpace(form.FullName))
            {
                user.UpdateProfile(form.FullName, user.Email, user.Language);
            }

            if (!user.HasRole(PlatformRole.Technician))
            {
                user.GrantRole(PlatformRole.Technician, now);
            }

            technician = new Technician(user.Id, form.Skills, form.CommuneIds);
            technician.SetWeeklyAvailability(Week(form));
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.Technicians.Add(technician);
        db.AuditLogs.Add(new AuditLog(adminId, "technician.create", nameof(Technician), technician.Id.ToString(), false, null, phone.E164, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(technician.Id);
    }

    public Task<Result<Guid>> UpdateAsync(Guid adminId, Guid technicianId, TechnicianForm form, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, technicianId, "technician.update", t =>
        {
            t.SetSkills(form.Skills);
            t.SetZones(form.CommuneIds);
            t.SetWeeklyAvailability(Week(form));
        }, cancellationToken);

    public Task<Result<Guid>> SetActiveAsync(Guid adminId, Guid technicianId, bool active, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, technicianId, active ? "technician.activate" : "technician.deactivate", t =>
        {
            if (active)
            {
                t.Activate();
            }
            else
            {
                t.Deactivate();
            }
        }, cancellationToken);

    public Task<Result<Guid>> CertifyAsync(Guid adminId, Guid technicianId, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, technicianId, "technician.certify", t => t.Certify(), cancellationToken);

    private static List<WeeklyAvailability> Week(TechnicianForm form) =>
        form.DayStart is { } start && form.DayEnd is { } end
            ? [.. form.Days.Distinct().Select(d => new WeeklyAvailability(d, start, end))]
            : [];

    private async Task<Result<Guid>> ChangeAsync(Guid adminId, Guid technicianId, string action, Action<Technician> change, CancellationToken cancellationToken)
    {
        var technician = await db.Technicians.FirstOrDefaultAsync(t => t.Id == technicianId, cancellationToken);
        if (technician is null)
        {
            return Result<Guid>.Failure("technician.not_found");
        }

        try
        {
            change(technician);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.AuditLogs.Add(new AuditLog(adminId, action, nameof(Technician), technician.Id.ToString(), false, null, null, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(technician.Id);
    }
}

// ===================================================================== Offers and prices

public sealed record PlanForm(
    string? Name,
    long RegistrationFee,
    long MonthlyPrice,
    int Priority,
    int? IncludedVisits,
    int CriticalHours,
    int HighHours,
    int NormalHours,
    IReadOnlyCollection<ServiceDomain> Domains,
    bool IsActive);

/// <summary>
/// Prices and service levels are data edited here, never code. Existing subscriptions keep their price
/// snapshot; new subscriptions (and SLA of new requests) use the new values. Every change is audited.
/// </summary>
public sealed class PlanManagement(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default) =>
        [.. (await db.Plans.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(p => p.Segment).ThenBy(p => p.MonthlyPrice)];

    public async Task<Result<Guid>> UpdateAsync(Guid adminId, Guid planId, PlanForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return Result<Guid>.Failure("plan.not_found");
        }

        if (form.RegistrationFee < 0 || form.MonthlyPrice <= 0)
        {
            return Result<Guid>.Failure("plan.price");
        }

        var before = Describe(plan);
        try
        {
            plan.Update(form.Name ?? string.Empty, Money.FromFcfa(form.RegistrationFee), Money.FromFcfa(form.MonthlyPrice), plan.BillingPeriodMonths, plan.DiagnosticRequired);
            plan.SetServiceLevel(form.Priority, form.IncludedVisits);
            plan.SetSla(Urgency.Critical, TimeSpan.FromHours(form.CriticalHours));
            plan.SetSla(Urgency.High, TimeSpan.FromHours(form.HighHours));
            plan.SetSla(Urgency.Normal, TimeSpan.FromHours(form.NormalHours));
            plan.SetIncludedDomains(form.Domains);
            if (form.IsActive)
            {
                plan.Activate();
            }
            else
            {
                plan.Deactivate();
            }
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.AuditLogs.Add(new AuditLog(adminId, "plan.update", nameof(Plan), plan.Code, false, null, $"{before} → {Describe(plan)}", clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(plan.Id);
    }

    private static string Describe(Plan p) => string.Create(
        CultureInfo.InvariantCulture,
        $"{p.Name} {p.RegistrationFee.Amount}+{p.MonthlyPrice.Amount}/m prio {p.Priority} visits {p.IncludedVisitsPerPeriod?.ToString(CultureInfo.InvariantCulture) ?? "∞"} sla {string.Join('/', p.SlaPolicies.OrderBy(s => s.Urgency).Select(s => s.MaxDelay.TotalHours))} {(p.IsActive ? "on" : "off")}");
}

// ===================================================================== Geography

public sealed record CommuneRow(Guid Id, string Name, string Department, string Region, int Neighborhoods);

public sealed record NeighborhoodRow(Guid Id, string Name, int Members, int Threshold);

/// <summary>Region / Department / Commune / Quartier reference data: add or rename neighborhoods.</summary>
public sealed class GeographyManagement(IApplicationDbContext db, Settings settings, TimeProvider clock)
{
    public async Task<IReadOnlyList<CommuneRow>> CommunesAsync(string? query, CancellationToken cancellationToken = default)
    {
        var term = Slug.From(query ?? string.Empty);
        return await (
                from c in db.Communes
                join d in db.Departments on c.DepartmentId equals d.Id
                join r in db.Regions on d.RegionId equals r.Id
                where term == string.Empty || c.Slug.Contains(term) || d.Slug.Contains(term)
                orderby r.Name, d.Name, c.Name
                select new CommuneRow(c.Id, c.Name, d.Name, r.Name, db.Neighborhoods.Count(n => n.CommuneId == c.Id)))
            .AsNoTracking()
            .Take(300)
            .ToListAsync(cancellationToken);
    }

    public async Task<(CommuneRow Commune, IReadOnlyList<NeighborhoodRow> Neighborhoods)?> CommuneAsync(Guid communeId, CancellationToken cancellationToken = default)
    {
        var commune = (await CommunesAsync(null, cancellationToken)).FirstOrDefault(c => c.Id == communeId)
            ?? await (
                from c in db.Communes
                join d in db.Departments on c.DepartmentId equals d.Id
                join r in db.Regions on d.RegionId equals r.Id
                where c.Id == communeId
                select new CommuneRow(c.Id, c.Name, d.Name, r.Name, 0))
                .FirstOrDefaultAsync(cancellationToken);
        if (commune is null)
        {
            return null;
        }

        var neighborhoods = await (
                from n in db.Neighborhoods
                join p in db.NeighborhoodPrograms on n.Id equals p.NeighborhoodId into programs
                from p in programs.DefaultIfEmpty()
                where n.CommuneId == communeId
                orderby n.Name
                select new NeighborhoodRow(n.Id, n.Name, p == null ? 0 : p.MemberCount, p == null ? 0 : p.Threshold))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return (commune, neighborhoods);
    }

    public async Task<Result<Guid>> AddNeighborhoodAsync(Guid adminId, Guid communeId, string? name, CancellationToken cancellationToken = default)
    {
        if (!await db.Communes.AnyAsync(c => c.Id == communeId, cancellationToken))
        {
            return Result<Guid>.Failure("place.not_found");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Guid>.Failure("place.name");
        }

        var slug = Slug.From(name);
        if (await db.Neighborhoods.AnyAsync(n => n.CommuneId == communeId && n.Slug == slug, cancellationToken))
        {
            return Result<Guid>.Failure("place.exists");
        }

        Neighborhood neighborhood;
        try
        {
            neighborhood = new Neighborhood(Guid.CreateVersion7(), communeId, name, slug);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.Neighborhoods.Add(neighborhood);
        db.NeighborhoodPrograms.Add(new NeighborhoodProgram(neighborhood.Id, await settings.IntAsync(SettingKeys.NeighborhoodThreshold, cancellationToken)));
        db.AuditLogs.Add(new AuditLog(adminId, "geo.neighborhood_add", nameof(Neighborhood), neighborhood.Id.ToString(), false, null, name, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(neighborhood.Id);
    }

    public async Task<Result<Guid>> RenameNeighborhoodAsync(Guid adminId, Guid neighborhoodId, string? name, CancellationToken cancellationToken = default)
    {
        var neighborhood = await db.Neighborhoods.FirstOrDefaultAsync(n => n.Id == neighborhoodId, cancellationToken);
        if (neighborhood is null)
        {
            return Result<Guid>.Failure("place.not_found");
        }

        try
        {
            neighborhood.Rename(name ?? string.Empty);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.AuditLogs.Add(new AuditLog(adminId, "geo.neighborhood_rename", nameof(Neighborhood), neighborhood.Id.ToString(), false, null, name, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(neighborhood.Id);
    }
}

// ===================================================================== Settings

public sealed record SettingRow(string Key, string Value, bool IsNumber);

/// <summary>Platform settings (grace period, warranty, thresholds…). Numbers are checked; changes audited.</summary>
public sealed class SettingsManagement(IApplicationDbContext db, TimeProvider clock)
{
    private static readonly HashSet<string> Numbers =
        [SettingKeys.PaymentGraceDays, SettingKeys.AutoValidateAfterDays, SettingKeys.WarrantyDays, SettingKeys.NeighborhoodThreshold];

    /// <summary>Edited on their own screen (Wave link: payments page).</summary>
    private static readonly HashSet<string> Elsewhere = [SettingKeys.WaveLink];

    public async Task<IReadOnlyList<SettingRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return [.. SettingKeys.Defaults.Keys
            .Where(k => !Elsewhere.Contains(k))
            .Select(k => new SettingRow(k, stored.GetValueOrDefault(k, SettingKeys.Defaults[k]), Numbers.Contains(k)))];
    }

    public async Task<Result<string>> UpdateAsync(Guid adminId, string key, string? value, CancellationToken cancellationToken = default)
    {
        if (!SettingKeys.Defaults.ContainsKey(key) || Elsewhere.Contains(key))
        {
            return Result<string>.Failure("setting.unknown");
        }

        var text = value?.Trim() ?? string.Empty;
        if (Numbers.Contains(key) && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 0 or > 10_000))
        {
            return Result<string>.Failure("setting.number");
        }

        var now = clock.GetUtcNow();
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting(key, text, now));
        }
        else
        {
            setting.Change(text, now);
        }

        db.AuditLogs.Add(new AuditLog(adminId, "settings.update", nameof(AppSetting), key, false, null, text, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result<string>.Success(text);
    }
}
