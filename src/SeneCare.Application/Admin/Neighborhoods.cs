using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Media;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Admin;

/// <summary>
/// « Adhérents » of a neighborhood: customer accounts with a running subscription (active or in its
/// grace period) covering at least one building there. Recounted when a subscription starts, or on demand.
/// </summary>
public sealed class NeighborhoodMembership(IApplicationDbContext db, TimeProvider clock)
{
    /// <summary>Recounts the given neighborhoods (all of them when null). The caller saves.</summary>
    public async Task<int> RefreshAsync(IReadOnlyCollection<Guid>? neighborhoodIds, CancellationToken cancellationToken = default)
    {
        var programs = await db.NeighborhoodPrograms
            .Where(p => neighborhoodIds == null || neighborhoodIds.Contains(p.NeighborhoodId))
            .ToListAsync(cancellationToken);
        var ids = programs.Select(p => p.NeighborhoodId).ToList();

        // Saved state, overridden by subscriptions changed in this unit of work (just activated…).
        var local = db.Subscriptions.Local.ToList();
        var localIds = local.Select(s => s.Id).ToList();
        var stored = await db.Subscriptions.AsNoTracking()
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue) && !localIds.Contains(s.Id))
            .Select(s => new { s.AccountId, Properties = s.CoveredPropertyIds })
            .ToListAsync(cancellationToken);
        var running = stored
            .Concat(local.Where(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
                .Select(s => new { s.AccountId, Properties = (IReadOnlyCollection<Guid>)[.. s.CoveredPropertyIds] }))
            .ToList();
        var coveredIds = running.SelectMany(s => s.Properties).Distinct().ToList();
        var places = await db.Properties.AsNoTracking()
            .Where(p => coveredIds.Contains(p.Id) && ids.Contains(p.Address.NeighborhoodId))
            .Select(p => new { p.Id, p.Address.NeighborhoodId })
            .ToDictionaryAsync(p => p.Id, p => p.NeighborhoodId, cancellationToken);

        var counts = running
            .SelectMany(s => s.Properties.Where(places.ContainsKey).Select(p => (s.AccountId, Neighborhood: places[p])))
            .Distinct()
            .GroupBy(x => x.Neighborhood)
            .ToDictionary(g => g.Key, g => g.Count());

        var now = clock.GetUtcNow();
        foreach (var program in programs)
        {
            program.UpdateMemberCount(counts.GetValueOrDefault(program.NeighborhoodId), now);
        }

        return programs.Count;
    }

    /// <summary>Recount after a subscription change on these buildings. The caller saves.</summary>
    public async Task RefreshForPropertiesAsync(IReadOnlyCollection<Guid> propertyIds, CancellationToken cancellationToken = default)
    {
        var neighborhoods = await db.Properties.Where(p => propertyIds.Contains(p.Id)).Select(p => p.Address.NeighborhoodId).Distinct().ToListAsync(cancellationToken);
        await RefreshAsync(neighborhoods, cancellationToken);
    }
}

public sealed record ProgramRow(Guid NeighborhoodId, string Neighborhood, string Commune, int Members, int Threshold, NeighborhoodProgramStatus Status, int Trees, int Operations);

public sealed record OperationRow(Guid Id, DateOnly Date, int Trees, string Description, bool IsPublished, IReadOnlyList<Guid> PhotoIds);

/// <summary>Programme Quartiers back-office: thresholds, greening operations, photos, publication.</summary>
public sealed class NeighborhoodPrograms(IApplicationDbContext db, NeighborhoodMembership membership, MediaUploader uploader, TimeProvider clock)
{
    public async Task<IReadOnlyList<ProgramRow>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        var rows = await (
                from p in db.NeighborhoodPrograms
                join n in db.Neighborhoods on p.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where !activeOnly || p.Status == NeighborhoodProgramStatus.Active || p.MemberCount > 0
                select new { Program = p, Neighborhood = n.Name, Commune = c.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return [.. rows
            .Select(r => new ProgramRow(r.Program.NeighborhoodId, r.Neighborhood, r.Commune, r.Program.MemberCount, r.Program.Threshold, r.Program.Status, r.Program.TotalTreesPlanted, r.Program.Operations.Count))
            .OrderByDescending(r => r.Status)
            .ThenByDescending(r => (double)r.Members / Math.Max(1, r.Threshold))
            .ThenBy(r => r.Neighborhood, StringComparer.CurrentCulture)
            .Take(200)];
    }

    public async Task<(ProgramRow Program, IReadOnlyList<OperationRow> Operations)?> DetailAsync(Guid neighborhoodId, CancellationToken cancellationToken = default)
    {
        var row = await (
                from p in db.NeighborhoodPrograms
                join n in db.Neighborhoods on p.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where p.NeighborhoodId == neighborhoodId
                select new { Program = p, Neighborhood = n.Name, Commune = c.Name })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var program = row.Program;
        return (
            new ProgramRow(program.NeighborhoodId, row.Neighborhood, row.Commune, program.MemberCount, program.Threshold, program.Status, program.TotalTreesPlanted, program.Operations.Count),
            [.. program.Operations.OrderByDescending(o => o.Date).Select(o => new OperationRow(o.Id, o.Date, o.TreesPlanted, o.Description, o.IsPublished, [.. o.Photos.Select(ph => ph.MediaFileId)]))]);
    }

    public async Task<Result<int>> RecountAllAsync(CancellationToken cancellationToken = default)
    {
        var count = await membership.RefreshAsync(null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(count);
    }

    public Task<Result<Guid>> ChangeThresholdAsync(Guid adminId, Guid neighborhoodId, int threshold, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, neighborhoodId, "neighborhood.threshold", p => p.ChangeThreshold(threshold, clock.GetUtcNow()), cancellationToken);

    public async Task<Result<Guid>> RecordOperationAsync(
        Guid adminId, Guid neighborhoodId, DateOnly date, int trees, string? description, IReadOnlyList<UploadedFile> photos, CancellationToken cancellationToken = default)
    {
        var program = await db.NeighborhoodPrograms.FirstOrDefaultAsync(p => p.NeighborhoodId == neighborhoodId, cancellationToken);
        if (program is null)
        {
            return Result<Guid>.Failure("place.not_found");
        }

        if (photos.Count > 8)
        {
            return Result<Guid>.Failure("mission.photos_count");
        }

        try
        {
            var operation = program.RecordOperation(date, trees, description ?? string.Empty);
            foreach (var photo in photos)
            {
                var stored = await uploader.StoreAsync(photo, MediaKind.Photo, adminId, null, cancellationToken);
                if (!stored.IsSuccess)
                {
                    return Result<Guid>.Failure(stored.Error);
                }

                program.AddOperationPhoto(operation.Id, stored.Value.Id);
            }
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.AuditLogs.Add(new AuditLog(adminId, "neighborhood.operation", nameof(NeighborhoodProgram), neighborhoodId.ToString(), false, null, $"{date:yyyy-MM-dd} · {trees}", clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(program.Id);
    }

    /// <summary>Publication makes the photos and trees visible to the neighborhood's members.</summary>
    public Task<Result<Guid>> PublishAsync(Guid adminId, Guid neighborhoodId, Guid operationId, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, neighborhoodId, "neighborhood.publish", p => p.PublishOperation(operationId), cancellationToken);

    private async Task<Result<Guid>> ChangeAsync(Guid adminId, Guid neighborhoodId, string action, Action<NeighborhoodProgram> change, CancellationToken cancellationToken)
    {
        var program = await db.NeighborhoodPrograms.FirstOrDefaultAsync(p => p.NeighborhoodId == neighborhoodId, cancellationToken);
        if (program is null)
        {
            return Result<Guid>.Failure("place.not_found");
        }

        try
        {
            change(program);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.AuditLogs.Add(new AuditLog(adminId, action, nameof(NeighborhoodProgram), neighborhoodId.ToString(), false, null, null, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(program.Id);
    }
}
