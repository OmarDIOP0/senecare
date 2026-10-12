using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Infrastructure.Persistence.Seed;

namespace SeneCare.Infrastructure.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public class SeedTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task Seed_creates_reference_data()
    {
        await using var db = fixture.CreateContext();

        Assert.Equal(14, await db.Regions.CountAsync());
        Assert.Equal(46, await db.Departments.CountAsync());
        Assert.True(await db.Neighborhoods.AnyAsync(n => n.Name == "Almadies"));
        Assert.Equal(await db.Neighborhoods.CountAsync(), await db.NeighborhoodPrograms.CountAsync());

        var plans = await db.Plans.OrderBy(p => p.Priority).ToListAsync();
        Assert.Equal(["HOME", "ESSENTIEL", "PROFESSIONNEL", "PREMIUM"], plans.Select(p => p.Code));
        Assert.Equal(8_500, plans[0].MonthlyPrice.Amount);
        Assert.Equal(3_500_000, plans[3].MonthlyPrice.Amount);
        Assert.Equal(TimeSpan.FromHours(4), plans[3].GetMaxDelay(Urgency.Critical));
    }

    [PostgresFact]
    public async Task Seed_is_idempotent()
    {
        await using var db = fixture.CreateContext();
        var before = await db.Neighborhoods.CountAsync();

        await new DatabaseSeeder(db, TimeProvider.System, NullLogger<DatabaseSeeder>.Instance).SeedAsync();

        Assert.Equal(before, await db.Neighborhoods.CountAsync());
        Assert.Equal(4, await db.Plans.CountAsync());
    }
}

[Collection(PostgresTestGroup.Name)]
public class AggregatePersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task Phone_number_is_unique()
    {
        var phone = UniquePhone();
        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(User.Register(phone, Now));
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.CreateContext();
        db2.Users.Add(User.Register(phone, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task Service_request_round_trips_with_history_appointment_and_rating()
    {
        var (user, account, property) = await CreateHouseholdAsync();
        var dispatcher = Guid.CreateVersion7();
        Guid requestId;

        await using (var db = fixture.CreateContext())
        {
            var number = "SC-TEST-" + Guid.NewGuid().ToString("N")[..8];
            var request = ServiceRequest.Create(
                number, account.Id, property.Id, user.Id, ServiceDomain.Waterproofing,
                "Infiltration au plafond du salon", Urgency.High, TimeSpan.FromHours(24), false,
                [new TimeSlot(Now.AddDays(1), Now.AddDays(1).AddHours(3))], Now);
            request.StartReview(dispatcher, Now);
            var slot = new TimeSlot(Now.AddHours(20), Now.AddHours(22));
            request.ProposeAppointment([slot], dispatcher, Now);
            request.ConfirmAppointment(slot, user.Id, Now);
            request.MarkOnTheWay(dispatcher, Now.AddHours(19));
            request.StartWork(dispatcher, Now.AddHours(20));
            request.Complete(dispatcher, Now.AddHours(22));
            request.ValidateByCustomer(user.Id, new Rating(4, "Rapide", Now.AddHours(23)), Now.AddHours(23));
            db.ServiceRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        await using var read = fixture.CreateContext();
        var loaded = await read.ServiceRequests.SingleAsync(r => r.Id == requestId);

        Assert.Equal(RequestStatus.ValidatedByCustomer, loaded.Status);
        Assert.Equal(8, loaded.History.Count);
        Assert.Equal(Now.AddHours(24), loaded.SlaDeadline);
        Assert.False(loaded.IsSlaBreached(Now.AddDays(10)));
        Assert.Equal(4, loaded.Rating!.Score);
        var appointment = Assert.Single(loaded.Appointments);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(Now.AddHours(20), appointment.ChosenSlot!.Start);
        Assert.Single(loaded.PreferredSlots);
    }

    [PostgresFact]
    public async Task Business_subscription_invoice_and_diagnostic_round_trip()
    {
        var manager = User.Register(UniquePhone(), Now);
        var account = CustomerAccount.CreateBusiness("Teranga Services SARL", null, manager.Id, Now);
        Guid subscriptionId, invoiceId, diagnosticId;

        await using (var db = fixture.CreateContext())
        {
            var neighborhoodId = await db.Neighborhoods.Select(n => n.Id).FirstAsync();
            var site = new Property(account.Id, PropertyKind.Office, "Agence Plateau", new Address(neighborhoodId, null, "Face à la BICIS", null), Now);
            var plan = await db.Plans.SingleAsync(p => p.Code == "ESSENTIEL");
            var subscription = Subscription.Create(account, plan, [site.Id], Now);
            subscription.Activate(new DateOnly(2026, 1, 1), Now);

            var invoice = new Invoice("FAC-TEST-" + Guid.NewGuid().ToString("N")[..8], account.Id, subscription.Id, subscription.CurrentPeriod, new DateOnly(2026, 1, 1));
            invoice.AddLine(new InvoiceLine(InvoiceLineKind.RegistrationFee, "Inscription", 1, plan.RegistrationFee));
            invoice.AddLine(new InvoiceLine(InvoiceLineKind.SubscriptionPeriod, "T1 2026", 3, plan.MonthlyPrice));
            invoice.Issue(Now);

            var diagnostic = new Diagnostic(account.Id, subscription.Id, subscription.CurrentPeriod!, Now.AddDays(80));
            diagnostic.Start(Guid.CreateVersion7(), Now);
            diagnostic.RecordControlPoint(site.Id, ServiceDomain.Electricity, "Tableau général", ControlPointResult.Anomaly, "Disjoncteur chaud");
            diagnostic.Submit("Remplacer le disjoncteur", null, Now);
            diagnostic.Validate(Guid.CreateVersion7(), Now);

            db.AddRange(manager, account, site, subscription, invoice, diagnostic);
            await db.SaveChangesAsync();
            (subscriptionId, invoiceId, diagnosticId) = (subscription.Id, invoice.Id, diagnostic.Id);
        }

        await using var read = fixture.CreateContext();
        var loadedSubscription = await read.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        var loadedInvoice = await read.Invoices.SingleAsync(i => i.Id == invoiceId);
        var loadedDiagnostic = await read.Diagnostics.SingleAsync(d => d.Id == diagnosticId);
        var loadedAccount = await read.CustomerAccounts.SingleAsync(a => a.Id == loadedSubscription.AccountId);

        Assert.Equal(4_575_000, loadedInvoice.Total.Amount);
        Assert.Equal(loadedSubscription.CurrentPeriod, loadedInvoice.Period);
        Assert.Equal(AccountRole.Manager, Assert.Single(loadedAccount.Members).Role);
        Assert.Single(loadedSubscription.CoveredPropertyIds);
        Assert.Equal(1, loadedDiagnostic.AnomalyCount);
        Assert.Equal(new DateOnly(2026, 4, 1), loadedSubscription.PrepareRenewal(loadedDiagnostic).Start);
    }

    private static PhoneNumber UniquePhone() =>
        PhoneNumber.Parse("77" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private async Task<(User User, CustomerAccount Account, Property Property)> CreateHouseholdAsync()
    {
        await using var db = fixture.CreateContext();
        var user = User.Register(UniquePhone(), Now);
        user.UpdateProfile("Awa Ndiaye", null, Language.French);
        user.GiveConsent(ConsentType.PersonalData, "2026-10", Now);
        var account = CustomerAccount.CreateHousehold("Famille Ndiaye", user.Id, Now);
        var neighborhoodId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.Id).SingleAsync();
        var property = new Property(
            account.Id, PropertyKind.Villa, "Villa des Almadies",
            new Address(neighborhoodId, new GeoPoint(14.7456, -17.5130), "Derrière la mosquée, portail vert", null), Now);
        db.AddRange(user, account, property);
        await db.SaveChangesAsync();
        return (user, account, property);
    }
}
