using SeneCare.Domain.Common;

namespace SeneCare.Domain.Neighborhoods;

public enum NeighborhoodProgramStatus
{
    /// <summary>Collecting members until the threshold is reached.</summary>
    Gathering = 1,

    /// <summary>Threshold reached: greening operations every 3 months.</summary>
    Active = 2,
}

public sealed record NeighborhoodProgramActivated(Guid ProgramId, Guid NeighborhoodId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Reforestation / embellishment operation carried out in an active neighborhood.</summary>
public sealed class GreeningOperation : Entity
{
    private readonly List<MediaAttachment> _photos = [];

    private GreeningOperation()
    {
        Description = string.Empty;
    }

    internal GreeningOperation(DateOnly date, int treesPlanted, string description)
    {
        if (treesPlanted < 0)
        {
            throw new DomainException("greening.trees", "The number of trees cannot be negative.");
        }

        Date = date;
        TreesPlanted = treesPlanted;
        Description = Guard.Required(description, "greening.description", 2000);
    }

    public DateOnly Date { get; private set; }

    public int TreesPlanted { get; private set; }

    public string Description { get; private set; }

    public bool IsPublished { get; private set; }

    public IReadOnlyCollection<MediaAttachment> Photos => _photos.AsReadOnly();

    internal void AddPhoto(Guid mediaFileId) => _photos.Add(new MediaAttachment(mediaFileId, MediaKind.Photo));

    internal void Publish() => IsPublished = true;
}

/// <summary>
/// « Programme Quartiers »: once <see cref="Threshold"/> members subscribe in the same neighborhood,
/// SENECARE runs a greening operation every 3 months. The program stays active once reached.
/// </summary>
public sealed class NeighborhoodProgram : AggregateRoot
{
    public const int DefaultThreshold = 500;

    private readonly List<GreeningOperation> _operations = [];

    private NeighborhoodProgram()
    {
    }

    public NeighborhoodProgram(Guid neighborhoodId, int threshold)
    {
        NeighborhoodId = neighborhoodId;
        Status = NeighborhoodProgramStatus.Gathering;
        ChangeThreshold(threshold, DateTimeOffset.MinValue);
    }

    public Guid NeighborhoodId { get; private set; }

    public int MemberCount { get; private set; }

    public int Threshold { get; private set; }

    public NeighborhoodProgramStatus Status { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public IReadOnlyCollection<GreeningOperation> Operations => _operations.AsReadOnly();

    public int TotalTreesPlanted => _operations.Sum(o => o.TreesPlanted);

    /// <summary>Progress towards the threshold, 0–1 (« 347 / 500 voisins »).</summary>
    public double Progress => Math.Min(1d, (double)MemberCount / Threshold);

    public void UpdateMemberCount(int memberCount, DateTimeOffset now)
    {
        if (memberCount < 0)
        {
            throw new DomainException("neighborhood.members", "Member count cannot be negative.");
        }

        MemberCount = memberCount;
        ActivateIfThresholdReached(now);
    }

    public void ChangeThreshold(int threshold, DateTimeOffset now)
    {
        if (threshold <= 0)
        {
            throw new DomainException("neighborhood.threshold", "The threshold must be positive.");
        }

        Threshold = threshold;
        ActivateIfThresholdReached(now);
    }

    public GreeningOperation RecordOperation(DateOnly date, int treesPlanted, string description)
    {
        if (Status != NeighborhoodProgramStatus.Active)
        {
            throw new DomainException("neighborhood.not_active", "The neighborhood has not reached its threshold yet.");
        }

        var operation = new GreeningOperation(date, treesPlanted, description);
        _operations.Add(operation);
        return operation;
    }

    public void AddOperationPhoto(Guid operationId, Guid mediaFileId) => GetOperation(operationId).AddPhoto(mediaFileId);

    public void PublishOperation(Guid operationId) => GetOperation(operationId).Publish();

    private GreeningOperation GetOperation(Guid operationId) =>
        _operations.Find(o => o.Id == operationId)
        ?? throw new DomainException("neighborhood.operation_not_found", "Operation not found.");

    private void ActivateIfThresholdReached(DateTimeOffset now)
    {
        if (Status == NeighborhoodProgramStatus.Gathering && MemberCount >= Threshold)
        {
            Status = NeighborhoodProgramStatus.Active;
            ActivatedAt = now;
            Raise(new NeighborhoodProgramActivated(Id, NeighborhoodId, now));
        }
    }
}
