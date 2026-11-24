using SeneCare.Domain.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Domain.Locations;

/// <summary>
/// « Prévenez-moi »: someone outside the covered zones leaves a number to be told when SENECARE opens there. Personal
/// data (law 2008-12): kept only for that purpose, erased by an admin once handled.
/// </summary>
public sealed class CoverageRequest : Entity
{
    public const int LocalityMaxLength = 120;
    public const int NameMaxLength = 120;

    private CoverageRequest()
    {
        Locality = string.Empty;
        PhoneNumber = null!;
    }

    public CoverageRequest(Guid regionId, Guid? communeId, string? locality, string? fullName, PhoneNumber phoneNumber, DateTimeOffset now)
    {
        RegionId = regionId;
        CommuneId = communeId;
        Locality = Guard.Optional(locality, "coverage.locality", LocalityMaxLength) ?? string.Empty;
        FullName = Guard.Optional(fullName, "coverage.name", NameMaxLength);
        PhoneNumber = phoneNumber ?? throw new DomainException("coverage.phone", "A phone number is required.");
        CreatedAt = now;
    }

    public Guid RegionId { get; private set; }

    public Guid? CommuneId { get; private set; }

    /// <summary>Town or neighborhood as typed (often missing from the reference data).</summary>
    public string Locality { get; private set; }

    public string? FullName { get; private set; }

    public PhoneNumber PhoneNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
