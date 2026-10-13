using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;

namespace SeneCare.Application.Technicians;

public enum MissionKind
{
    Intervention = 1,
    Diagnostic = 2,
}

/// <summary>One line of « Missions du jour ». Coordinates let the phone sort by distance, even offline.</summary>
public sealed record MissionCard(
    Guid Id,
    MissionKind Kind,
    string Number,
    ServiceDomain? Domain,
    string Title,
    Urgency Urgency,
    RequestStatus? Status,
    DiagnosticStatus? DiagnosticStatus,
    DateTimeOffset? At,
    string Site,
    string Place,
    string? Landmark,
    double? Latitude,
    double? Longitude,
    bool IsDone);

public sealed record ChecklistLine(Guid Id, string Label, bool IsDone);

public sealed record PartLine(string Name, decimal Quantity, string Unit);

public sealed record MissionDetail(
    Guid InterventionId,
    Guid RequestId,
    string Number,
    ServiceDomain Domain,
    string Description,
    Urgency Urgency,
    RequestStatus Status,
    DateTimeOffset? AppointmentAt,
    string Site,
    string Place,
    string? Landmark,
    string? AddressDetails,
    double? Latitude,
    double? Longitude,
    string? CustomerName,
    string CustomerPhoneE164,
    IReadOnlyList<Guid> RequestPhotoIds,
    IReadOnlyList<ChecklistLine> Checklist,
    IReadOnlyList<Guid> BeforePhotoIds,
    IReadOnlyList<Guid> AfterPhotoIds,
    IReadOnlyList<PartLine> Parts,
    string? SignedByName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Report)
{
    public bool IsCompleted => CompletedAt is not null;

    public bool IsWorking => Status is RequestStatus.InProgress;
}

/// <summary>What a technician sees. Every query starts from the technician: no access to other missions.</summary>
public sealed class MissionQueries(IApplicationDbContext db, TechnicianAccess technicians, TimeProvider clock)
{
    private static readonly RequestStatus[] Active =
    [
        RequestStatus.AppointmentConfirmed, RequestStatus.OnTheWay, RequestStatus.OnSite, RequestStatus.InProgress,
    ];

    /// <summary>Open missions (whatever their date: a late one must not disappear) and those closed today.</summary>
    public async Task<IReadOnlyList<MissionCard>> TodayAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var technician = await technicians.OfAsync(userId, cancellationToken);
        if (technician is null)
        {
            return [];
        }

        var today = DakarCalendar.Today(clock);
        var startOfDay = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var interventions = await (
                from i in db.Interventions
                join r in db.ServiceRequests on i.RequestId equals r.Id
                where (i.LeadTechnicianId == technician.Id || i.TeamTechnicianIds.Contains(technician.Id))
                    && ((i.CompletedAt == null && Active.Contains(r.Status)) || i.CompletedAt >= startOfDay)
                select new { Intervention = i, Request = r })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var diagnostics = await db.Diagnostics.AsNoTracking()
            .Where(d => d.TechnicianId == technician.Id
                && (d.Status == DiagnosticStatus.Planned || d.Status == DiagnosticStatus.InProgress
                    || (d.Status == DiagnosticStatus.Submitted && d.SubmittedAt >= startOfDay)))
            .ToListAsync(cancellationToken);

        var propertyIds = interventions.Select(x => x.Request.PropertyId).ToList();
        var accountIds = diagnostics.Select(d => d.AccountId).ToList();
        var places = await PlacesAsync(propertyIds, accountIds, cancellationToken);
        var accounts = await db.CustomerAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.DisplayName, cancellationToken);

        var cards = interventions.Select(x =>
        {
            var place = places.ByProperty[x.Request.PropertyId];
            return new MissionCard(
                x.Intervention.Id, MissionKind.Intervention, x.Request.Number, x.Request.Domain, Title(x.Request.Description), x.Request.Urgency,
                x.Request.Status, null, x.Request.CurrentAppointment?.ChosenSlot?.Start, place.Name, place.Area, place.Landmark,
                place.Latitude, place.Longitude, x.Intervention.CompletedAt is not null);
        }).ToList();

        cards.AddRange(diagnostics.Select(d =>
        {
            var place = places.FirstSiteOfAccount.GetValueOrDefault(d.AccountId);
            return new MissionCard(
                d.Id, MissionKind.Diagnostic, accounts.GetValueOrDefault(d.AccountId, string.Empty), null,
                accounts.GetValueOrDefault(d.AccountId, string.Empty), Urgency.Planned, null, d.Status, d.ScheduledFor,
                place?.Name ?? string.Empty, place?.Area ?? string.Empty, place?.Landmark, place?.Latitude, place?.Longitude,
                d.Status == DiagnosticStatus.Submitted);
        }));

        // Not done first, then the most urgent, then the earliest appointment. The phone refines by distance.
        return [.. cards.OrderBy(c => c.IsDone).ThenBy(c => c.Urgency).ThenBy(c => c.At ?? DateTimeOffset.MaxValue)];
    }

    public async Task<MissionDetail?> DetailAsync(Guid userId, Guid interventionId, CancellationToken cancellationToken = default)
    {
        var technician = await technicians.OfAsync(userId, cancellationToken);
        var intervention = await db.Interventions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == interventionId, cancellationToken);
        if (technician is null || intervention is null || !intervention.Involves(technician.Id))
        {
            return null;
        }

        var request = await db.ServiceRequests.AsNoTracking().FirstAsync(r => r.Id == intervention.RequestId, cancellationToken);
        var place = (await PlacesAsync([request.PropertyId], [], cancellationToken)).ByProperty[request.PropertyId];
        var customer = await db.Users.AsNoTracking().FirstAsync(u => u.Id == request.ReportedByUserId, cancellationToken);

        return new MissionDetail(
            intervention.Id,
            request.Id,
            request.Number,
            request.Domain,
            request.Description,
            request.Urgency,
            request.Status,
            request.CurrentAppointment?.ChosenSlot?.Start,
            place.Name,
            place.Area,
            place.Landmark,
            place.Details,
            place.Latitude,
            place.Longitude,
            customer.FullName,
            customer.PhoneNumber.E164,
            [.. request.Attachments.Where(a => a.Kind == MediaKind.Photo).Select(a => a.MediaFileId)],
            [.. intervention.Checklist.Select(c => new ChecklistLine(c.Id, c.Label, c.IsDone))],
            [.. intervention.Photos.Where(p => p.Kind == MediaKind.PhotoBefore).Select(p => p.MediaFileId)],
            [.. intervention.Photos.Where(p => p.Kind == MediaKind.PhotoAfter).Select(p => p.MediaFileId)],
            [.. intervention.PartsUsed.Select(p => new PartLine(p.Name, p.Quantity, p.Unit))],
            intervention.SignedByName,
            intervention.StartedAt,
            intervention.CompletedAt,
            intervention.Report);
    }

    /// <summary>« Rapports »: the technician's closed interventions of the last 30 days.</summary>
    public async Task<IReadOnlyList<MissionCard>> HistoryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var technician = await technicians.OfAsync(userId, cancellationToken);
        if (technician is null)
        {
            return [];
        }

        var since = clock.GetUtcNow().AddDays(-30);
        var done = await (
                from i in db.Interventions
                join r in db.ServiceRequests on i.RequestId equals r.Id
                where (i.LeadTechnicianId == technician.Id || i.TeamTechnicianIds.Contains(technician.Id)) && i.CompletedAt >= since
                orderby i.CompletedAt descending
                select new { Intervention = i, Request = r })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var places = await PlacesAsync([.. done.Select(x => x.Request.PropertyId)], [], cancellationToken);
        return [.. done.Select(x =>
        {
            var place = places.ByProperty[x.Request.PropertyId];
            return new MissionCard(
                x.Intervention.Id, MissionKind.Intervention, x.Request.Number, x.Request.Domain, Title(x.Request.Description), x.Request.Urgency,
                x.Request.Status, null, x.Intervention.CompletedAt, place.Name, place.Area, place.Landmark, place.Latitude, place.Longitude, true);
        })];
    }

    private sealed record Place(string Name, string Area, string? Landmark, string? Details, double? Latitude, double? Longitude);

    private sealed record Places(IReadOnlyDictionary<Guid, Place> ByProperty, IReadOnlyDictionary<Guid, Place> FirstSiteOfAccount);

    /// <summary>Building name, « Quartier, Commune », landmark and GPS point.</summary>
    private async Task<Places> PlacesAsync(IReadOnlyCollection<Guid> propertyIds, IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        var rows = await (
                from p in db.Properties
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where propertyIds.Contains(p.Id) || accountIds.Contains(p.AccountId)
                orderby p.CreatedAt
                select new { p.Id, p.AccountId, p.Name, Neighborhood = n.Name, Commune = c.Name, p.Address })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var byProperty = rows.ToDictionary(
            r => r.Id,
            r => new Place(r.Name, $"{r.Neighborhood}, {r.Commune}", r.Address.Landmark, r.Address.Details, r.Address.Location?.Latitude, r.Address.Location?.Longitude));
        var firstSite = rows.GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g => byProperty[g.First().Id]);
        return new Places(byProperty, firstSite);
    }

    private static string Title(string description)
    {
        var line = description.Split('\n', 2)[0].Trim();
        return line.Length <= 80 ? line : line[..77] + "…";
    }
}

/// <summary>The technician profile of a signed-in user (null for anyone else, or a deactivated technician).</summary>
public sealed class TechnicianAccess(IApplicationDbContext db)
{
    public Task<Technician?> OfAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Technicians.FirstOrDefaultAsync(t => t.UserId == userId && t.IsActive, cancellationToken);
}
