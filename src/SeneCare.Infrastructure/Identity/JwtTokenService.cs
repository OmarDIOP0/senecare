using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Infrastructure.Persistence;

namespace SeneCare.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "senecare";

    public string Audience { get; set; } = "senecare-mobile";

    /// <summary>Base64 key (≥ 32 bytes). Secret, never in the repository.</summary>
    public string? SigningKey { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Long-lived: users on mobile data should not need a new SMS code every day.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(60);

    public SymmetricSecurityKey GetSecurityKey()
    {
        var bytes = string.IsNullOrWhiteSpace(SigningKey) ? [] : Convert.FromBase64String(SigningKey);
        return bytes.Length >= 32
            ? new SymmetricSecurityKey(bytes)
            : throw new InvalidOperationException("Jwt:SigningKey must be a base64 key of at least 32 bytes (see README).");
    }
}

/// <summary>
/// Short JWT access tokens + rotating refresh tokens. Re-using an already rotated refresh token
/// (sign of theft) revokes the whole family.
/// </summary>
internal sealed class JwtTokenService(
    SeneCareDbContext db,
    UserClaimsBuilder claimsBuilder,
    ISecretHasher hasher,
    IOptions<JwtOptions> options,
    TimeProvider clock) : IAuthTokenService
{
    public async Task<AuthTokens> IssueAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var (refresh, token) = NewRefreshToken(userId, Guid.CreateVersion7(), ipAddress);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(cancellationToken);
        return await CreateTokensAsync(userId, refresh, token, cancellationToken);
    }

    public async Task<AuthTokens?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var hash = hasher.Hash(refreshToken);
        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (!current.IsActive(now))
        {
            if (current.ReplacedByTokenId is not null)
            {
                await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            }

            return null;
        }

        var stillActive = await db.Users.AnyAsync(u => u.Id == current.UserId && u.IsActive, cancellationToken);
        if (!stillActive)
        {
            return null;
        }

        var (next, token) = NewRefreshToken(current.UserId, current.FamilyId, ipAddress);
        current.ReplacedByTokenId = next.Id;
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(cancellationToken);
        return await CreateTokensAsync(current.UserId, next, token, cancellationToken);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = hasher.Hash(refreshToken);
        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (current is not null)
        {
            await RevokeFamilyAsync(current.FamilyId, clock.GetUtcNow(), cancellationToken);
        }
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    private (RefreshToken Entity, string Token) NewRefreshToken(Guid userId, Guid familyId, string? ipAddress)
    {
        var token = Secrets.UrlToken(48);
        var now = clock.GetUtcNow();
        return (new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = hasher.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + options.Value.RefreshTokenLifetime,
            CreatedByIp = ipAddress,
        }, token);
    }

    private async Task<AuthTokens> CreateTokensAsync(Guid userId, RefreshToken refresh, string refreshToken, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expires = now + settings.AccessTokenLifetime;

        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        claims.AddRange(await claimsBuilder.BuildAsync(userId, cancellationToken));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.GetSecurityKey(), SecurityAlgorithms.HmacSha256),
        };

        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AuthTokens(accessToken, expires, refreshToken, refresh.ExpiresAt);
    }
}
