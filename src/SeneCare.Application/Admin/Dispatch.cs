using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Technicians;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;

namespace SeneCare.Application.Admin;

/// <summary>Columns of the dispatch queue.</summary>
public enum DispatchStage
{
    /// <summary>New or being analysed: qualify, then propose slots (or dispatch now if critical).</summary>
    ToAnalyse = 1,

    /// <summary>Slots proposed, waiting for the customer's choice.</summary>
    WaitingCustomer = 2,

    /// <summary>Slot confirmed, no technician yet.</summary>
    ToAssign = 3,

    /// <summary>Technician assigned / on the way / on site.</summary>
    InProgress = 4,

    /// <summary>Customer disputed the work.</summary>
    Disputed = 5,
}

public sealed record DispatchItem(
    Guid Id,
    string Number,
    ServiceDomain Domain,
    Urgency Urgency,
    RequestStatus Status,
    DispatchStage Stage,
    string Title,
    string Site,
    string Commune,
    Guid CommuneId,
    string AccountName,
    string PlanName,
    int PlanPriority,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SlaDeadline,
    DateTimeOffset? AppointmentAt,
    string? TechnicianName,
    double? Latitude,
    double? Longitude)
{
    public bool IsLate(DateTimeOffset now) => SlaDeadline is { } deadline && deadline < now;
}

public sealed record TechnicianSuggestion(
    Guid TechnicianId,
    string Name,
    string PhoneE164,
    bool HasSkill,
    bool CoversZone,
    bool? IsAvailable,
    int OpenMissions,
    double? Rating,
    int Score);

public sealed record DispatchDetail(
    DispatchItem Item,
    string Description,
    string? Landmark,
    string? CustomerName,
    string CustomerPhoneE164,
    IReadOnlyList<Guid> PhotoIds,
    Guid? VoiceNoteId,
    IReadOnlyList<(RequestStatus? From, RequestStatus To, DateTimeOffset At, string? Comment)> History,
    IReadOnlyList<TimeSlot> ProposedSlots,
    IReadOnlyList<TimeSlot> PreferredSlots,
    TimeSlot? ConfirmedSlot,
    Guid? InterventionId,
    IReadOnlyList<TechnicianSuggestion> Suggestions,
    string? DisputeReason);

public sealed record CalendarEntry(Guid RequestId, string Number, ServiceDomain Domain, Urgency Urgency, string Site, DateTimeOffset Start, DateTimeOffset End, string? TechnicianName);

/// <summary>
/// Dispatch queue: every request that needs an action from SENECARE, most pressing first
/// (late SLA, critical, higher offer). Read side of the dispatcher screens (list, calendar, map).
/// </summary>
public sealed class DispatchQueries(IApplicationDbContext db, TimeProvider clock)
{
    private static readonly RequestStatus[] Handled =
    [
        RequestStatus.Submitted, RequestStatus.UnderReview, RequestStatus.AppointmentProposed, RequestStatus.AppointmentConfirmed,
        RequestStatus.OnTheWay, RequestStatus.OnSite, RequestStatus.InProgress, RequestStatus.Disputed,
    ];

    public async Task<IReadOnlyList<DispatchItem>> QueueAsync(CancellationToken cancellationToken = default)
    {
        var items = await ItemsAsync(r => Handled.Contains(r.Status), cancellationToken);
        var now = clock.GetUtcNow();
        return [.. items
            .OrderBy(i => i.Stage == DispatchStage.InProgress)
            .ThenByDescending(i => i.IsLate(now))
            .ThenBy(i => i.Urgency)
            .ThenByDescending(i => i.PlanPriority)
            .ThenBy(i => i.SlaDeadline ?? i.AppointmentAt ?? DateTimeOffset.MaxValue)];
    }

    /// <summary>Confirmed appointments of the week starting <paramref name="weekStart"/> (calendar view).</summary>
    public async Task<IReadOnlyList<CalendarEntry>> CalendarAsync(DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        var from = new DateTimeOffset(weekStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = from.AddDays(7);
        var items = await ItemsAsync(
            r => r.Status != RequestStatus.Cancelled && r.Status != RequestStatus.AwaitingApproval,
            cancellationToken,
            appointmentFrom: from,
            appointmentTo: to);
        var slots = await db.ServiceRequests.AsNoTracking()
            .Where(r => items.Select(i => i.Id).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.CurrentAppointment?.ChosenSlot, cancellationToken);
        return [.. items
            .Where(i => slots.GetValueOrDefault(i.Id) is not null)
            .Select(i => new CalendarEntry(i.Id, i.Number, i.Domain, i.Urgency, i.Site, slots[i.Id]!.Start, slots[i.Id]!.End, i.TechnicianName))
            .OrderBy(e => e.Start)];
    }

    public async Task<DispatchDetail?> DetailAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var item = (await ItemsAsync(r => r.Id == requestId, cancellationToken)).FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        var request = await db.ServiceRequests.AsNoTracking().FirstAsync(r => r.Id == requestId, cancellationToken);
        var property = await db.Properties.AsNoTracking().FirstAsync(p => p.Id == request.PropertyId, cancellationToken);
        var customer = await db.Users.AsNoTracking().FirstAsync(u => u.Id == request.ReportedByUserId, cancellationToken);
        var intervention = await db.Interventions.AsNoTracking()
            .Where(i => i.RequestId == requestId && i.CompletedAt == null)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var appointment = request.CurrentAppointment;
        var slot = appointment?.ChosenSlot ?? appointment?.ProposedSlots.OrderBy(s => s.Start).FirstOrDefault()
            ?? request.PreferredSlots.OrderBy(s => s.Start).FirstOrDefault();

        return new DispatchDetail(
            item,
            request.Description,
            property.Address.Landmark,
            customer.FullName,
            customer.PhoneNumber.E164,
            [.. request.Attachments.Where(a => a.Kind == MediaKind.Photo).Select(a => a.MediaFileId)],
            request.Attachments.Where(a => a.Kind == MediaKind.VoiceNote).Select(a => (Guid?)a.MediaFileId).FirstOrDefault(),
            [.. request.History.OrderBy(h => h.At).Select(h => (h.From, h.To, h.At, h.Comment))],
            appointment?.Status == AppointmentStatus.Proposed ? [.. appointment.ProposedSlots.OrderBy(s => s.Start)] : [],
            [.. request.PreferredSlots.OrderBy(s => s.Start)],
            appointment?.ChosenSlot,
            intervention,
            await SuggestAsync(item.Domain, item.CommuneId, slot, cancellationToken),
            request.DisputeReason);
    }

    /// <summary>Active technicians, for missions without a single trade (quarterly diagnostics).</summary>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> TechniciansAsync(CancellationToken cancellationToken = default) =>
        [.. (await (
                from t in db.Technicians
                join u in db.Users on t.UserId equals u.Id
                where t.IsActive
                orderby u.FullName
                select new { t.Id, Name = u.FullName ?? u.PhoneNumber.ToString() })
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .Select(t => (t.Id, t.Name))];

    /// <summary>
    /// Technicians ranked for a mission: the trade is required; then covering the commune, free on the
    /// slot, fewer open missions and a better rating come first. The dispatcher decides.
    /// </summary>
    public async Task<IReadOnlyList<TechnicianSuggestion>> SuggestAsync(
        ServiceDomain domain, Guid communeId, TimeSlot? slot, CancellationToken cancellationToken = default)
    {
        var technicians = await (
                from t in db.Technicians
                join u in db.Users on t.UserId equals u.Id
                where t.IsActive
                select new { Technician = t, u.FullName, u.PhoneNumber })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var ids = technicians.Select(t => t.Technician.Id).ToList();
        var load = await db.Interventions.AsNoTracking()
            .Where(i => i.CompletedAt == null && ids.Contains(i.LeadTechnicianId))
            .GroupBy(i => i.LeadTechnicianId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, cancellationToken);

        return [.. technicians
            .Select(t => Rank(t.Technician, t.FullName ?? t.PhoneNumber.Formatted, t.PhoneNumber.E164, domain, communeId, slot, load.GetValueOrDefault(t.Technician.Id)))
            .Where(s => s.HasSkill)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.OpenMissions)
            .Take(8)];
    }

    internal static TechnicianSuggestion Rank(Technician t, string name, string phone, ServiceDomain domain, Guid communeId, TimeSlot? slot, int openMissions)
    {
        var hasSkill = t.Skills.Contains(domain);
        var zone = t.CommuneIds.Contains(communeId);
        bool? available = slot is null || t.WeeklyAvailability.Count == 0 ? null : t.IsAvailable(slot);
        var score = (zone ? 50 : 0) + (available == true ? 30 : available == false ? -30 : 0) - (openMissions * 5)
            + (int)Math.Round((t.AverageRating ?? 4) * 2) + (t.IsCertified ? 5 : 0);
        return new TechnicianSuggestion(t.Id, name, phone, hasSkill, zone, available, openMissions, t.AverageRating, score);
    }

    private async Task<List<DispatchItem>> ItemsAsync(
        System.Linq.Expressions.Expression<Func<ServiceRequest, bool>> filter,
        CancellationToken cancellationToken,
        DateTimeOffset? appointmentFrom = null,
        DateTimeOffset? appointmentTo = null)
    {
        var rows = await (
                from r in db.ServiceRequests.Where(filter)
                join p in db.Properties on r.PropertyId equals p.Id
                join a in db.CustomerAccounts on r.AccountId equals a.Id
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                select new { Request = r, Site = p.Name, p.Address, Account = a.DisplayName, Commune = c.Name, CommuneId = c.Id })
            .AsNoTracking()
            .Take(500)
            .ToListAsync(cancellationToken);

        if (appointmentFrom is { } from && appointmentTo is { } to)
        {
            rows = [.. rows.Where(r => r.Request.CurrentAppointment?.ChosenSlot is { } s && s.Start >= from && s.Start < to)];
        }

        var propertyIds = rows.Select(r => r.Request.PropertyId).Distinct().ToList();
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.CoveredPropertyIds.Any(id => propertyIds.Contains(id)))
            .ToListAsync(cancellationToken);
        var plans = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var requestIds = rows.Select(r => r.Request.Id).ToList();
        var technicians = await (
                from i in db.Interventions
                join t in db.Technicians on i.LeadTechnicianId equals t.Id
                join u in db.Users on t.UserId equals u.Id
                where requestIds.Contains(i.RequestId)
                orderby i.CompletedAt
                select new { i.RequestId, Name = u.FullName ?? string.Empty })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r =>
        {
            var subscription = subscriptions.Where(s => s.Covers(r.Request.PropertyId)).MaxBy(s => s.CreatedAt);
            var plan = subscription is null ? null : plans.GetValueOrDefault(subscription.PlanId);
            return new DispatchItem(
                r.Request.Id, r.Request.Number, r.Request.Domain, r.Request.Urgency, r.Request.Status, StageOf(r.Request.Status, technicians.Any(t => t.RequestId == r.Request.Id)),
                Title(r.Request.Description), r.Site, r.Commune, r.CommuneId, r.Account, plan?.Name ?? string.Empty, plan?.Priority ?? 0,
                r.Request.CreatedAt, r.Request.SlaDeadline, r.Request.CurrentAppointment?.ChosenSlot?.Start,
                technicians.LastOrDefault(t => t.RequestId == r.Request.Id)?.Name,
                r.Address.Location?.Latitude, r.Address.Location?.Longitude);
        })];
    }

    private static DispatchStage StageOf(RequestStatus status, bool assigned) => status switch
    {
        RequestStatus.Submitted or RequestStatus.UnderReview => DispatchStage.ToAnalyse,
        RequestStatus.AppointmentProposed => DispatchStage.WaitingCustomer,
        RequestStatus.AppointmentConfirmed when !assigned => DispatchStage.ToAssign,
        RequestStatus.Disputed => DispatchStage.Disputed,
        _ => DispatchStage.InProgress,
    };

    private static string Title(string description)
    {
        var line = description.Split('\n', 2)[0].Trim();
        return line.Length <= 90 ? line : line[..87] + "…";
    }
}

/// <summary>Dispatcher actions on a request. Each one is written in the request history (who, when).</summary>
public sealed class DispatchActions(IApplicationDbContext db, AssignmentHandler assignments, TimeProvider clock)
{
    /// <summary>Default length of an intervention slot when the dispatcher picks a start time.</summary>
    public static readonly TimeSpan SlotLength = TimeSpan.FromHours(2);

    public Task<Result<Guid>> StartReviewAsync(Guid dispatcherId, Guid requestId, CancellationToken cancellationToken = default) =>
        ChangeAsync(requestId, (r, now) =>
        {
            if (r.Status == RequestStatus.Submitted)
            {
                r.StartReview(dispatcherId, now);
            }
        }, cancellationToken);

    /// <summary>Changes the urgency; the SLA restarts with the delay of the customer's offer.</summary>
    public async Task<Result<Guid>> RequalifyAsync(Guid dispatcherId, Guid requestId, Urgency urgency, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Result<Guid>.Failure("request.not_found");
        }

        var delay = await DelayAsync(request, urgency, cancellationToken);
        return await ChangeAsync(requestId, (r, now) =>
        {
            if (r.Status == RequestStatus.Submitted)
            {
                r.StartReview(dispatcherId, now);
            }

            r.Requalify(urgency, delay, dispatcherId, now);
        }, cancellationToken);
    }

    /// <summary>Up to three slots (start times; each lasts <see cref="SlotLength"/>) proposed to the customer.</summary>
    public Task<Result<Guid>> ProposeSlotsAsync(Guid dispatcherId, Guid requestId, IReadOnlyList<DateTimeOffset> starts, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var slots = starts.Where(s => s > now).Distinct().OrderBy(s => s).Take(3).Select(s => new TimeSlot(s, s + SlotLength)).ToList();
        if (slots.Count == 0)
        {
            return Task.FromResult(Result<Guid>.Failure("dispatch.slots_required"));
        }

        return ChangeAsync(requestId, (r, at) =>
        {
            if (r.Status == RequestStatus.Submitted)
            {
                r.StartReview(dispatcherId, at);
            }

            r.ProposeAppointment(slots, dispatcherId, at);
        }, cancellationToken);
    }

    /// <summary>Critical urgency: no customer choice, the technician leaves now.</summary>
    public async Task<Result<Guid>> DispatchNowAsync(Guid dispatcherId, Guid requestId, Guid technicianId, CancellationToken cancellationToken = default)
    {
        var changed = await ChangeAsync(requestId, (r, now) =>
        {
            if (r.Status == RequestStatus.Submitted)
            {
                r.StartReview(dispatcherId, now);
            }

            r.DispatchImmediately(new TimeSlot(now, now + SlotLength), dispatcherId, now);
        }, cancellationToken);
        return changed.IsSuccess ? await assignments.AssignInterventionAsync(requestId, technicianId, cancellationToken) : changed;
    }

    public Task<Result<Guid>> AssignAsync(Guid requestId, Guid technicianId, CancellationToken cancellationToken = default) =>
        assignments.AssignInterventionAsync(requestId, technicianId, cancellationToken);

    public Task<Result<Guid>> ReopenAsync(Guid dispatcherId, Guid requestId, CancellationToken cancellationToken = default) =>
        ChangeAsync(requestId, (r, now) => r.Reopen(dispatcherId, now), cancellationToken);

    public Task<Result<Guid>> CancelAsync(Guid dispatcherId, Guid requestId, string? reason, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(reason)
            ? Task.FromResult(Result<Guid>.Failure("dispatch.cancel_reason"))
            : ChangeAsync(requestId, (r, now) => r.Cancel(dispatcherId, reason, now), cancellationToken);

    private async Task<TimeSpan?> DelayAsync(ServiceRequest request, Urgency urgency, CancellationToken cancellationToken)
    {
        var planId = await db.Subscriptions
            .Where(s => s.CoveredPropertyIds.Contains(request.PropertyId))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (Guid?)s.PlanId)
            .FirstOrDefaultAsync(cancellationToken);
        var plan = planId is null ? null : await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        return plan?.GetMaxDelay(urgency);
    }

    private async Task<Result<Guid>> ChangeAsync(Guid requestId, Action<ServiceRequest, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Result<Guid>.Failure("request.not_found");
        }

        try
        {
            change(request, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }
}
