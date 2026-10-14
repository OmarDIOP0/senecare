using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Locations;

namespace SeneCare.Application.Locations;

/// <summary>« Almadies » · Ngor, Dakar</summary>
public sealed record NeighborhoodOption(Guid Id, string Name, string Commune, string Department, string Region)
{
    public string Context => Commune == Name ? $"{Department}, {Region}" : $"{Commune}, {Department}";
}

/// <summary>
/// Neighborhood picker search. Matches on slugs, so « sacre coeur », « Sacré-Cœur » and « SACRE-COEUR »
/// all find « Sacré-Cœur »; names starting with the text come first.
/// </summary>
public sealed class SearchNeighborhoodsHandler(IApplicationDbContext db)
{
    public const int MaxResults = 8;

    public async Task<IReadOnlyList<NeighborhoodOption>> HandleAsync(string? text, CancellationToken cancellationToken = default)
    {
        var query = Slug.From(text ?? string.Empty);
        if (query.Length < 2)
        {
            return [];
        }

        var rows = await (
                from n in db.Neighborhoods
                join c in db.Communes on n.CommuneId equals c.Id
                join d in db.Departments on c.DepartmentId equals d.Id
                join r in db.Regions on d.RegionId equals r.Id
                where n.Slug.Contains(query) || c.Slug.Contains(query)
                orderby n.Slug.StartsWith(query) descending, n.Name
                select new NeighborhoodOption(n.Id, n.Name, c.Name, d.Name, r.Name))
            .AsNoTracking()
            .Take(MaxResults)
            .ToListAsync(cancellationToken);
        return rows;
    }
}

/// <summary>« Ngor » · Dakar: a commune to attach a new neighborhood to.</summary>
public sealed record CommuneOption(Guid Id, string Name, string Department);

/// <summary>All communes, by department then name (113 in the reference data): the « add my neighborhood » list.</summary>
public sealed class ListCommunesHandler(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<CommuneOption>> HandleAsync(CancellationToken cancellationToken = default) =>
        await (
                from c in db.Communes
                join d in db.Departments on c.DepartmentId equals d.Id
                orderby d.Name, c.Name
                select new CommuneOption(c.Id, c.Name, d.Name))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
}

public sealed record SuggestNeighborhoodCommand(Guid UserId, Guid CommuneId, string? Name);

/// <summary>
/// « Ajouter mon quartier » when the search finds nothing. Same name in the same commune: the existing one is
/// returned. A few per person and per day, so the reference data cannot be flooded.
/// </summary>
public sealed class SuggestNeighborhoodHandler(IApplicationDbContext db, TimeProvider clock)
{
    public const int MaxPerUserPerDay = 5;

    public async Task<Result<NeighborhoodOption>> HandleAsync(SuggestNeighborhoodCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var commune = await (
                from c in db.Communes
                join d in db.Departments on c.DepartmentId equals d.Id
                join r in db.Regions on d.RegionId equals r.Id
                where c.Id == command.CommuneId
                select new { c.Name, Department = d.Name, Region = r.Name })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (commune is null)
        {
            return Result<NeighborhoodOption>.Failure("place.commune");
        }

        var slug = Slug.From(command.Name ?? string.Empty);
        var existing = await db.Neighborhoods.AsNoTracking()
            .FirstOrDefaultAsync(n => n.CommuneId == command.CommuneId && n.Slug == slug, cancellationToken);
        if (existing is not null)
        {
            return Result<NeighborhoodOption>.Success(new NeighborhoodOption(existing.Id, existing.Name, commune.Name, commune.Department, commune.Region));
        }

        var now = clock.GetUtcNow();
        var dayAgo = now.AddDays(-1);
        if (await db.Neighborhoods.CountAsync(n => n.SuggestedByUserId == command.UserId && n.SuggestedAt > dayAgo, cancellationToken) >= MaxPerUserPerDay)
        {
            return Result<NeighborhoodOption>.Failure("place.too_many");
        }

        Neighborhood neighborhood;
        try
        {
            neighborhood = Neighborhood.Suggest(command.CommuneId, command.Name ?? string.Empty, command.UserId, now);
        }
        catch (DomainException ex)
        {
            return Result<NeighborhoodOption>.Failure(ex.Code);
        }

        db.Neighborhoods.Add(neighborhood);
        await db.SaveChangesAsync(cancellationToken);
        return Result<NeighborhoodOption>.Success(new NeighborhoodOption(neighborhood.Id, neighborhood.Name, commune.Name, commune.Department, commune.Region));
    }
}
