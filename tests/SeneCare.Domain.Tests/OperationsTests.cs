using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Technicians;
using static SeneCare.Domain.Tests.TestData;

namespace SeneCare.Domain.Tests;

public class DiagnosticTests
{
    [Fact]
    public void Cannot_submit_without_control_points()
    {
        var diagnostic = Started();

        Assert.Throws<DomainException>(() => diagnostic.Submit(null, null, Now));
    }

    [Fact]
    public void Rejected_report_goes_back_to_the_technician()
    {
        var diagnostic = Started();
        diagnostic.RecordControlPoint(null, ServiceDomain.Waterproofing, "Terrasse", ControlPointResult.Anomaly, "Fissures");
        diagnostic.Submit(null, null, Now);

        diagnostic.Reject();

        Assert.Equal(DiagnosticStatus.InProgress, diagnostic.Status);
    }

    [Fact]
    public void Only_anomalies_can_be_converted_once_into_a_request()
    {
        var diagnostic = Started();
        var ok = diagnostic.RecordControlPoint(null, ServiceDomain.Electricity, "Disjoncteur", ControlPointResult.Ok, null);
        var anomaly = diagnostic.RecordControlPoint(null, ServiceDomain.Waterproofing, "Terrasse", ControlPointResult.Anomaly, "Fissures");

        Assert.Throws<DomainException>(() => diagnostic.MarkAnomalyConverted(ok.Id, Guid.CreateVersion7()));
        diagnostic.MarkAnomalyConverted(anomaly.Id, Guid.CreateVersion7());
        Assert.Throws<DomainException>(() => diagnostic.MarkAnomalyConverted(anomaly.Id, Guid.CreateVersion7()));
        Assert.Equal(1, diagnostic.AnomalyCount);
    }

    private static Diagnostic Started()
    {
        var diagnostic = new Diagnostic(Guid.CreateVersion7(), Guid.CreateVersion7(), DateRange.OfMonths(new DateOnly(2026, 1, 1), 3), Now);
        diagnostic.Start(Guid.CreateVersion7(), Now);
        return diagnostic;
    }
}

public class InterventionTests
{
    [Fact]
    public void Completing_requires_an_after_photo_and_a_signature_or_reason()
    {
        var intervention = Started();

        Assert.Throws<DomainException>(() => intervention.Complete("Joint remplacé", null, 90, Now));
        intervention.AddPhoto(Guid.CreateVersion7(), MediaKind.PhotoAfter);
        Assert.Throws<DomainException>(() => intervention.Complete("Joint remplacé", null, 90, Now));

        intervention.Complete("Joint remplacé", "Client absent, voisin présent", 90, Now);

        Assert.True(intervention.IsCompleted);
    }

    [Fact]
    public void Warranty_covers_same_domain_and_property_until_its_end()
    {
        var intervention = Started();
        intervention.AddPhoto(Guid.CreateVersion7(), MediaKind.PhotoAfter);
        intervention.SignByCustomer(Guid.CreateVersion7(), "Awa Ndiaye");
        intervention.Complete("Joint remplacé", null, 90, Now);

        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        Assert.Equal(today.AddDays(90), intervention.WarrantyEndsOn);
        Assert.True(intervention.CoversUnderWarranty(intervention.PropertyId, ServiceDomain.Plumbing, today.AddDays(30)));
        Assert.False(intervention.CoversUnderWarranty(intervention.PropertyId, ServiceDomain.Electricity, today.AddDays(30)));
        Assert.False(intervention.CoversUnderWarranty(intervention.PropertyId, ServiceDomain.Plumbing, today.AddDays(91)));
    }

    [Fact]
    public void Closed_intervention_cannot_be_changed()
    {
        var intervention = Started();
        intervention.AddPhoto(Guid.CreateVersion7(), MediaKind.PhotoAfter);
        intervention.Complete("OK", "Client absent", 90, Now);

        Assert.Throws<DomainException>(() => intervention.AddPart(new PartUsed("Joint", 1, "pièce", null)));
    }

    private static Intervention Started()
    {
        var intervention = new Intervention(Guid.CreateVersion7(), Guid.CreateVersion7(), ServiceDomain.Plumbing, Guid.CreateVersion7(), ["Couper l'eau", "Vérifier l'étanchéité"]);
        intervention.Start(Now);
        return intervention;
    }
}

public class TechnicianTests
{
    private static readonly Guid Yoff = Guid.CreateVersion7();

    [Fact]
    public void Handles_only_its_skills_and_zones()
    {
        var technician = new Technician(Guid.CreateVersion7(), [ServiceDomain.Plumbing], [Yoff]);

        Assert.True(technician.CanHandle(ServiceDomain.Plumbing, Yoff));
        Assert.False(technician.CanHandle(ServiceDomain.Electricity, Yoff));
        Assert.False(technician.CanHandle(ServiceDomain.Plumbing, Guid.CreateVersion7()));
    }

    [Fact]
    public void Availability_respects_working_hours_and_unavailabilities()
    {
        var technician = new Technician(Guid.CreateVersion7(), [ServiceDomain.Plumbing], [Yoff]);
        technician.SetWeeklyAvailability([new WeeklyAvailability(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(18, 0))]);
        var mondayMorning = new TimeSlot(Now.AddHours(1), Now.AddHours(3)); // 2026-10-12 is a Monday

        Assert.True(technician.IsAvailable(mondayMorning));
        Assert.False(technician.IsAvailable(new TimeSlot(Now.AddHours(10), Now.AddHours(12)))); // ends at 20:00

        technician.AddUnavailability(new TimeSlot(Now.AddHours(2), Now.AddHours(4)), "Formation");
        Assert.False(technician.IsAvailable(mondayMorning));
    }

    [Fact]
    public void Average_rating_is_computed()
    {
        var technician = new Technician(Guid.CreateVersion7(), [ServiceDomain.Plumbing], [Yoff]);

        technician.AddRating(5);
        technician.AddRating(4);

        Assert.Equal(4.5, technician.AverageRating);
    }
}

public class NeighborhoodProgramTests
{
    [Fact]
    public void Activates_when_threshold_is_reached()
    {
        var program = new NeighborhoodProgram(Guid.CreateVersion7(), NeighborhoodProgram.DefaultThreshold);

        program.UpdateMemberCount(347, Now);
        Assert.Equal(NeighborhoodProgramStatus.Gathering, program.Status);
        Assert.Equal(0.694, program.Progress, 3);

        program.UpdateMemberCount(500, Now);
        Assert.Equal(NeighborhoodProgramStatus.Active, program.Status);
        Assert.Single(program.DomainEvents.OfType<NeighborhoodProgramActivated>());
    }

    [Fact]
    public void Lowering_the_threshold_can_activate_the_program()
    {
        var program = new NeighborhoodProgram(Guid.CreateVersion7(), 500);
        program.UpdateMemberCount(320, Now);

        program.ChangeThreshold(300, Now);

        Assert.Equal(NeighborhoodProgramStatus.Active, program.Status);
    }

    [Fact]
    public void Operations_only_in_active_neighborhoods()
    {
        var program = new NeighborhoodProgram(Guid.CreateVersion7(), 500);

        Assert.Throws<DomainException>(() => program.RecordOperation(new DateOnly(2026, 11, 1), 40, "Plantation de neems"));

        program.UpdateMemberCount(510, Now);
        program.RecordOperation(new DateOnly(2026, 11, 1), 40, "Plantation de neems");
        Assert.Equal(40, program.TotalTreesPlanted);
    }
}

public class AccountTests
{
    [Fact]
    public void Business_account_keeps_at_least_one_manager()
    {
        var account = Business();

        Assert.Throws<DomainException>(() => account.RemoveMember(UserId));
        Assert.Throws<DomainException>(() => account.ChangeMemberRole(UserId, AccountRole.Reporter, []));
    }

    [Fact]
    public void Reporter_only_accesses_their_sites()
    {
        var account = Business();
        var reporter = Guid.CreateVersion7();
        var site = Guid.CreateVersion7();

        var member = account.AddMember(reporter, AccountRole.Reporter, [site], Now);

        Assert.True(member.CanAccess(site));
        Assert.False(member.CanAccess(Guid.CreateVersion7()));
    }

    [Fact]
    public void Household_has_no_business_roles() =>
        Assert.Throws<DomainException>(() => Household().AddMember(Guid.CreateVersion7(), AccountRole.Reporter, [], Now));

    [Fact]
    public void Address_needs_gps_or_landmark() =>
        Assert.Throws<DomainException>(() => new Address(Guid.CreateVersion7(), null, " ", null));
}

public class PlanTests
{
    [Fact]
    public void Default_sla_matches_the_specification()
    {
        var plan = HomePlan();

        Assert.Equal(TimeSpan.FromHours(4), plan.GetMaxDelay(Urgency.Critical));
        Assert.Equal(TimeSpan.FromHours(24), plan.GetMaxDelay(Urgency.High));
        Assert.Equal(TimeSpan.FromHours(72), plan.GetMaxDelay(Urgency.Normal));
        Assert.Null(plan.GetMaxDelay(Urgency.Planned));
    }

    [Fact]
    public void Sla_can_be_tightened_per_plan()
    {
        var premium = new Plan("PREMIUM", "Premium", AccountKind.Business, Money.FromFcfa(75_000), Money.FromFcfa(3_500_000), 3, true);

        premium.SetSla(Urgency.Critical, TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.FromHours(2), premium.GetMaxDelay(Urgency.Critical));
        Assert.Equal(10_500_000, premium.PeriodPrice.Amount);
    }
}
