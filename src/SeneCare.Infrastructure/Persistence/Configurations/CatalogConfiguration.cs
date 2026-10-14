using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeneCare.Domain.Content;
using SeneCare.Domain.Locations;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Platform;

namespace SeneCare.Infrastructure.Persistence.Configurations;

internal sealed class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.ToTable("regions");
        builder.Property(p => p.Name).HasMaxLength(Place.NameMaxLength);
        builder.HasIndex(p => p.Slug).IsUnique();
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.Property(p => p.Name).HasMaxLength(Place.NameMaxLength);
        builder.HasOne<Region>().WithMany().HasForeignKey(p => p.RegionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.RegionId, p.Slug }).IsUnique();
    }
}

internal sealed class CommuneConfiguration : IEntityTypeConfiguration<Commune>
{
    public void Configure(EntityTypeBuilder<Commune> builder)
    {
        builder.ToTable("communes");
        builder.Property(p => p.Name).HasMaxLength(Place.NameMaxLength);
        builder.HasOne<Department>().WithMany().HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.DepartmentId, p.Slug }).IsUnique();
    }
}

internal sealed class NeighborhoodConfiguration : IEntityTypeConfiguration<Neighborhood>
{
    public void Configure(EntityTypeBuilder<Neighborhood> builder)
    {
        builder.ToTable("neighborhoods");
        builder.Property(p => p.Name).HasMaxLength(Place.NameMaxLength);
        builder.HasOne<Commune>().WithMany().HasForeignKey(p => p.CommuneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.CommuneId, p.Slug }).IsUnique();
        builder.Ignore(p => p.IsSuggested);
        // Customer additions to review.
        builder.HasIndex(p => p.SuggestedAt).HasFilter("suggested_at IS NOT NULL");
        // Fast « search a neighborhood » picker.
        builder.HasIndex(p => p.Name);
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.Property(p => p.Code).HasMaxLength(Plan.CodeMaxLength);
        builder.Property(p => p.Name).HasMaxLength(Plan.NameMaxLength);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.AnnualFreeMonths).HasDefaultValue(2);
        builder.Ignore(p => p.OffersAnnual);
        builder.Ignore(p => p.AnnualPrice);
        builder.PrimitiveCollection(p => p.IncludedDomains);

        builder.OwnsMany(p => p.SlaPolicies, b =>
        {
            b.ToTable("plan_sla_policies");
            b.WithOwner().HasForeignKey("plan_id");
            b.HasIndex("plan_id", nameof(PlanSlaPolicy.Urgency)).IsUnique();
        });
    }
}

internal sealed class NeighborhoodProgramConfiguration : IEntityTypeConfiguration<NeighborhoodProgram>
{
    public void Configure(EntityTypeBuilder<NeighborhoodProgram> builder)
    {
        builder.ToTable("neighborhood_programs");
        builder.HasOne<Neighborhood>().WithMany().HasForeignKey(p => p.NeighborhoodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.NeighborhoodId).IsUnique();
        builder.HasMany(p => p.Operations).WithOne().HasForeignKey("program_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Operations).AutoInclude();
    }
}

internal sealed class GreeningOperationConfiguration : IEntityTypeConfiguration<GreeningOperation>
{
    public void Configure(EntityTypeBuilder<GreeningOperation> builder)
    {
        builder.ToTable("greening_operations");
        builder.Property(o => o.Description).HasMaxLength(2000);
        builder.OwnsMany(o => o.Photos, OwnedMedia.Configure<GreeningOperation>("greening_operation_photos", "operation_id"));
    }
}

internal sealed class PreventiveAdviceConfiguration : IEntityTypeConfiguration<PreventiveAdvice>
{
    public void Configure(EntityTypeBuilder<PreventiveAdvice> builder)
    {
        builder.ToTable("preventive_advices");
        builder.Property(a => a.Title).HasMaxLength(PreventiveAdvice.TitleMaxLength);
        builder.Property(a => a.Body).HasMaxLength(PreventiveAdvice.BodyMaxLength);
        builder.PrimitiveCollection(a => a.Months);
        builder.HasIndex(a => a.IsPublished);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.Property(n => n.TemplateKey).HasMaxLength(100);
        builder.Property(n => n.PayloadJson).HasColumnType("jsonb").Metadata.SetMaxLength(null);
        builder.Property(n => n.LastError).HasMaxLength(1000);
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        builder.HasIndex(n => n.Status);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.Property(a => a.Details).HasMaxLength(4000);
        builder.HasIndex(a => a.At);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.ActorUserId);
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Value).HasMaxLength(2000);
    }
}
