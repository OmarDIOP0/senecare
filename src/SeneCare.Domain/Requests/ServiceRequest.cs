using SeneCare.Domain.Common;

namespace SeneCare.Domain.Requests;

public sealed record RequestStatusChange(
    RequestStatus? From, RequestStatus To, DateTimeOffset At, Guid? ByUserId, string? Comment);

public sealed record ServiceRequestStatusChanged(
    Guid RequestId, RequestStatus From, RequestStatus To, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Customer's rating given when validating the intervention.</summary>
public sealed record Rating
{
    public const int CommentMaxLength = 1000;

    public Rating(int score, string? comment, DateTimeOffset givenAt)
    {
        if (score is < 1 or > 5)
        {
            throw new DomainException("rating.score", "The score must be between 1 and 5.");
        }

        Score = score;
        Comment = Guard.Optional(comment, "rating.comment", CommentMaxLength);
        GivenAt = givenAt;
    }

    public int Score { get; private init; }

    public string? Comment { get; private init; }

    public DateTimeOffset GivenAt { get; private init; }
}

/// <summary>
/// « Signalement »: an issue reported on a property. Follows <see cref="RequestStateMachine"/>,
/// keeps a timestamped history and computes its SLA deadline from the plan's delay for its urgency.
/// </summary>
public sealed class ServiceRequest : AggregateRoot
{
    public const int DescriptionMaxLength = 2000;
    public const int MaxPhotos = 6;

    private readonly List<RequestStatusChange> _history = [];
    private readonly List<MediaAttachment> _attachments = [];
    private readonly List<TimeSlot> _preferredSlots = [];
    private readonly List<Appointment> _appointments = [];

    private ServiceRequest()
    {
        Number = string.Empty;
        Description = string.Empty;
    }

    private ServiceRequest(
        string number,
        Guid accountId,
        Guid propertyId,
        Guid reportedByUserId,
        ServiceDomain domain,
        string description,
        Urgency urgency)
    {
        Number = Guard.Required(number, "request.number", 32);
        AccountId = accountId;
        PropertyId = propertyId;
        ReportedByUserId = reportedByUserId;
        Domain = domain;
        Description = Guard.Required(description, "request.description", DescriptionMaxLength);
        Urgency = urgency;
    }

    /// <summary>Human-readable number, e.g. « SC-2026-000123 ».</summary>
    public string Number { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid PropertyId { get; private set; }

    public Guid ReportedByUserId { get; private set; }

    public ServiceDomain Domain { get; private set; }

    public string Description { get; private set; }

    public Urgency Urgency { get; private set; }

    public RequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the request entered the SENECARE queue (after approval when required).</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>Time by which a technician must be on site; null for planned work.</summary>
    public DateTimeOffset? SlaDeadline { get; private set; }

    /// <summary>First time a technician started working on site (SLA end).</summary>
    public DateTimeOffset? RespondedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Previous intervention whose warranty covers this request, if any.</summary>
    public Guid? WarrantyInterventionId { get; private set; }

    public Rating? Rating { get; private set; }

    public string? DisputeReason { get; private set; }

    public IReadOnlyCollection<RequestStatusChange> History => _history.AsReadOnly();

    public IReadOnlyCollection<MediaAttachment> Attachments => _attachments.AsReadOnly();

    public IReadOnlyCollection<TimeSlot> PreferredSlots => _preferredSlots.AsReadOnly();

    public IReadOnlyCollection<Appointment> Appointments => _appointments.AsReadOnly();

    public Appointment? CurrentAppointment =>
        _appointments.LastOrDefault(a => a.Status is AppointmentStatus.Proposed or AppointmentStatus.Confirmed);

    public bool IsUnderWarranty => WarrantyInterventionId is not null;

    /// <summary>
    /// Creates a request. <paramref name="slaDelay"/> comes from the subscription's plan for this urgency
    /// (null for planned work). With <paramref name="requiresApproval"/>, it waits for a site manager.
    /// </summary>
    public static ServiceRequest Create(
        string number,
        Guid accountId,
        Guid propertyId,
        Guid reportedByUserId,
        ServiceDomain domain,
        string description,
        Urgency urgency,
        TimeSpan? slaDelay,
        bool requiresApproval,
        IEnumerable<TimeSlot> preferredSlots,
        DateTimeOffset now)
    {
        var request = new ServiceRequest(number, accountId, propertyId, reportedByUserId, domain, description, urgency)
        {
            CreatedAt = now,
        };
        request._preferredSlots.AddRange(preferredSlots.Select(s => s with { }));

        if (requiresApproval)
        {
            request.Status = RequestStatus.AwaitingApproval;
            request._history.Add(new RequestStatusChange(null, RequestStatus.AwaitingApproval, now, reportedByUserId, null));
        }
        else
        {
            request.Status = RequestStatus.Submitted;
            request._history.Add(new RequestStatusChange(null, RequestStatus.Submitted, now, reportedByUserId, null));
            request.StartSla(slaDelay, now);
        }

        return request;
    }

    public void AddAttachment(Guid mediaFileId, MediaKind kind)
    {
        if (kind is not (MediaKind.Photo or MediaKind.VoiceNote))
        {
            throw new DomainException("request.attachment_kind", "Only photos and voice notes can be attached.");
        }

        if (kind == MediaKind.Photo && _attachments.Count(a => a.Kind == MediaKind.Photo) >= MaxPhotos)
        {
            throw new DomainException("request.too_many_photos", "Too many photos for one request.");
        }

        _attachments.Add(new MediaAttachment(mediaFileId, kind));
    }

    public void Approve(Guid approverUserId, TimeSpan? slaDelay, DateTimeOffset now)
    {
        TransitionTo(RequestStatus.Submitted, approverUserId, null, now);
        StartSla(slaDelay, now);
    }

    public void StartReview(Guid dispatcherUserId, DateTimeOffset now) =>
        TransitionTo(RequestStatus.UnderReview, dispatcherUserId, null, now);

    /// <summary>Dispatcher changes the urgency; the SLA restarts from now with the new delay.</summary>
    public void Requalify(Urgency urgency, TimeSpan? slaDelay, Guid dispatcherUserId, DateTimeOffset now)
    {
        if (Status is not (RequestStatus.Submitted or RequestStatus.UnderReview or RequestStatus.AppointmentProposed))
        {
            throw new DomainException("request.requalify", "Urgency can only change before the appointment is confirmed.");
        }

        if (urgency == Urgency)
        {
            return;
        }

        var comment = $"Urgency {Urgency} → {urgency}";
        Urgency = urgency;
        StartSla(slaDelay, now);
        _history.Add(new RequestStatusChange(Status, Status, now, dispatcherUserId, comment));
    }

    public Appointment ProposeAppointment(IEnumerable<TimeSlot> slots, Guid dispatcherUserId, DateTimeOffset now)
    {
        var appointment = Appointment.Propose(slots, dispatcherUserId, now);
        TransitionTo(RequestStatus.AppointmentProposed, dispatcherUserId, null, now);
        CurrentAppointment?.Cancel();
        _appointments.Add(appointment);
        return appointment;
    }

    /// <summary>Customer picks one of the proposed slots.</summary>
    public void ConfirmAppointment(TimeSlot chosenSlot, Guid userId, DateTimeOffset now)
    {
        var appointment = CurrentAppointment
            ?? throw new DomainException("request.no_appointment", "No appointment has been proposed.");
        TransitionTo(RequestStatus.AppointmentConfirmed, userId, null, now);
        appointment.Confirm(chosenSlot, now);
    }

    /// <summary>Critical urgency: the dispatcher sends a technician right away, without customer choice.</summary>
    public void DispatchImmediately(TimeSlot slot, Guid dispatcherUserId, DateTimeOffset now)
    {
        var appointment = Appointment.Propose([slot], dispatcherUserId, now);
        TransitionTo(RequestStatus.AppointmentConfirmed, dispatcherUserId, "Immediate dispatch", now);
        appointment.Confirm(slot, now);
        _appointments.Add(appointment);
    }

    public void MarkOnTheWay(Guid technicianUserId, DateTimeOffset now) =>
        TransitionTo(RequestStatus.OnTheWay, technicianUserId, null, now);

    public void StartWork(Guid technicianUserId, DateTimeOffset now)
    {
        TransitionTo(RequestStatus.InProgress, technicianUserId, null, now);
        RespondedAt ??= now;
    }

    public void Complete(Guid technicianUserId, DateTimeOffset now)
    {
        TransitionTo(RequestStatus.Completed, technicianUserId, null, now);
        CompletedAt = now;
    }

    public void ValidateByCustomer(Guid userId, Rating rating, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rating);
        TransitionTo(RequestStatus.ValidatedByCustomer, userId, null, now);
        Rating = rating;
    }

    /// <summary>Daily job: a completed request without answer after <paramref name="afterDays"/> days is validated.</summary>
    public bool TryAutoValidate(int afterDays, DateTimeOffset now)
    {
        if (Status != RequestStatus.Completed || CompletedAt is null || now < CompletedAt.Value.AddDays(afterDays))
        {
            return false;
        }

        TransitionTo(RequestStatus.ValidatedByCustomer, null, "Automatic validation", now);
        return true;
    }

    public void Dispute(Guid userId, string reason, DateTimeOffset now)
    {
        var trimmed = Guard.Required(reason, "request.dispute_reason", Rating.CommentMaxLength);
        TransitionTo(RequestStatus.Disputed, userId, trimmed, now);
        DisputeReason = trimmed;
    }

    public void Reopen(Guid dispatcherUserId, DateTimeOffset now) =>
        TransitionTo(RequestStatus.UnderReview, dispatcherUserId, "Reopened after dispute", now);

    public void Cancel(Guid userId, string? reason, DateTimeOffset now)
    {
        TransitionTo(RequestStatus.Cancelled, userId, Guard.Optional(reason, "request.cancel_reason", 500), now);
        CurrentAppointment?.Cancel();
    }

    public void MarkUnderWarranty(Guid interventionId) => WarrantyInterventionId = interventionId;

    /// <summary>True when the technician was not on site in time (or still isn't, past the deadline).</summary>
    public bool IsSlaBreached(DateTimeOffset now) =>
        SlaDeadline is { } deadline && (RespondedAt ?? now) > deadline;

    private void StartSla(TimeSpan? slaDelay, DateTimeOffset now)
    {
        SubmittedAt ??= now;
        SlaDeadline = Urgency == Urgency.Planned || slaDelay is null ? null : now + slaDelay.Value;
    }

    private void TransitionTo(RequestStatus target, Guid? byUserId, string? comment, DateTimeOffset now)
    {
        if (!RequestStateMachine.CanTransition(Status, target))
        {
            throw new DomainException(
                "request.invalid_transition",
                $"A request cannot go from {Status} to {target}.");
        }

        var from = Status;
        Status = target;
        _history.Add(new RequestStatusChange(from, target, now, byUserId, comment));
        Raise(new ServiceRequestStatusChanged(Id, from, target, now));
    }
}
