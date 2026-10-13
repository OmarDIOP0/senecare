using System.Net;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Back-office management: customers, technicians, offers, manual payments, geography, settings.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class ManagementTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task Finance_records_a_bank_transfer_and_the_company_subscription_starts()
    {
        var (manager, accountId) = await CompanyAsync();
        await manager.SubmitAsync("/entreprise/offre", "/entreprise/offre", new Dictionary<string, string> { ["plan"] = "PREMIUM" });
        await manager.GetStringAsync(new Uri("/abonnement", UriKind.Relative)); // issues the first invoice

        Guid invoiceId;
        await using (var db = app.Database.CreateContext())
        {
            invoiceId = await db.Invoices.Where(i => i.AccountId == accountId).Select(i => i.Id).SingleAsync();
        }

        var finance = await StaffAsync(PlatformRole.Finance);
        var page = WebUtility.HtmlDecode(await finance.GetStringAsync(new Uri("/admin/paiements", UriKind.Relative)));
        Assert.Contains($"/admin/paiements/factures/{invoiceId}/encaisser", page, StringComparison.Ordinal);

        // A reference is required.
        await finance.SubmitAsync("/admin/paiements", $"/admin/paiements/factures/{invoiceId}/encaisser", new Dictionary<string, string> { ["channel"] = "BankTransfer" });
        await finance.SubmitAsync("/admin/paiements", $"/admin/paiements/factures/{invoiceId}/encaisser", new Dictionary<string, string>
        {
            ["channel"] = "BankTransfer",
            ["note"] = "Virement CBAO du 12/10",
        });

        await using var check = app.Database.CreateContext();
        Assert.Equal(InvoiceStatus.Paid, (await check.Invoices.SingleAsync(i => i.Id == invoiceId)).Status);
        var payment = await check.Payments.SingleAsync(p => p.InvoiceId == invoiceId);
        Assert.Equal(PaymentChannel.BankTransfer, payment.Channel);
        Assert.Equal(10_575_000, payment.Amount.Amount);
        var subscription = await check.Subscriptions.SingleAsync(s => s.AccountId == accountId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(await check.Diagnostics.AnyAsync(d => d.SubscriptionId == subscription.Id && d.Status == DiagnosticStatus.Planned));
        Assert.True(await check.AuditLogs.AnyAsync(a => a.Action == "payment.record_manual" && a.EntityId == payment.Id.ToString()));
    }

    [PostgresFact]
    public async Task A_new_price_applies_to_new_subscriptions_only()
    {
        var admin = await StaffAsync(PlatformRole.SuperAdmin);
        var (before, beforeProperty) = await HouseholdWithHomeAsync();
        Guid planId;
        await using (var db = app.Database.CreateContext())
        {
            planId = (await db.Plans.SingleAsync(p => p.Code == "HOME")).Id;
        }

        try
        {
            await PostFormAsync(admin, "/admin/offres", $"/admin/offres/{planId}", PlanFields(9_000));
            var (_, afterProperty) = await HouseholdWithHomeAsync();

            await using var db = app.Database.CreateContext();
            Assert.Equal(8_500, (await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(beforeProperty))).MonthlyPrice.Amount);
            Assert.Equal(9_000, (await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(afterProperty))).MonthlyPrice.Amount);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "plan.update" && a.EntityId == "HOME"));
        }
        finally
        {
            await PostFormAsync(admin, "/admin/offres", $"/admin/offres/{planId}", PlanFields(8_500));
        }

        // Prices are the super-admin's: a dispatcher is refused.
        var dispatcher = await StaffAsync(PlatformRole.Dispatcher);
        Assert.StartsWith("/acces-refuse", (await dispatcher.GetAsync(new Uri("/admin/offres", UriKind.Relative))).Location(), StringComparison.Ordinal);
        GC.KeepAlive(before);
    }

    [PostgresFact]
    public async Task Dispatcher_adds_a_technician_who_can_sign_in_and_can_be_deactivated()
    {
        var dispatcher = await StaffAsync(PlatformRole.Dispatcher);
        Guid communeId;
        await using (var db = app.Database.CreateContext())
        {
            communeId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.CommuneId).SingleAsync();
        }

        var phone = TestData.NewPhone();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("phone", phone.Formatted),
            new("fullName", "Cheikh Mbaye"),
            new("skills", "Electricity"),
            new("skills", "Plumbing"),
            new("communes", communeId.ToString()),
            new("days", "Monday"),
            new("days", "Saturday"),
            new("start", "08:00"),
            new("end", "18:00"),
        };
        await PostFormAsync(dispatcher, "/admin/techniciens", "/admin/techniciens", fields);

        Guid technicianId;
        await using (var db = app.Database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.PhoneNumber == phone);
            Assert.True(user.HasRole(PlatformRole.Technician));
            var technician = await db.Technicians.SingleAsync(t => t.UserId == user.Id);
            Assert.Equal(2, technician.Skills.Count);
            Assert.Equal(2, technician.WeeklyAvailability.Count);
            technicianId = technician.Id;
        }

        var technicianClient = app.CreateClient();
        await technicianClient.SignInAsync(app.Messages, phone);
        await technicianClient.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string> { ["FullName"] = "Cheikh Mbaye", ["AcceptsPrivacyPolicy"] = "true" });
        Assert.Equal(HttpStatusCode.OK, (await technicianClient.GetAsync(new Uri("/technicien", UriKind.Relative))).StatusCode);

        await dispatcher.SubmitAsync($"/admin/techniciens/{technicianId}", $"/admin/techniciens/{technicianId}/statut", new Dictionary<string, string> { ["active"] = "false" });
        await using var check = app.Database.CreateContext();
        Assert.False((await check.Technicians.SingleAsync(t => t.Id == technicianId)).IsActive);
    }

    [PostgresFact]
    public async Task Support_finds_a_customer_by_phone_and_reads_the_file()
    {
        var phone = TestData.NewPhone();
        var customer = app.CreateClient();
        await customer.SignInAsync(app.Messages, phone);
        await customer.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Khady Diagne",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        await AddPropertyAsync(customer, "Villa", "Maison de Ngor");

        var support = await StaffAsync(PlatformRole.Support);
        var results = WebUtility.HtmlDecode(await support.GetStringAsync(new Uri($"/admin/clients?q={Uri.EscapeDataString(phone.Formatted)}", UriKind.Relative)));
        Assert.Contains("Khady Diagne", results, StringComparison.Ordinal);
        var byName = WebUtility.HtmlDecode(await support.GetStringAsync(new Uri("/admin/clients?q=khady", UriKind.Relative)));
        Assert.Contains("Khady Diagne", byName, StringComparison.Ordinal);

        var link = System.Text.RegularExpressions.Regex.Match(results, "/admin/clients/([0-9a-f-]{36})").Value;
        var file = WebUtility.HtmlDecode(await support.GetStringAsync(new Uri(link, UriKind.Relative)));
        Assert.Contains("Maison de Ngor", file, StringComparison.Ordinal);

        Assert.StartsWith("/acces-refuse", (await support.GetAsync(new Uri("/admin/parametres", UriKind.Relative))).Location(), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Super_admin_maintains_neighborhoods_and_settings()
    {
        var admin = await StaffAsync(PlatformRole.SuperAdmin);
        Guid communeId;
        await using (var db = app.Database.CreateContext())
        {
            communeId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.CommuneId).SingleAsync();
        }

        var name = "Cité Test " + Guid.NewGuid().ToString("N")[..6];
        var page = $"/admin/referentiel/{communeId}";
        await admin.SubmitAsync(page, $"{page}/quartiers", new Dictionary<string, string> { ["name"] = name });
        await using (var db = app.Database.CreateContext())
        {
            var neighborhood = await db.Neighborhoods.SingleAsync(n => n.Name == name);
            Assert.True(await db.NeighborhoodPrograms.AnyAsync(p => p.NeighborhoodId == neighborhood.Id));
        }

        // Duplicate refused.
        await admin.SubmitAsync(page, $"{page}/quartiers", new Dictionary<string, string> { ["name"] = name });
        await using (var db = app.Database.CreateContext())
        {
            Assert.Equal(1, await db.Neighborhoods.CountAsync(n => n.Name == name));
        }

        await admin.SubmitAsync("/admin/parametres", "/admin/parametres", new Dictionary<string, string> { ["key"] = SettingKeys.WarrantyDays, ["value"] = "beaucoup" });
        await using (var db = app.Database.CreateContext())
        {
            Assert.NotEqual("beaucoup", (await db.AppSettings.SingleAsync(s => s.Key == SettingKeys.WarrantyDays)).Value);
        }

        await admin.SubmitAsync("/admin/parametres", "/admin/parametres", new Dictionary<string, string> { ["key"] = SettingKeys.WarrantyDays, ["value"] = "120" });
        try
        {
            await using var db = app.Database.CreateContext();
            Assert.Equal("120", (await db.AppSettings.SingleAsync(s => s.Key == SettingKeys.WarrantyDays)).Value);
        }
        finally
        {
            await admin.SubmitAsync("/admin/parametres", "/admin/parametres", new Dictionary<string, string> { ["key"] = SettingKeys.WarrantyDays, ["value"] = "90" });
        }
    }

    private static List<KeyValuePair<string, string>> PlanFields(long monthly) =>
    [
        new("name", "Maisons & Villas"),
        new("registrationFee", "10000"),
        new("monthlyPrice", monthly.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("priority", "1"),
        new("visits", string.Empty),
        new("criticalHours", "4"),
        new("highHours", "24"),
        new("normalHours", "72"),
        new("isActive", "true"),
        .. Enum.GetValues<SeneCare.Domain.Common.ServiceDomain>().Select(d => new KeyValuePair<string, string>("domains", d.ToString())),
    ];

    private async Task<HttpClient> StaffAsync(PlatformRole role)
    {
        var phone = TestData.NewPhone();
        await using (var db = app.Database.CreateContext())
        {
            var user = User.Register(phone, DateTimeOffset.UtcNow);
            user.GrantRole(role, DateTimeOffset.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, phone);
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string> { ["FullName"] = "Fatou Sarr", ["AcceptsPrivacyPolicy"] = "true" });
        return client;
    }

    private async Task<(HttpClient Client, Guid AccountId)> CompanyAsync()
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Ousmane Fall",
            ["Profile"] = "Business",
            ["CompanyName"] = "Thiès Industries",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        var site = await AddPropertyAsync(client, "Office", "Usine Thiès");
        await using var db = app.Database.CreateContext();
        return (client, await db.Properties.Where(p => p.Id == site).Select(p => p.AccountId).SingleAsync());
    }

    private async Task<(HttpClient Client, Guid PropertyId)> HouseholdWithHomeAsync()
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        return (client, await AddPropertyAsync(client, "Villa", "Villa des Almadies"));
    }

    private async Task<Guid> AddPropertyAsync(HttpClient client, string kind, string name)
    {
        Guid neighborhoodId;
        await using (var db = app.Database.CreateContext())
        {
            neighborhoodId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.Id).SingleAsync();
        }

        var response = await client.SubmitMultipartAsync("/logements/nouveau", "/logements/nouveau", new Dictionary<string, string>
        {
            ["Kind"] = kind,
            ["Name"] = name,
            ["Landmark"] = "Près du rond-point",
            ["NeighborhoodId"] = neighborhoodId.ToString(),
        }, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

    private static async Task PostFormAsync(HttpClient client, string page, string url, List<KeyValuePair<string, string>> fields)
    {
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        fields.Add(new("__RequestVerificationToken", WebUtility.HtmlDecode(token)));
        using var form = new FormUrlEncodedContent(fields);
        var response = await client.PostAsync(new Uri(url, UriKind.Relative), form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
