using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Locations;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Persistence.Converters;

namespace SeneCare.Infrastructure.Persistence;

public sealed class SeneCareDbContext(DbContextOptions<SeneCareDbContext> options) : DbContext(options)
{
    public const string ServiceRequestNumberSequence = "service_request_number_seq";
    public const string InvoiceNumberSequence = "invoice_number_seq";

    public DbSet<User> Users => Set<User>();

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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable text ("Critical", "Wave"…), robust to reordering.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();
        configurationBuilder.Properties<PhoneNumber>().HaveConversion<PhoneNumberConverter>().HaveMaxLength(16);
        configurationBuilder.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(ServiceRequestNumberSequence);
        modelBuilder.HasSequence<long>(InvoiceNumberSequence);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SeneCareDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(AggregateRoot).IsAssignableFrom(t.ClrType) && !t.IsOwned()))
        {
            modelBuilder.Entity(entityType.ClrType).Ignore(nameof(AggregateRoot.DomainEvents));
        }
    }
}
