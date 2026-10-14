using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Application.Business;
using SeneCare.Domain.Common;
using SeneCare.Domain.Content;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Locations;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;
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

    DbSet<Region> Regions { get; }

    DbSet<Department> Departments { get; }

    DbSet<Commune> Communes { get; }

    DbSet<Neighborhood> Neighborhoods { get; }

    DbSet<Subscription> Subscriptions { get; }

    DbSet<Invoice> Invoices { get; }

    DbSet<Payment> Payments { get; }

    DbSet<ServiceRequest> ServiceRequests { get; }

    DbSet<Intervention> Interventions { get; }

    DbSet<Technician> Technicians { get; }

    DbSet<Diagnostic> Diagnostics { get; }

    DbSet<MediaFile> MediaFiles { get; }

    DbSet<NeighborhoodProgram> NeighborhoodPrograms { get; }

    DbSet<AppSetting> AppSettings { get; }

    DbSet<PreventiveAdvice> PreventiveAdvices { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>PDF documents (QuestPDF in Infrastructure). Inputs are plain, already-authorized data.</summary>
public interface IPdfRenderer
{
    byte[] FieldReport(FieldReport report);

    byte[] RequestsReport(RequestsReport report);

    byte[] Invoice(Payments.InvoicePdf invoice);
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

    /// <summary>Erasure: the sign-in record follows the placeholder number and every session is invalidated.</summary>
    Task ForgetAsync(User user, CancellationToken cancellationToken = default);
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

/// <summary>Human-readable document numbers from database sequences (« SC-2026-000123 »).</summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextServiceRequestNumberAsync(CancellationToken cancellationToken = default);

    Task<string> NextInvoiceNumberAsync(CancellationToken cancellationToken = default);
}

/// <summary>Binary storage for photos, voice notes, signatures and PDFs (local disk now, object storage later).</summary>
public interface IFileStorage
{
    /// <summary>Stores the content and returns its storage key.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes the file (no error when it is already gone).</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
