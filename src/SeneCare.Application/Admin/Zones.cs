using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Locations;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Admin;

public sealed record ZoneRegion(Guid Id, string Name, bool Served, int Communes, int ServedCommunes, int Waiting);

public sealed record ZoneDepartment(Guid Id, string Name, string Region);

public sealed record ZoneCommune(Guid Id, string Name, string Department, string Region, bool Served, bool RegionServed, int Neighborhoods);

public sealed record WaitlistEntry(Guid Id, DateTimeOffset At, string? Name, string Phone, string Region, string Place);

/// <summary>
/// Zones SENECARE covers (super-admin): open / close regions and communes, add communes the reference data lacks, and
/// read the « Prévenez-moi » requests of each zone. Every change is audited; reading the list (phone numbers) too.
/// </summary>
public sealed class ZoneManagement(IApplicationDbContext db, Settings settings, TimeProvider clock)
{
    public async Task<IReadOnlyList<ZoneRegion>> RegionsAsync(CancellationToken cancellationToken = default) =>
        await (
                from r in db.Regions
                orderby r.Name
                select new ZoneRegion(
                    r.Id,
                    r.Name,
                    r.IsServed,
                    (from c in db.Communes join d in db.Departments on c.DepartmentId equals d.Id where d.RegionId == r.Id select c.Id).Count(),
                    (from c in db.Communes join d in db.Departments on c.DepartmentId equals d.Id where d.RegionId == r.Id && c.IsServed select c.Id).Count(),
                    db.CoverageRequests.Count(w => w.RegionId == r.Id)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ZoneDepartment>> DepartmentsAsync(CancellationToken cancellationToken = default) =>
        await (
                from d in db.Departments
                join r in db.Regions on d.RegionId equals r.Id
                orderby r.Name, d.Name
                select new ZoneDepartment(d.Id, d.Name, r.Name))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <summary>Communes matching <paramref name="query"/> (commune, department or region), with their coverage.</summary>
    public async Task<IReadOnlyList<ZoneCommune>> CommunesAsync(string? query, CancellationToken cancellationToken = default)
    {
        var term = Slug.From(query ?? string.Empty);
        return await (
                from c in db.Communes
                join d in db.Departments on c.DepartmentId equals d.Id
                join r in db.Regions on d.RegionId equals r.Id
                where term == string.Empty || c.Slug.Contains(term) || d.Slug.Contains(term) || r.Slug.Contains(term)
                orderby r.Name, d.Name, c.Name
                select new ZoneCommune(c.Id, c.Name, d.Name, r.Name, c.IsServed, r.IsServed, db.Neighborhoods.Count(n => n.CommuneId == c.Id)))
            .AsNoTracking()
            .Take(600)
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<Guid>> SetRegionServedAsync(Guid adminId, Guid regionId, bool served, CancellationToken cancellationToken = default)
    {
        var region = await db.Regions.FirstOrDefaultAsync(r => r.Id == regionId, cancellationToken);
        if (region is null)
        {
            return Result<Guid>.Failure("place.not_found");
        }

        region.SetServed(served);
        Audit(adminId, served ? "geo.region_open" : "geo.region_close", nameof(Region), region.Id, region.Name, false);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(region.Id);
    }

    public async Task<Result<Guid>> SetCommuneServedAsync(Guid adminId, Guid communeId, bool served, CancellationToken cancellationToken = default)
    {
        var commune = await db.Communes.FirstOrDefaultAsync(c => c.Id == communeId, cancellationToken);
        if (commune is null)
        {
            return Result<Guid>.Failure("place.not_found");
        }

        commune.SetServed(served);
        Audit(adminId, served ? "geo.commune_open" : "geo.commune_close", nameof(Commune), commune.Id, commune.Name, false);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(commune.Id);
    }

    /// <summary>A commune missing from the reference data, with a first neighborhood of the same name (as seeded).</summary>
    public async Task<Result<Guid>> AddCommuneAsync(Guid adminId, Guid departmentId, string? name, CancellationToken cancellationToken = default)
    {
        if (!await db.Departments.AnyAsync(d => d.Id == departmentId, cancellationToken))
        {
            return Result<Guid>.Failure("place.not_found");
        }

        var slug = Slug.From(name ?? string.Empty);
        if (slug.Length < 2)
        {
            return Result<Guid>.Failure("place.name");
        }

        if (await db.Communes.AnyAsync(c => c.DepartmentId == departmentId && c.Slug == slug, cancellationToken))
        {
            return Result<Guid>.Failure("place.exists");
        }

        Commune commune;
        Neighborhood neighborhood;
        try
        {
            commune = new Commune(Guid.CreateVersion7(), departmentId, name!.Trim(), slug);
            neighborhood = new Neighborhood(Guid.CreateVersion7(), commune.Id, name.Trim(), slug);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.Communes.Add(commune);
        db.Neighborhoods.Add(neighborhood);
        db.NeighborhoodPrograms.Add(new NeighborhoodProgram(neighborhood.Id, await settings.IntAsync(SettingKeys.NeighborhoodThreshold, cancellationToken)));
        Audit(adminId, "geo.commune_add", nameof(Commune), commune.Id, commune.Name, false);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(commune.Id);
    }

    /// <summary>« Prévenez-moi » requests, newest first (phone numbers: the reading is logged as sensitive).</summary>
    public async Task<IReadOnlyList<WaitlistEntry>> WaitlistAsync(Guid adminId, Guid? regionId, CancellationToken cancellationToken = default)
    {
        var rows = await (
                from w in db.CoverageRequests
                join r in db.Regions on w.RegionId equals r.Id
                join c in db.Communes on w.CommuneId equals c.Id into communes
                from c in communes.DefaultIfEmpty()
                where regionId == null || w.RegionId == regionId
                orderby w.CreatedAt descending
                select new { w.Id, w.CreatedAt, w.FullName, w.PhoneNumber, Region = r.Name, Commune = c == null ? null : c.Name, w.Locality })
            .AsNoTracking()
            .Take(500)
            .ToListAsync(cancellationToken);
        Audit(adminId, "coverage.waitlist_read", nameof(CoverageRequest), regionId, $"{rows.Count} rows", true);
        await db.SaveChangesAsync(cancellationToken);
        return [.. rows.Select(r => new WaitlistEntry(
            r.Id, r.CreatedAt, r.FullName, r.PhoneNumber.Formatted, r.Region,
            string.Join(" · ", new[] { r.Commune, r.Locality }.Where(s => !string.IsNullOrWhiteSpace(s)))))];
    }

    /// <summary>Handled (person told, or no longer wanted): the request and its number are erased.</summary>
    public async Task<Result<Guid>> DeleteWaitlistAsync(Guid adminId, Guid id, CancellationToken cancellationToken = default)
    {
        var request = await db.CoverageRequests.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (request is null)
        {
            return Result<Guid>.Failure("place.not_found");
        }

        db.CoverageRequests.Remove(request);
        Audit(adminId, "coverage.waitlist_delete", nameof(CoverageRequest), id, null, true);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(id);
    }

    private void Audit(Guid adminId, string action, string entity, Guid? id, string? details, bool sensitive) =>
        db.AuditLogs.Add(new AuditLog(adminId, action, entity, id?.ToString(), sensitive, null, details, clock.GetUtcNow()));
}
