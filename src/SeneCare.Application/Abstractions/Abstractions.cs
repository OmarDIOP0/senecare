using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Abstractions;

/// <summary>Data access for use cases. Implemented by the EF Core context in Infrastructure.</summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }

    DbSet<OtpChallenge> OtpChallenges { get; }

    DbSet<CustomerAccount> CustomerAccounts { get; }

    DbSet<AccountInvitation> AccountInvitations { get; }

    DbSet<Property> Properties { get; }

    DbSet<Plan> Plans { get; }

    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sends a text message by SMS. Dev: console; prod: Orange SMS API or an aggregator.</summary>
public interface ISmsSender
{
    Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken = default);
}

/// <summary>Sends a WhatsApp message (optional channel; falls back to SMS on failure).</summary>
public interface IWhatsAppSender
{
    Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken = default);
}

/// <summary>Keyed hashing of short secrets (OTP codes, invitation tokens). Never store them in clear.</summary>
public interface ISecretHasher
{
    string Hash(string secret);

    /// <summary>Constant-time comparison.</summary>
    bool Matches(string secret, string hash);
}

/// <summary>Creates the authentication (ASP.NET Identity) record sharing the domain user's Id.</summary>
public interface IIdentityService
{
    Task EnsureIdentityUserAsync(User user, CancellationToken cancellationToken = default);
}

public sealed record AuthTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>JWT access tokens + rotating refresh tokens for the REST API (future mobile app).</summary>
public interface IAuthTokenService
{
    Task<AuthTokens> IssueAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Rotates the refresh token. Null when invalid, expired or reused (reuse revokes the whole family).</summary>
    Task<AuthTokens?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}
