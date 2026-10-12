using SeneCare.Domain.Common;

namespace SeneCare.Domain.Diagnostics;

public enum DiagnosticStatus
{
    Planned = 1,
    InProgress = 2,
    Submitted = 3,
    Validated = 4,
    Cancelled = 5,
}

public enum ControlPointResult
{
    Ok = 1,
    Anomaly = 2,
    NotApplicable = 3,
}

public sealed record DiagnosticValidated(Guid DiagnosticId, Guid SubscriptionId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>One checked item of a diagnostic, optionally tied to a specific site.</summary>
public sealed class ControlPoint : Entity
{
    public const int LabelMaxLength = 200;
    public const int CommentMaxLength = 1000;

    private readonly List<MediaAttachment> _photos = [];

    private ControlPoint()
    {
        Label = string.Empty;
    }

    internal ControlPoint(Guid? propertyId, ServiceDomain domain, string label, ControlPointResult result, string? comment)
    {
        PropertyId = propertyId;
        Domain = domain;
        Label = Guard.Required(label, "controlpoint.label", LabelMaxLength);
        Result = result;
        Comment = Guard.Optional(comment, "controlpoint.comment", CommentMaxLength);
    }

    public Guid? PropertyId { get; private set; }

    public ServiceDomain Domain { get; private set; }

    public string Label { get; private set; }

    public ControlPointResult Result { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Service request created from this anomaly (« convertir en demande en un clic »).</summary>
    public Guid? ConvertedRequestId { get; private set; }

    public IReadOnlyCollection<MediaAttachment> Photos => _photos.AsReadOnly();

    internal void AddPhoto(Guid mediaFileId) => _photos.Add(new MediaAttachment(mediaFileId, MediaKind.Photo));

    internal void MarkConverted(Guid requestId)
    {
        if (Result != ControlPointResult.Anomaly)
        {
            throw new DomainException("diagnostic.not_anomaly", "Only an anomaly can become a request.");
        }

        if (ConvertedRequestId is not null)
        {
            throw new DomainException("diagnostic.already_converted", "This anomaly already has a request.");
        }

        ConvertedRequestId = requestId;
    }
}

/// <summary>
/// Quarterly technical diagnostic of a business subscription. A <see cref="DiagnosticStatus.Validated"/>
/// diagnostic covering the current period is required before billing the next one.
/// </summary>
public sealed class Diagnostic : AggregateRoot
{
    public const int RecommendationsMaxLength = 4000;

    private readonly List<ControlPoint> _controlPoints = [];

    private Diagnostic()
    {
        PeriodCovered = null!;
    }

    public Diagnostic(Guid accountId, Guid subscriptionId, DateRange periodCovered, DateTimeOffset scheduledFor)
    {
        ArgumentNullException.ThrowIfNull(periodCovered);
        AccountId = accountId;
        SubscriptionId = subscriptionId;
        PeriodCovered = periodCovered with { };
        ScheduledFor = scheduledFor;
        Status = DiagnosticStatus.Planned;
    }

    public Guid AccountId { get; private set; }

    public Guid SubscriptionId { get; private set; }

    /// <summary>Billing period this diagnostic closes.</summary>
    public DateRange PeriodCovered { get; private set; }

    public DateTimeOffset ScheduledFor { get; private set; }

    public DiagnosticStatus Status { get; private set; }

    public Guid? TechnicianId { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public string? Recommendations { get; private set; }

    public Guid? ReportMediaFileId { get; private set; }

    public Guid? ValidatedByUserId { get; private set; }

    public DateTimeOffset? ValidatedAt { get; private set; }

    public IReadOnlyCollection<ControlPoint> ControlPoints => _controlPoints.AsReadOnly();

    public int AnomalyCount => _controlPoints.Count(c => c.Result == ControlPointResult.Anomaly);

    public void Reschedule(DateTimeOffset scheduledFor)
    {
        EnsureStatus(DiagnosticStatus.Planned);
        ScheduledFor = scheduledFor;
    }

    public void Start(Guid technicianId, DateTimeOffset now)
    {
        EnsureStatus(DiagnosticStatus.Planned);
        TechnicianId = technicianId;
        StartedAt = now;
        Status = DiagnosticStatus.InProgress;
    }

    public ControlPoint RecordControlPoint(Guid? propertyId, ServiceDomain domain, string label, ControlPointResult result, string? comment)
    {
        EnsureStatus(DiagnosticStatus.InProgress);
        var point = new ControlPoint(propertyId, domain, label, result, comment);
        _controlPoints.Add(point);
        return point;
    }

    public void AddControlPointPhoto(Guid controlPointId, Guid mediaFileId)
    {
        EnsureStatus(DiagnosticStatus.InProgress);
        GetControlPoint(controlPointId).AddPhoto(mediaFileId);
    }

    public void Submit(string? recommendations, Guid? reportMediaFileId, DateTimeOffset now)
    {
        EnsureStatus(DiagnosticStatus.InProgress);
        if (_controlPoints.Count == 0)
        {
            throw new DomainException("diagnostic.empty", "Record at least one control point before submitting.");
        }

        Recommendations = Guard.Optional(recommendations, "diagnostic.recommendations", RecommendationsMaxLength);
        ReportMediaFileId = reportMediaFileId;
        SubmittedAt = now;
        Status = DiagnosticStatus.Submitted;
    }

    /// <summary>Supervisor sends the report back to the technician for corrections.</summary>
    public void Reject()
    {
        EnsureStatus(DiagnosticStatus.Submitted);
        Status = DiagnosticStatus.InProgress;
    }

    public void Validate(Guid validatedByUserId, DateTimeOffset now)
    {
        EnsureStatus(DiagnosticStatus.Submitted);
        ValidatedByUserId = validatedByUserId;
        ValidatedAt = now;
        Status = DiagnosticStatus.Validated;
        Raise(new DiagnosticValidated(Id, SubscriptionId, now));
    }

    public void Cancel()
    {
        if (Status == DiagnosticStatus.Validated)
        {
            throw new DomainException("diagnostic.validated", "A validated diagnostic cannot be cancelled.");
        }

        Status = DiagnosticStatus.Cancelled;
    }

    public void MarkAnomalyConverted(Guid controlPointId, Guid requestId) =>
        GetControlPoint(controlPointId).MarkConverted(requestId);

    /// <summary>True when this diagnostic unlocks billing after <paramref name="period"/> for the subscription.</summary>
    public bool UnlocksRenewalOf(Guid subscriptionId, DateRange period) =>
        Status == DiagnosticStatus.Validated && SubscriptionId == subscriptionId && PeriodCovered == period;

    private ControlPoint GetControlPoint(Guid controlPointId) =>
        _controlPoints.Find(c => c.Id == controlPointId)
        ?? throw new DomainException("diagnostic.controlpoint_not_found", "Control point not found.");

    private void EnsureStatus(DiagnosticStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException("diagnostic.invalid_status", $"Operation not allowed while the diagnostic is {Status}.");
        }
    }
}
