using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Requests;

public sealed record TechnicianContact(string Name, string PhoneE164);

public sealed record RequestSummary(
    Guid Id,
    string Number,
    ServiceDomain Domain,
    string Title,
    RequestStatus Status,
    Urgency Urgency,
    string PropertyName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AppointmentAt,
    TechnicianContact? Technician)
{
    public bool IsOpen => !RequestStateMachine.IsFinal(Status);
}

public sealed record StatusStep(RequestStatus? From, RequestStatus To, DateTimeOffset At, string? Comment);

public sealed record RequestDetail(
    RequestSummary Summary,
    string Description,
    DateTimeOffset? SlaDeadline,
    bool IsSlaBreached,
    bool IsUnderWarranty,
    IReadOnlyList<Guid> PhotoIds,
    Guid? VoiceNoteId,
    IReadOnlyList<StatusStep> History,
    IReadOnlyList<TimeSlot> ProposedSlots,
    IReadOnlyList<Guid> AfterPhotoIds,
    string? Report,
    DateOnly? WarrantyEndsOn,
    int? RatingScore,
    string? RatingComment,
    string? DisputeReason)
{
    public bool CanChooseSlot => Summary.Status == RequestStatus.AppointmentProposed && ProposedSlots.Count > 0;

    public bool CanValidate => Summary.Status == RequestStatus.Completed;

    public bool CanCancel => Summary.Status is RequestStatus.AwaitingApproval or RequestStatus.Submitted
        or RequestStatus.UnderReview or RequestStatus.AppointmentProposed or RequestStatus.AppointmentConfirmed;
}

/// <summary>Requests visible to a customer (only properties they can access).</summary>
public sealed class RequestQueries(IApplicationDbContext db, AccessControl access, TimeProvider clock)
{
    public async Task<IReadOnlyList<RequestSummary>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var propertyIds = await access.PropertyIdsOfAsync(userId, cancellationToken);
        var requests = await db.ServiceRequests.AsNoTracking()
            .Where(r => propertyIds.Contains(r.PropertyId))
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return await SummariesAsync(requests, cancellationToken);
    }

    public async Task<RequestDetail?> DetailAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null || await access.PropertyAsync(userId, request.PropertyId, cancellationToken) is null)
        {
            return null;
        }

        var summary = (await SummariesAsync([request], cancellationToken))[0];
        var intervention = await db.Interventions.AsNoTracking()
            .Where(i => i.RequestId == request.Id)
            .OrderByDescending(i => i.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var appointment = request.CurrentAppointment;

        return new RequestDetail(
            summary,
            request.Description,
            request.SlaDeadline,
            request.IsSlaBreached(clock.GetUtcNow()),
            request.IsUnderWarranty,
            [.. request.Attachments.Where(a => a.Kind == MediaKind.Photo).Select(a => a.MediaFileId)],
            request.Attachments.Where(a => a.Kind == MediaKind.VoiceNote).Select(a => (Guid?)a.MediaFileId).FirstOrDefault(),
            [.. request.History.OrderBy(h => h.At).Select(h => new StatusStep(h.From, h.To, h.At, h.Comment))],
            appointment?.Status == AppointmentStatus.Proposed ? [.. appointment.ProposedSlots.OrderBy(s => s.Start)] : [],
            intervention is null ? [] : [.. intervention.Photos.Where(p => p.Kind == MediaKind.PhotoAfter).Select(p => p.MediaFileId)],
            intervention?.Report,
            intervention?.WarrantyEndsOn,
            request.Rating?.Score,
            request.Rating?.Comment,
            request.DisputeReason);
    }

    internal async Task<IReadOnlyList<RequestSummary>> SummariesAsync(IReadOnlyList<ServiceRequest> requests, CancellationToken cancellationToken)
    {
        var requestIds = requests.Select(r => r.Id).ToList();
        var propertyIds = requests.Select(r => r.PropertyId).Distinct().ToList();
        var propertyNames = await db.Properties.AsNoTracking()
            .Where(p => propertyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var technicians = await (
                from i in db.Interventions
                join t in db.Technicians on i.LeadTechnicianId equals t.Id
                join u in db.Users on t.UserId equals u.Id
                where requestIds.Contains(i.RequestId)
                select new { i.RequestId, u.FullName, u.PhoneNumber })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. requests.Select(r =>
        {
            var tech = technicians.FirstOrDefault(t => t.RequestId == r.Id);
            var appointment = r.Appointments.Where(a => a.Status == AppointmentStatus.Confirmed).Select(a => a.ChosenSlot?.Start).LastOrDefault();
            return new RequestSummary(
                r.Id,
                r.Number,
                r.Domain,
                Title(r.Description),
                r.Status,
                r.Urgency,
                propertyNames.GetValueOrDefault(r.PropertyId, string.Empty),
                r.CreatedAt,
                appointment,
                tech is null ? null : new TechnicianContact(tech.FullName ?? string.Empty, tech.PhoneNumber.E164));
        })];
    }

    /// <summary>First line of the description, as a short title for cards.</summary>
    private static string Title(string description)
    {
        var line = description.Split('\n', 2)[0].Trim();
        return line.Length <= 70 ? line : string.Concat(line.AsSpan(0, 67), "…");
    }
}
