using SeneCare.Domain.Common;

namespace SeneCare.Domain.Technicians;

/// <summary>Recurring working hours (Africa/Dakar is UTC+0 all year, so times are UTC).</summary>
public sealed record WeeklyAvailability
{
    public WeeklyAvailability(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        if (end <= start)
        {
            throw new DomainException("availability.invalid", "Working hours must end after they start.");
        }

        Day = day;
        Start = start;
        End = end;
    }

    public DayOfWeek Day { get; private init; }

    public TimeOnly Start { get; private init; }

    public TimeOnly End { get; private init; }

    public bool Covers(TimeSlot slot) =>
        slot.Start.UtcDateTime.Date == slot.End.UtcDateTime.Date
        && slot.Start.UtcDateTime.DayOfWeek == Day
        && TimeOnly.FromDateTime(slot.Start.UtcDateTime) >= Start
        && TimeOnly.FromDateTime(slot.End.UtcDateTime) <= End;
}

public sealed record Unavailability
{
    public Unavailability(TimeSlot slot, string? reason)
    {
        Slot = slot;
        Reason = reason;
    }

    // For EF Core materialization (nested TimeSlot cannot be bound through the constructor).
    private Unavailability()
    {
        Slot = null!;
    }

    public TimeSlot Slot { get; private init; }

    public string? Reason { get; private init; }
}

/// <summary>SENECARE technician: skills (domains), covered communes, availability and rating.</summary>
public sealed class Technician : AggregateRoot
{
    private readonly List<ServiceDomain> _skills = [];
    private readonly List<Guid> _communeIds = [];
    private readonly List<WeeklyAvailability> _weeklyAvailability = [];
    private readonly List<Unavailability> _unavailabilities = [];

    private Technician()
    {
    }

    public Technician(Guid userId, IEnumerable<ServiceDomain> skills, IEnumerable<Guid> communeIds)
    {
        UserId = userId;
        IsActive = true;
        SetSkills(skills);
        SetZones(communeIds);
    }

    public Guid UserId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>« Technicien certifié SENECARE » badge.</summary>
    public bool IsCertified { get; private set; }

    public int RatingCount { get; private set; }

    public int RatingSum { get; private set; }

    public double? AverageRating => RatingCount == 0 ? null : (double)RatingSum / RatingCount;

    public IReadOnlyCollection<ServiceDomain> Skills => _skills.AsReadOnly();

    public IReadOnlyCollection<Guid> CommuneIds => _communeIds.AsReadOnly();

    public IReadOnlyCollection<WeeklyAvailability> WeeklyAvailability => _weeklyAvailability.AsReadOnly();

    public IReadOnlyCollection<Unavailability> Unavailabilities => _unavailabilities.AsReadOnly();

    public void SetSkills(IEnumerable<ServiceDomain> skills)
    {
        var list = skills.Distinct().ToList();
        if (list.Count == 0)
        {
            throw new DomainException("technician.skills", "A technician has at least one skill.");
        }

        _skills.Clear();
        _skills.AddRange(list);
    }

    public void SetZones(IEnumerable<Guid> communeIds)
    {
        var list = communeIds.Distinct().ToList();
        if (list.Count == 0)
        {
            throw new DomainException("technician.zones", "A technician covers at least one commune.");
        }

        _communeIds.Clear();
        _communeIds.AddRange(list);
    }

    public void SetWeeklyAvailability(IEnumerable<WeeklyAvailability> slots)
    {
        _weeklyAvailability.Clear();
        _weeklyAvailability.AddRange(slots);
    }

    public void AddUnavailability(TimeSlot slot, string? reason) =>
        _unavailabilities.Add(new Unavailability(slot, Guard.Optional(reason, "technician.unavailability", 200)));

    public bool CanHandle(ServiceDomain domain, Guid communeId) =>
        IsActive && _skills.Contains(domain) && _communeIds.Contains(communeId);

    public bool IsAvailable(TimeSlot slot) =>
        IsActive
        && _weeklyAvailability.Exists(a => a.Covers(slot))
        && !_unavailabilities.Exists(u => u.Slot.Overlaps(slot));

    public void AddRating(int score)
    {
        if (score is < 1 or > 5)
        {
            throw new DomainException("rating.score", "The score must be between 1 and 5.");
        }

        RatingCount++;
        RatingSum += score;
    }

    public void Certify() => IsCertified = true;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
