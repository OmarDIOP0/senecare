using SeneCare.Domain.Common;

namespace SeneCare.Domain.Locations;

/// <summary>Administrative reference data: Région → Département → Commune → Quartier. Seeded, editable by admins.</summary>
public abstract class Place : Entity
{
    public const int NameMaxLength = 120;

    protected Place()
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    protected Place(Guid id, string name, string slug)
        : base(id)
    {
        Name = Guard.Required(name, "place.name", NameMaxLength);
        Slug = Guard.Required(slug, "place.slug", NameMaxLength);
    }

    public string Name { get; private set; }

    /// <summary>Stable URL-friendly key, unique among siblings (« sicap-liberte »).</summary>
    public string Slug { get; private set; }

    public void Rename(string name) => Name = Guard.Required(name, "place.name", NameMaxLength);
}

public sealed class Region : Place
{
    private Region()
    {
    }

    public Region(Guid id, string name, string slug)
        : base(id, name, slug)
    {
    }
}

public sealed class Department : Place
{
    private Department()
    {
    }

    public Department(Guid id, Guid regionId, string name, string slug)
        : base(id, name, slug) => RegionId = regionId;

    public Guid RegionId { get; private set; }
}

public sealed class Commune : Place
{
    private Commune()
    {
    }

    public Commune(Guid id, Guid departmentId, string name, string slug)
        : base(id, name, slug) => DepartmentId = departmentId;

    public Guid DepartmentId { get; private set; }
}

public sealed class Neighborhood : Place
{
    private Neighborhood()
    {
    }

    public Neighborhood(Guid id, Guid communeId, string name, string slug)
        : base(id, name, slug) => CommuneId = communeId;

    public Guid CommuneId { get; private set; }

    /// <summary>Set when a customer added it (missing from the reference data): an admin checks it later.</summary>
    public Guid? SuggestedByUserId { get; private set; }

    public DateTimeOffset? SuggestedAt { get; private set; }

    public bool IsSuggested => SuggestedAt is not null;

    /// <summary>A neighborhood typed by a customer because the list did not have it.</summary>
    public static Neighborhood Suggest(Guid communeId, string name, Guid userId, DateTimeOffset now)
    {
        var trimmed = Guard.Required(name, "place.name", NameMaxLength).Trim();
        var slug = Common.Slug.From(trimmed);
        if (slug.Length < 2)
        {
            throw new DomainException("place.name", "A neighborhood name needs letters.");
        }

        return new Neighborhood(Guid.CreateVersion7(), communeId, trimmed, slug)
        {
            SuggestedByUserId = userId,
            SuggestedAt = now,
        };
    }
}
