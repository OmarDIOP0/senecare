namespace SeneCare.Domain.Common;

public sealed record TimeSlot
{
    public TimeSlot(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new DomainException("timeslot.invalid", "A time slot must end after it starts.");
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; private init; }

    public DateTimeOffset End { get; private init; }

    public TimeSpan Duration => End - Start;

    public bool Overlaps(TimeSlot other) => Start < other.End && other.Start < End;
}

/// <summary>Inclusive range of calendar days (billing period, diagnostic coverage…).</summary>
public sealed record DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new DomainException("daterange.invalid", "A period must end on or after its start.");
        }

        Start = start;
        End = end;
    }

    public DateOnly Start { get; private init; }

    public DateOnly End { get; private init; }

    public bool Contains(DateOnly day) => day >= Start && day <= End;

    /// <summary>Period starting on <paramref name="start"/> and lasting <paramref name="months"/> months.</summary>
    public static DateRange OfMonths(DateOnly start, int months)
    {
        if (months <= 0)
        {
            throw new DomainException("daterange.months", "A period lasts at least one month.");
        }

        return new DateRange(start, start.AddMonths(months).AddDays(-1));
    }

    public DateRange Next(int months) => OfMonths(End.AddDays(1), months);
}

/// <summary>GPS position (WGS 84).</summary>
public sealed record GeoPoint
{
    public GeoPoint(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new DomainException("geo.invalid", "Invalid GPS coordinates.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; private init; }

    public double Longitude { get; private init; }
}

/// <summary>Reference from an aggregate to a <see cref="MediaFile"/> (owned value).</summary>
public sealed record MediaAttachment(Guid MediaFileId, MediaKind Kind);
