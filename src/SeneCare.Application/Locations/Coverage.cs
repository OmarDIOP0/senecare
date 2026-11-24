using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Locations;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Locations;

/// <summary>
/// Zones SENECARE covers (user decision of 24/11/2026): set by admins per region and per commune. A new home can only
/// be placed in an open zone; elsewhere the person leaves a number (« Prévenez-moi »).
/// </summary>
public sealed class Coverage(IApplicationDbContext db)
{
    /// <summary>The neighborhood exists and lies in an open commune of an open region.</summary>
    public Task<bool> IsServedAsync(Guid neighborhoodId, CancellationToken cancellationToken = default) =>
        (from n in db.Neighborhoods
         join c in db.Communes on n.CommuneId equals c.Id
         join d in db.Departments on c.DepartmentId equals d.Id
         join r in db.Regions on d.RegionId equals r.Id
         where n.Id == neighborhoodId && c.IsServed && r.IsServed
         select n.Id).AnyAsync(cancellationToken);

    public Task<bool> IsCommuneServedAsync(Guid communeId, CancellationToken cancellationToken = default) =>
        (from c in db.Communes
         join d in db.Departments on c.DepartmentId equals d.Id
         join r in db.Regions on d.RegionId equals r.Id
         where c.Id == communeId && c.IsServed && r.IsServed
         select c.Id).AnyAsync(cancellationToken);
}

public sealed record WaitlistRegion(Guid Id, string Name, bool Served);

public sealed record WaitlistCommune(Guid Id, string Name, Guid RegionId, string Department);

public sealed record JoinWaitlistCommand(string? Phone, string? FullName, Guid RegionId, Guid? CommuneId, string? Locality);

/// <summary>« Prévenez-moi quand SENECARE arrive chez moi »: a number per zone, a few per day at most.</summary>
public sealed class WaitlistHandler(IApplicationDbContext db, TimeProvider clock)
{
    public const int MaxPerPhonePerDay = 3;

    public async Task<IReadOnlyList<WaitlistRegion>> RegionsAsync(CancellationToken cancellationToken = default) =>
        await db.Regions.AsNoTracking().OrderBy(r => r.Name).Select(r => new WaitlistRegion(r.Id, r.Name, r.IsServed)).ToListAsync(cancellationToken);

    public async Task<WaitlistCommune?> CommuneAsync(Guid communeId, CancellationToken cancellationToken = default) =>
        await (from c in db.Communes
               join d in db.Departments on c.DepartmentId equals d.Id
               where c.Id == communeId
               select new WaitlistCommune(c.Id, c.Name, d.RegionId, d.Name))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<Guid>> JoinAsync(JoinWaitlistCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!PhoneNumber.TryParse(command.Phone, out var phone))
        {
            return Result<Guid>.Failure("phone.invalid");
        }

        if (!await db.Regions.AnyAsync(r => r.Id == command.RegionId, cancellationToken))
        {
            return Result<Guid>.Failure("coverage.region");
        }

        Guid? communeId = command.CommuneId is { } id
            && await (from c in db.Communes
                      join d in db.Departments on c.DepartmentId equals d.Id
                      where c.Id == id && d.RegionId == command.RegionId
                      select c.Id).AnyAsync(cancellationToken)
            ? id
            : null;
        if (communeId is null && string.IsNullOrWhiteSpace(command.Locality))
        {
            return Result<Guid>.Failure("coverage.locality");
        }

        var now = clock.GetUtcNow();
        var dayAgo = now.AddDays(-1);
        if (await db.CoverageRequests.CountAsync(c => c.PhoneNumber == phone && c.CreatedAt > dayAgo, cancellationToken) >= MaxPerPhonePerDay)
        {
            return Result<Guid>.Failure("coverage.too_many");
        }

        CoverageRequest request;
        try
        {
            request = new CoverageRequest(command.RegionId, communeId, command.Locality, command.FullName, phone, now);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.CoverageRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }
}
