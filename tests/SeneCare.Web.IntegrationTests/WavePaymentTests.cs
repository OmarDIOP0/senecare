using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>
/// Wave link payment: the customer pays through the merchant link / QR code and declares it,
/// Finance verifies against the Wave history, then the subscription is activated.
/// </summary>
[Collection(WebAppTestGroup.Name)]
public sealed partial class WavePaymentTests(WebAppFixture app)
{
    private const string WaveLink = "https://pay.wave.com/m/M_sn_test/c/sn/?amount={montant}";

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task Customer_pays_with_wave_and_finance_activates_the_subscription()
    {
        var finance = await StaffAsync(PlatformRole.Finance);
        Assert.Equal(HttpStatusCode.Redirect, (await finance.SubmitAsync(
            "/admin/paiements", "/admin/paiements/lien-wave", new Dictionary<string, string> { ["link"] = WaveLink })).StatusCode);

        var customer = await HouseholdAsync();
        var propertyId = await AddHomeAsync(customer);

        var page = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
        Assert.Contains("18 500 FCFA", page.Replace(' ', ' ').Replace(' ', ' '), StringComparison.Ordinal);
        Assert.Contains("https://pay.wave.com/m/M_sn_test/c/sn/?amount=18500", page, StringComparison.Ordinal);
        Assert.Contains("<svg", page, StringComparison.Ordinal);
        var invoiceId = InvoiceAction().Match(page).Groups[1].Value;
        Assert.NotEmpty(invoiceId);

        var declared = await customer.SubmitAsync(
            "/abonnement", $"/abonnement/factures/{invoiceId}/paiement", new Dictionary<string, string> { ["reference"] = "T_WAVE_42" });
        Assert.Equal(HttpStatusCode.Redirect, declared.StatusCode);
        page = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
        Assert.Contains("L'équipe SENECARE le vérifie", page, StringComparison.Ordinal);

        // Declaring twice is refused: one declaration at a time (the form is gone, so replay it from another page).
        await customer.SubmitAsync("/logements/nouveau",$"/abonnement/factures/{invoiceId}/paiement", new Dictionary<string, string>());
        await using (var db = app.Database.CreateContext())
        {
            Assert.Equal(1, await db.Payments.CountAsync(p => p.InvoiceId == Guid.Parse(invoiceId)));
        }

        var admin = WebUtility.HtmlDecode(await finance.GetStringAsync(new Uri("/admin/paiements", UriKind.Relative)));
        Assert.Contains("T_WAVE_42", admin, StringComparison.Ordinal);
        Guid paymentId;
        await using (var db = app.Database.CreateContext())
        {
            paymentId = await db.Payments.Where(p => p.InvoiceId == Guid.Parse(invoiceId)).Select(p => p.Id).SingleAsync();
        }

        Assert.Equal(HttpStatusCode.Redirect, (await finance.SubmitAsync(
            "/admin/paiements", $"/admin/paiements/{paymentId}/valider", new Dictionary<string, string>())).StatusCode);

        await using (var db = app.Database.CreateContext())
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(propertyId));
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            Assert.Equal(InvoiceStatus.Paid, (await db.Invoices.SingleAsync(i => i.Id == Guid.Parse(invoiceId))).Status);
            Assert.Equal(PaymentStatus.Succeeded, (await db.Payments.SingleAsync(p => p.Id == paymentId)).Status);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "payment.verify" && a.EntityId == paymentId.ToString()));
        }

        page = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/signaler", UriKind.Relative)));
        Assert.DoesNotContain("Activez votre abonnement", page, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Rejected_payment_shows_the_reason_and_can_be_declared_again()
    {
        await SetWaveLinkAsync(WaveLink);
        var finance = await StaffAsync(PlatformRole.SuperAdmin);
        var customer = await HouseholdAsync();
        await AddHomeAsync(customer);

        var page = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
        var invoiceId = InvoiceAction().Match(page).Groups[1].Value;
        await customer.SubmitAsync("/abonnement", $"/abonnement/factures/{invoiceId}/paiement", new Dictionary<string, string>());

        Guid paymentId;
        await using (var db = app.Database.CreateContext())
        {
            paymentId = await db.Payments.Where(p => p.InvoiceId == Guid.Parse(invoiceId)).Select(p => p.Id).SingleAsync();
        }

        // A reason is mandatory.
        await finance.SubmitAsync("/admin/paiements", $"/admin/paiements/{paymentId}/refuser", new Dictionary<string, string> { ["reason"] = " " });
        await using (var db = app.Database.CreateContext())
        {
            Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(p => p.Id == paymentId)).Status);
        }

        await finance.SubmitAsync("/admin/paiements", $"/admin/paiements/{paymentId}/refuser", new Dictionary<string, string> { ["reason"] = "Aucun paiement reçu de ce numéro" });

        page = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
        Assert.Contains("Aucun paiement reçu de ce numéro", page, StringComparison.Ordinal);
        Assert.Contains("J'ai payé", page, StringComparison.Ordinal);

        await using (var db = app.Database.CreateContext())
        {
            Assert.Equal(PaymentStatus.Failed, (await db.Payments.SingleAsync(p => p.Id == paymentId)).Status);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "payment.reject" && a.EntityId == paymentId.ToString()));
        }
    }

    [PostgresFact]
    public async Task Only_finance_reaches_payments_and_only_wave_links_are_accepted()
    {
        var customer = await HouseholdAsync();
        Assert.StartsWith("/acces-refuse", (await customer.GetAsync(new Uri("/admin/paiements", UriKind.Relative))).Location(), StringComparison.Ordinal);

        var dispatcher = await StaffAsync(PlatformRole.Dispatcher);
        Assert.StartsWith("/acces-refuse", (await dispatcher.GetAsync(new Uri("/admin/paiements", UriKind.Relative))).Location(), StringComparison.Ordinal);
        Assert.DoesNotContain("/admin/paiements", await dispatcher.GetStringAsync(new Uri("/admin", UriKind.Relative)), StringComparison.Ordinal);

        var finance = await StaffAsync(PlatformRole.Finance);
        await finance.SubmitAsync("/admin/paiements", "/admin/paiements/lien-wave", new Dictionary<string, string> { ["link"] = WaveLink });
        await finance.SubmitAsync("/admin/paiements", "/admin/paiements/lien-wave", new Dictionary<string, string> { ["link"] = "https://pay-wave.example.com/arnaque" });

        await using var db = app.Database.CreateContext();
        Assert.Equal(WaveLink, (await db.AppSettings.SingleAsync(s => s.Key == SettingKeys.WaveLink)).Value);
    }

    private async Task SetWaveLinkAsync(string link)
    {
        var finance = await StaffAsync(PlatformRole.Finance);
        await finance.SubmitAsync("/admin/paiements", "/admin/paiements/lien-wave", new Dictionary<string, string> { ["link"] = link });
    }

    private async Task<HttpClient> StaffAsync(PlatformRole role)
    {
        // Same path as Bootstrap:SuperAdminPhones: the role exists before the first sign-in.
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
        var registered = await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Fatou Sarr",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        return client;
    }

    private async Task<HttpClient> HouseholdAsync()
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        var registered = await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        Assert.Equal("/", registered.Location());
        return client;
    }

    private async Task<Guid> AddHomeAsync(HttpClient client)
    {
        Guid neighborhoodId;
        await using (var db = app.Database.CreateContext())
        {
            neighborhoodId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.Id).SingleAsync();
        }

        var response = await client.SubmitMultipartAsync("/logements/nouveau", "/logements/nouveau", new Dictionary<string, string>
        {
            ["Kind"] = "Villa",
            ["Name"] = "Villa des Almadies",
            ["Landmark"] = "Derrière la mosquée, portail vert",
            ["NeighborhoodId"] = neighborhoodId.ToString(),
        }, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

    [GeneratedRegex("/abonnement/factures/([0-9a-f-]{36})/paiement")]
    private static partial Regex InvoiceAction();
}
