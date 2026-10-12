using SeneCare.Domain.Common;

namespace SeneCare.Domain.Accounts;

public enum PropertyKind
{
    House = 1,
    Villa = 2,
    Apartment = 3,
    Office = 10,
    Shop = 11,
    Warehouse = 12,
    Other = 99,
}

/// <summary>
/// Address adapted to Senegal: administrative neighborhood + GPS point + landmark
/// (« derrière la mosquée, portail vert »), because street addresses are often missing.
/// </summary>
public sealed record Address
{
    public const int LandmarkMaxLength = 250;
    public const int DetailsMaxLength = 250;

    // For EF Core materialization (nested GeoPoint cannot be bound through the constructor).
    private Address()
    {
    }

    public Address(Guid neighborhoodId, GeoPoint? location, string? landmark, string? details)
    {
        if (neighborhoodId == Guid.Empty)
        {
            throw new DomainException("address.neighborhood", "A neighborhood is required.");
        }

        if (location is null && string.IsNullOrWhiteSpace(landmark))
        {
            throw new DomainException("address.locate", "Give a GPS position or a landmark so the technician can find the place.");
        }

        NeighborhoodId = neighborhoodId;
        Location = location;
        Landmark = Guard.Optional(landmark, "address.landmark", LandmarkMaxLength);
        Details = Guard.Optional(details, "address.details", DetailsMaxLength);
    }

    public Guid NeighborhoodId { get; private init; }

    public GeoPoint? Location { get; private init; }

    public string? Landmark { get; private init; }

    /// <summary>Street, building, floor, door…</summary>
    public string? Details { get; private init; }
}

/// <summary>A building looked after by SENECARE: a home of a household, or a site of a business.</summary>
public sealed class Property : AggregateRoot
{
    public const int NameMaxLength = 120;
    public const int MaxPhotos = 10;

    private readonly List<MediaAttachment> _photos = [];

    private Property()
    {
        Name = string.Empty;
        Address = null!;
    }

    public Property(Guid accountId, PropertyKind kind, string name, Address address, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(address);
        AccountId = accountId;
        Kind = kind;
        Name = Guard.Required(name, "property.name", NameMaxLength);
        Address = address;
        CreatedAt = now;
        IsActive = true;
    }

    public Guid AccountId { get; private set; }

    public PropertyKind Kind { get; private set; }

    /// <summary>Friendly name: « Maison de Yoff », « Agence Plateau ».</summary>
    public string Name { get; private set; }

    public Address Address { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<MediaAttachment> Photos => _photos.AsReadOnly();

    public void Update(PropertyKind kind, string name, Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Kind = kind;
        Name = Guard.Required(name, "property.name", NameMaxLength);
        Address = address;
    }

    public void AddPhoto(Guid mediaFileId)
    {
        if (_photos.Count >= MaxPhotos)
        {
            throw new DomainException("property.too_many_photos", "Too many photos for this property.");
        }

        _photos.Add(new MediaAttachment(mediaFileId, MediaKind.Photo));
    }

    public void RemovePhoto(Guid mediaFileId) => _photos.RemoveAll(p => p.MediaFileId == mediaFileId);

    public void Archive() => IsActive = false;
}
