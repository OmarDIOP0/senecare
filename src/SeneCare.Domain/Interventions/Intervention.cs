using SeneCare.Domain.Common;

namespace SeneCare.Domain.Interventions;

public sealed class ChecklistItem : Entity
{
    public const int LabelMaxLength = 200;

    private ChecklistItem()
    {
        Label = string.Empty;
    }

    internal ChecklistItem(string label) => Label = Guard.Required(label, "checklist.label", LabelMaxLength);

    public string Label { get; private set; }

    public bool IsDone { get; private set; }

    public string? Comment { get; private set; }

    internal void Mark(bool done, string? comment)
    {
        IsDone = done;
        Comment = Guard.Optional(comment, "checklist.comment", 500);
    }
}

public sealed record PartUsed
{
    public const int NameMaxLength = 150;

    public PartUsed(string name, decimal quantity, string unit, Money? unitCost)
    {
        if (quantity <= 0)
        {
            throw new DomainException("part.quantity", "Quantity must be positive.");
        }

        Name = Guard.Required(name, "part.name", NameMaxLength);
        Quantity = quantity;
        Unit = Guard.Required(unit, "part.unit", 20);
        UnitCost = unitCost;
    }

    public string Name { get; private init; }

    /// <summary>Decimal to allow « 2,5 m » of cable.</summary>
    public decimal Quantity { get; private init; }

    public string Unit { get; private init; }

    public Money? UnitCost { get; private init; }
}

/// <summary>Field work done by technicians for a request: checklist, photos, parts, signature, report, warranty.</summary>
public sealed class Intervention : AggregateRoot
{
    public const int ReportMaxLength = 4000;

    private readonly List<Guid> _teamTechnicianIds = [];
    private readonly List<ChecklistItem> _checklist = [];
    private readonly List<MediaAttachment> _photos = [];
    private readonly List<PartUsed> _partsUsed = [];

    private Intervention()
    {
    }

    public Intervention(Guid requestId, Guid propertyId, ServiceDomain domain, Guid leadTechnicianId, IEnumerable<string> checklistLabels)
    {
        RequestId = requestId;
        PropertyId = propertyId;
        Domain = domain;
        LeadTechnicianId = leadTechnicianId;
        _checklist.AddRange(checklistLabels.Select(l => new ChecklistItem(l)));
    }

    public Guid RequestId { get; private set; }

    public Guid PropertyId { get; private set; }

    public ServiceDomain Domain { get; private set; }

    public Guid LeadTechnicianId { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? CustomerSignatureMediaFileId { get; private set; }

    public string? SignedByName { get; private set; }

    /// <summary>Why there is no signature (customer absent, tenant refused…).</summary>
    public string? NoSignatureReason { get; private set; }

    public string? Report { get; private set; }

    public Guid? ReportMediaFileId { get; private set; }

    public DateOnly? WarrantyEndsOn { get; private set; }

    public IReadOnlyCollection<Guid> TeamTechnicianIds => _teamTechnicianIds.AsReadOnly();

    public IReadOnlyCollection<ChecklistItem> Checklist => _checklist.AsReadOnly();

    public IReadOnlyCollection<MediaAttachment> Photos => _photos.AsReadOnly();

    public IReadOnlyCollection<PartUsed> PartsUsed => _partsUsed.AsReadOnly();

    public bool IsCompleted => CompletedAt is not null;

    public TimeSpan? Duration => CompletedAt - StartedAt;

    public void AddTeamMember(Guid technicianId)
    {
        if (technicianId != LeadTechnicianId && !_teamTechnicianIds.Contains(technicianId))
        {
            _teamTechnicianIds.Add(technicianId);
        }
    }

    public bool Involves(Guid technicianId) => technicianId == LeadTechnicianId || _teamTechnicianIds.Contains(technicianId);

    public void Start(DateTimeOffset now)
    {
        EnsureNotCompleted();
        StartedAt ??= now;
    }

    public void AddChecklistItem(string label)
    {
        EnsureNotCompleted();
        _checklist.Add(new ChecklistItem(label));
    }

    public void MarkChecklistItem(Guid itemId, bool done, string? comment)
    {
        EnsureNotCompleted();
        var item = _checklist.Find(i => i.Id == itemId)
            ?? throw new DomainException("checklist.not_found", "Checklist item not found.");
        item.Mark(done, comment);
    }

    public void AddPhoto(Guid mediaFileId, MediaKind kind)
    {
        EnsureNotCompleted();
        if (kind is not (MediaKind.PhotoBefore or MediaKind.PhotoAfter))
        {
            throw new DomainException("intervention.photo_kind", "Photos must be marked before or after.");
        }

        _photos.Add(new MediaAttachment(mediaFileId, kind));
    }

    public void AddPart(PartUsed part)
    {
        ArgumentNullException.ThrowIfNull(part);
        EnsureNotCompleted();
        _partsUsed.Add(part);
    }

    public void SignByCustomer(Guid signatureMediaFileId, string signedByName)
    {
        EnsureNotCompleted();
        CustomerSignatureMediaFileId = signatureMediaFileId;
        SignedByName = Guard.Required(signedByName, "intervention.signed_by", 120);
        NoSignatureReason = null;
    }

    /// <summary>
    /// Closes the intervention. Requires it to be started, at least one « after » photo, and either the
    /// customer's signature or a reason for its absence. Warranty runs <paramref name="warrantyDays"/> days.
    /// </summary>
    public void Complete(string report, string? noSignatureReason, int warrantyDays, DateTimeOffset now)
    {
        EnsureNotCompleted();
        if (StartedAt is null)
        {
            throw new DomainException("intervention.not_started", "The intervention has not started.");
        }

        if (!_photos.Exists(p => p.Kind == MediaKind.PhotoAfter))
        {
            throw new DomainException("intervention.after_photo", "Add at least one photo after the work.");
        }

        if (CustomerSignatureMediaFileId is null)
        {
            NoSignatureReason = Guard.Required(noSignatureReason, "intervention.signature", 300);
        }

        if (warrantyDays < 0)
        {
            throw new DomainException("intervention.warranty", "Warranty cannot be negative.");
        }

        Report = Guard.Required(report, "intervention.report", ReportMaxLength);
        CompletedAt = now;
        WarrantyEndsOn = DateOnly.FromDateTime(now.UtcDateTime).AddDays(warrantyDays);
    }

    public void AttachReportDocument(Guid mediaFileId) => ReportMediaFileId = mediaFileId;

    /// <summary>Erasure of the customer: the signer's name and the free-text report may identify them.</summary>
    public void Redact(string placeholder)
    {
        SignedByName = SignedByName is null ? null : placeholder;
        NoSignatureReason = NoSignatureReason is null ? null : placeholder;
        Report = Report is null ? null : placeholder;
    }

    /// <summary>True when a new issue of the same domain on the same property is covered by this work.</summary>
    public bool CoversUnderWarranty(Guid propertyId, ServiceDomain domain, DateOnly day) =>
        WarrantyEndsOn is { } end && PropertyId == propertyId && Domain == domain && day <= end;

    private void EnsureNotCompleted()
    {
        if (IsCompleted)
        {
            throw new DomainException("intervention.completed", "This intervention is already closed.");
        }
    }
}
