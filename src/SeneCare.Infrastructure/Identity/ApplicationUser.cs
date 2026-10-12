using Microsoft.AspNetCore.Identity;

namespace SeneCare.Infrastructure.Identity;

/// <summary>
/// Authentication record (security stamp, lockout, optional password later). Same Id as the domain
/// <see cref="Domain.Users.User"/>, which holds the business data (name, roles, consents).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>;

/// <summary>Rotating refresh token for the API. Only a hash is stored; a family groups successive rotations.</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ReplacedByTokenId is null && now < ExpiresAt;
}
