using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
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
using SeneCare.Infrastructure.Identity;
using SeneCare.Infrastructure.Persistence.Converters;

namespace SeneCare.Infrastructure.Persistence;

public sealed class SeneCareDbContext(DbContextOptions<SeneCareDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options), IApplicationDbContext
{
    public const string ServiceRequestNumberSequence = "service_request_number_seq";
    public const string InvoiceNumberSequence = "invoice_number_seq";

    // « new »: the domain users hide Identity's DbSet<ApplicationUser> Users (still reachable as Set<ApplicationUser>()).
    public new DbSet<User> Users => Set<User>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<AccountInvitation> AccountInvitations => Set<AccountInvitation>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();

    public DbSet<Property> Properties => Set<Property>();

    public DbSet<Region> Regions => Set<Region>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Commune> Communes => Set<Commune>();

    public DbSet<Neighborhood> Neighborhoods => Set<Neighborhood>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();

    public DbSet<Intervention> Interventions => Set<Intervention>();

    public DbSet<Diagnostic> Diagnostics => Set<Diagnostic>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<NeighborhoodProgram> NeighborhoodPrograms => Set<NeighborhoodProgram>();

    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<PreventiveAdvice> PreventiveAdvices => Set<PreventiveAdvice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable text ("Critical", "Wave"…), robust to reordering.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();
        configurationBuilder.Properties<PhoneNumber>().HaveConversion<PhoneNumberConverter>().HaveMaxLength(16);
        configurationBuilder.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasSequence<long>(ServiceRequestNumberSequence);
        builder.HasSequence<long>(InvoiceNumberSequence);

        builder.ApplyConfigurationsFromAssembly(typeof(SeneCareDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType) && !t.IsOwned()))
        {
            // Ids are created by the domain (GUID v7): a set key does not mean « already in the database »,
            // so children added to a loaded aggregate (new member, appointment…) are inserted, not updated.
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();

            if (typeof(AggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType).Ignore(nameof(AggregateRoot.DomainEvents));
            }
        }
    }
}
