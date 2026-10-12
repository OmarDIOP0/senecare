using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Common;

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
