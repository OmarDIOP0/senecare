using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Properties;

public sealed record PropertySummary(
    Guid Id,
    string Name,
    PropertyKind Kind,
    string Neighborhood,
    string Commune,
    string? Landmark,
    Guid? PhotoId,
    SubscriptionStatus? SubscriptionStatus,
    int OpenRequests);

public sealed record PropertyLocation(double? Latitude, double? Longitude, string? Details, IReadOnlyList<Guid> PhotoIds);

public sealed record HealthBookEntry(
    Guid RequestId,
    string Number,
    ServiceDomain Domain,
    string Summary,
    DateTimeOffset CompletedAt,
    DateOnly? WarrantyEndsOn,
    int? Rating);

public sealed record PreventiveCheck(ServiceDomain Domain, string TitleKey, DateOnly? DueOn);

/// <summary>« Carnet de santé » of a building: everything done, warranties running, next checks.</summary>
public sealed record HealthBook(
    PropertySummary Property,
    PropertyLocation Location,
    IReadOnlyList<HealthBookEntry> History,
    IReadOnlyList<HealthBookEntry> ActiveWarranties,
    IReadOnlyList<PreventiveCheck> NextChecks);

public sealed class PropertyQueries(IApplicationDbContext db, AccessControl access, TimeProvider clock)
{
    public async Task<IReadOnlyList<PropertySummary>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var ids = await access.PropertyIdsOfAsync(userId, cancellationToken);
        return await SummariesAsync(ids, cancellationToken);
    }

    public async Task<HealthBook?> HealthBookAsync(Guid userId, Guid propertyId, CancellationToken cancellationToken = default)
    {
        var property = await access.PropertyAsync(userId, propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var summary = (await SummariesAsync([property.Id], cancellationToken))[0];
        var done = await (
                from r in db.ServiceRequests
                join i in db.Interventions on r.Id equals i.RequestId
                where r.PropertyId == property.Id && i.CompletedAt != null
                orderby i.CompletedAt descending
                select new { r.Id, r.Number, r.Domain, r.Description, i.CompletedAt, i.WarrantyEndsOn, Score = r.Rating == null ? (int?)null : r.Rating.Score })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var history = done
            .Select(d => new HealthBookEntry(d.Id, d.Number, d.Domain, Shorten(d.Description), d.CompletedAt!.Value, d.WarrantyEndsOn, d.Score))
            .ToList();
        var today = DakarCalendar.Today(clock);
        var warranties = history.Where(h => h.WarrantyEndsOn >= today).ToList();

        return new HealthBook(
            summary,
            new PropertyLocation(property.Address.Location?.Latitude, property.Address.Location?.Longitude, property.Address.Details, [.. property.Photos.Select(p => p.MediaFileId)]),
            history,
            warranties,
            PreventiveSchedule.NextChecks(history, today));
    }

    private async Task<IReadOnlyList<PropertySummary>> SummariesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var properties = await (
                from p in db.Properties
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where ids.Contains(p.Id) && p.IsActive
                orderby p.CreatedAt
                select new { Property = p, Neighborhood = n.Name, Commune = c.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Status != SubscriptionStatus.Cancelled)
            .Where(s => s.CoveredPropertyIds.Any(id => ids.Contains(id)))
            .ToListAsync(cancellationToken);

        var finalStatuses = new[] { RequestStatus.ValidatedByCustomer, RequestStatus.Cancelled };
        var open = await db.ServiceRequests.AsNoTracking()
            .Where(r => ids.Contains(r.PropertyId) && !finalStatuses.Contains(r.Status))
            .GroupBy(r => r.PropertyId)
            .Select(g => new { PropertyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PropertyId, g => g.Count, cancellationToken);

        return [.. properties.Select(x => new PropertySummary(
            x.Property.Id,
            x.Property.Name,
            x.Property.Kind,
            x.Neighborhood,
            x.Commune,
            x.Property.Address.Landmark,
            x.Property.Photos.Select(p => (Guid?)p.MediaFileId).FirstOrDefault(),
            subscriptions.FirstOrDefault(s => s.Covers(x.Property.Id))?.Status,
            open.GetValueOrDefault(x.Property.Id)))];
    }

    private static string Shorten(string text) => text.Length <= 90 ? text : string.Concat(text.AsSpan(0, 87), "…");
}

/// <summary>
/// Preventive maintenance suggestions. Waterproofing before the rainy season (checked in June,
/// rains from July), electrical installation every 12 months, plumbing every 12 months.
/// </summary>
public static class PreventiveSchedule
{
    public static IReadOnlyList<PreventiveCheck> NextChecks(IReadOnlyList<HealthBookEntry> history, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(history);
        var june = new DateOnly(today.Year, 6, 1);
        var checkedThisYear = history.Any(h =>
            h.Domain == ServiceDomain.Waterproofing && DateOnly.FromDateTime(h.CompletedAt.UtcDateTime) > today.AddMonths(-12));
        var inSeason = today.Month is >= 6 and <= 10;
        var nextWaterproofing = inSeason && !checkedThisYear
            ? today // rainy season and never checked: as soon as possible
            : today < june ? june : june.AddYears(1);

        return
        [
            new(ServiceDomain.Waterproofing, "Check_Waterproofing", nextWaterproofing),
            Yearly(history, ServiceDomain.Electricity, "Check_Electricity"),
            Yearly(history, ServiceDomain.Plumbing, "Check_Plumbing"),
        ];
    }

    private static PreventiveCheck Yearly(IReadOnlyList<HealthBookEntry> history, ServiceDomain domain, string titleKey)
    {
        var last = history.Where(h => h.Domain == domain).Select(h => (DateTimeOffset?)h.CompletedAt).FirstOrDefault();
        return new PreventiveCheck(domain, titleKey, last is { } date ? DateOnly.FromDateTime(date.UtcDateTime).AddYears(1) : null);
    }
}
