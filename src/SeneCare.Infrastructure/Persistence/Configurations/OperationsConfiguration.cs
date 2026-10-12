using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Persistence.Configurations;

internal static class OwnedValues
{
    public static void DateRange<TOwner>(OwnedNavigationBuilder<TOwner, DateRange> b, string prefix)
        where TOwner : class
    {
        b.Property(r => r.Start).HasColumnName(prefix + "_start");
        b.Property(r => r.End).HasColumnName(prefix + "_end");
    }

    public static void TimeSlot<TOwner>(OwnedNavigationBuilder<TOwner, TimeSlot> b, string prefix)
        where TOwner : class
    {
        b.Property(s => s.Start).HasColumnName(prefix + "_start");
        b.Property(s => s.End).HasColumnName(prefix + "_end");
        b.Ignore(s => s.Duration);
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasOne<CustomerAccount>().WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.OwnsOne(s => s.CurrentPeriod, b => OwnedValues.DateRange(b, "current_period"));
        builder.PrimitiveCollection(s => s.CoveredPropertyIds);
        builder.HasIndex(s => s.AccountId);
        builder.HasIndex(s => s.Status);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.Property(i => i.Number).HasMaxLength(32);
        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasOne<CustomerAccount>().WithMany().HasForeignKey(i => i.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Subscription>().WithMany().HasForeignKey(i => i.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.Status, i.DueDate });
        builder.OwnsOne(i => i.Period, b => OwnedValues.DateRange(b, "period"));
        builder.PrimitiveCollection(i => i.AppliedPaymentIds);

        builder.OwnsMany(i => i.Lines, b =>
        {
            b.ToTable("invoice_lines");
            b.WithOwner().HasForeignKey("invoice_id");
            b.Property(l => l.Description).HasMaxLength(InvoiceLine.DescriptionMaxLength);
            b.Ignore(l => l.Total);
        });
        builder.Navigation(i => i.Lines).AutoInclude();
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasOne<Invoice>().WithMany().HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.ProviderReference).HasMaxLength(Payment.ReferenceMaxLength);
        // Webhooks are idempotent: one provider transaction = one payment.
        builder.HasIndex(p => new { p.Channel, p.ProviderReference }).IsUnique().HasFilter("provider_reference IS NOT NULL");
        builder.HasIndex(p => p.InvoiceId);
    }
}

internal sealed class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("service_requests");
        builder.Property(r => r.Number).HasMaxLength(32);
        builder.HasIndex(r => r.Number).IsUnique();
        builder.Property(r => r.ClientReference).HasMaxLength(64);
        // Offline replays: one request per phone-generated reference.
        builder.HasIndex(r => new { r.AccountId, r.ClientReference }).IsUnique().HasFilter("client_reference IS NOT NULL");
        builder.Property(r => r.Description).HasMaxLength(ServiceRequest.DescriptionMaxLength);
        builder.Property(r => r.DisputeReason).HasMaxLength(Rating.CommentMaxLength);
        builder.HasOne<CustomerAccount>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Property>().WithMany().HasForeignKey(r => r.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(r => r.CurrentAppointment);
        builder.Ignore(r => r.IsUnderWarranty);

        // Dispatch queue and customer lists.
        builder.HasIndex(r => new { r.Status, r.Urgency, r.SlaDeadline });
        builder.HasIndex(r => new { r.PropertyId, r.CreatedAt });
        builder.HasIndex(r => new { r.AccountId, r.CreatedAt });

        builder.OwnsOne(r => r.Rating, b =>
        {
            b.Property(x => x.Score).HasColumnName("rating_score");
            b.Property(x => x.Comment).HasColumnName("rating_comment").HasMaxLength(Rating.CommentMaxLength);
            b.Property(x => x.GivenAt).HasColumnName("rating_given_at");
        });

        builder.OwnsMany(r => r.History, b =>
        {
            b.ToTable("service_request_history");
            b.WithOwner().HasForeignKey("service_request_id");
            b.Property(h => h.Comment).HasMaxLength(1000);
        });

        builder.OwnsMany(r => r.PreferredSlots, b =>
        {
            b.ToTable("service_request_preferred_slots");
            b.WithOwner().HasForeignKey("service_request_id");
            b.Ignore(s => s.Duration);
        });

        builder.OwnsMany(r => r.Attachments, OwnedMedia.Configure<ServiceRequest>("service_request_attachments", "service_request_id"));

        builder.HasMany(r => r.Appointments).WithOne().HasForeignKey("service_request_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Appointments).AutoInclude();
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.OwnsOne(a => a.ChosenSlot, b => OwnedValues.TimeSlot(b, "chosen"));
        builder.OwnsMany(a => a.ProposedSlots, b =>
        {
            b.ToTable("appointment_proposed_slots");
            b.WithOwner().HasForeignKey("appointment_id");
            b.Ignore(s => s.Duration);
        });
    }
}

internal sealed class InterventionConfiguration : IEntityTypeConfiguration<Intervention>
{
    public void Configure(EntityTypeBuilder<Intervention> builder)
    {
        builder.ToTable("interventions");
        builder.HasOne<ServiceRequest>().WithMany().HasForeignKey(i => i.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.RequestId);
        builder.HasIndex(i => new { i.PropertyId, i.Domain, i.WarrantyEndsOn });
        builder.HasIndex(i => i.LeadTechnicianId);
        builder.Property(i => i.Report).HasMaxLength(Intervention.ReportMaxLength);
        builder.PrimitiveCollection(i => i.TeamTechnicianIds);
        builder.Ignore(i => i.IsCompleted);
        builder.Ignore(i => i.Duration);

        builder.HasMany(i => i.Checklist).WithOne().HasForeignKey("intervention_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Checklist).AutoInclude();
        builder.OwnsMany(i => i.Photos, OwnedMedia.Configure<Intervention>("intervention_photos", "intervention_id"));
        builder.OwnsMany(i => i.PartsUsed, b =>
        {
            b.ToTable("intervention_parts");
            b.WithOwner().HasForeignKey("intervention_id");
            b.Property(p => p.Quantity).HasPrecision(10, 2);
        });
    }
}

internal sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder) => builder.ToTable("intervention_checklist_items");
}

internal sealed class DiagnosticConfiguration : IEntityTypeConfiguration<Diagnostic>
{
    public void Configure(EntityTypeBuilder<Diagnostic> builder)
    {
        builder.ToTable("diagnostics");
        builder.HasOne<Subscription>().WithMany().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.SubscriptionId, d.Status });
        builder.HasIndex(d => d.ScheduledFor);
        builder.Property(d => d.Recommendations).HasMaxLength(Diagnostic.RecommendationsMaxLength);
        builder.OwnsOne(d => d.PeriodCovered, b => OwnedValues.DateRange(b, "period"));
        builder.Navigation(d => d.PeriodCovered).IsRequired();
        builder.Ignore(d => d.AnomalyCount);

        builder.HasMany(d => d.ControlPoints).WithOne().HasForeignKey("diagnostic_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.ControlPoints).AutoInclude();
    }
}

internal sealed class ControlPointConfiguration : IEntityTypeConfiguration<ControlPoint>
{
    public void Configure(EntityTypeBuilder<ControlPoint> builder)
    {
        builder.ToTable("diagnostic_control_points");
        builder.Property(c => c.Label).HasMaxLength(ControlPoint.LabelMaxLength);
        builder.Property(c => c.Comment).HasMaxLength(ControlPoint.CommentMaxLength);
        builder.OwnsMany(c => c.Photos, OwnedMedia.Configure<ControlPoint>("diagnostic_control_point_photos", "control_point_id"));
    }
}

internal sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.ToTable("technicians");
        builder.HasOne<User>().WithOne().HasForeignKey<Technician>(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.PrimitiveCollection(t => t.Skills);
        builder.PrimitiveCollection(t => t.CommuneIds);
        builder.Ignore(t => t.AverageRating);

        builder.OwnsMany(t => t.WeeklyAvailability, b =>
        {
            b.ToTable("technician_weekly_availability");
            b.WithOwner().HasForeignKey("technician_id");
        });

        builder.OwnsMany(t => t.Unavailabilities, b =>
        {
            b.ToTable("technician_unavailabilities");
            b.WithOwner().HasForeignKey("technician_id");
            b.Property(u => u.Reason).HasMaxLength(200);
            b.OwnsOne(u => u.Slot, s => OwnedValues.TimeSlot(s, "slot"));
            b.Navigation(u => u.Slot).IsRequired();
        });
    }
}
