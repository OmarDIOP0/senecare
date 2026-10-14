using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>
/// The offer chosen in the 4-step sign-up becomes the subscription (monthly or yearly), the payment is
/// declared then verified with notifications both ways, and invoices / receipts come as PDF.
/// </summary>
[Collection(WebAppTestGroup.Name)]
public sealed partial class OfferAndPaymentTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task A_yearly_home_plan_chosen_at_sign_up_is_billed_twelve_months_minus_two()
    {
        var customer = await SignUpAsync("Household", "HOME", annual: true, company: null);
        var propertyId = await AddPlaceAsync(customer, "Villa", "Villa de Ngor");

        await using (var db = app.Database.CreateContext())
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(propertyId));
            Assert.Equal(12, subscription.BillingPeriodMonths);
            Assert.Equal(2, subscription.FreeMonthsPerPeriod);
        }

        // Registration 10 000 + 10 months of 8 500.
        var page = Normalize(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
        Assert.Contains("95 000 FCFA", page, StringComparison.Ordinal);
        Assert.Contains("2 mois offerts", page, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task A_company_gets_its_chosen_plan_with_its_first_site()
    {
        var manager = await SignUpAsync("Business", "PROFESSIONNEL", annual: false, company: "Teranga Logistique");
        var first = await AddPlaceAsync(manager, "Office", "Siège Plateau");
        var second = await AddPlaceAsync(manager, "Warehouse", "Dépôt Rufisque");

        await using var db = app.Database.CreateContext();
        var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(first));
        Assert.Contains(second, subscription.CoveredPropertyIds);
        Assert.Equal(3, subscription.BillingPeriodMonths);
        Assert.Equal(2_500_000, subscription.MonthlyPrice.Amount);
    }

    [PostgresFact]
    public async Task Sign_up_in_five_steps_creates_the_account_its_first_site_and_the_subscription_to_pay()
    {
        var client = app.CreateClient();
        var phone = TestData.NewPhone();

        // Steps 1–2 without an account: profile, then the number.
        var start = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/inscription?profil=Business", UriKind.Relative)));
        Assert.Contains("Étape 1/5", start, StringComparison.Ordinal);
        var sent = await client.SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
        {
            ["Signup"] = "true",
            ["Profile"] = "Business",
            ["Phone"] = phone.Formatted,
            ["Channel"] = "Sms",
        });
        Assert.Equal("/connexion/code", sent.Location());
        Assert.Contains("Étape 2/5", WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/connexion/code", UriKind.Relative))), StringComparison.Ordinal);
        var verified = await client.SubmitAsync("/connexion/code", "/connexion/code", new Dictionary<string, string> { ["Code"] = app.Messages.LastCodeFor(phone) });
        Assert.Equal("/inscription", verified.Location());

        // Steps 3–5 in one form: the profile chosen before the code is kept.
        Guid neighborhoodId;
        await using (var db = app.Database.CreateContext())
        {
            neighborhoodId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.Id).SingleAsync();
        }

        Assert.Contains("value=\"Business\" checked", await client.GetStringAsync(new Uri("/inscription", UriKind.Relative)), StringComparison.Ordinal);
        var done = await client.SubmitMultipartAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["Profile"] = "Business",
            ["FullName"] = "Moussa Fall",
            ["CompanyName"] = "Boulangerie Fall",
            ["Property.Kind"] = "Shop",
            ["Property.Name"] = "Boutique Ouakam",
            ["Property.NeighborhoodId"] = neighborhoodId.ToString(),
            ["Property.Landmark"] = "En face de la pharmacie",
            ["PlanCode"] = "ESSENTIEL",
            ["Annual"] = "false",
            ["AcceptsPrivacyPolicy"] = "true",
        }, [("photos", "facade.png", Png)]);
        Assert.Equal("/abonnement", done.Location());

        await using (var db = app.Database.CreateContext())
        {
            var property = await db.Properties.SingleAsync(p => p.Name == "Boutique Ouakam" && p.Address.NeighborhoodId == neighborhoodId && p.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
            var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(property.Id));
            Assert.Equal(1_500_000, subscription.MonthlyPrice.Amount);
        }

        // No neighborhood: nothing is created, the form comes back.
        var other = app.CreateClient();
        await other.SignInAsync(app.Messages, TestData.NewPhone());
        var refused = await other.SubmitMultipartAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["Profile"] = "Household",
            ["FullName"] = "Awa Ndiaye",
            ["Property.Kind"] = "House",
            ["Property.Name"] = "Maison de Yoff",
            ["AcceptsPrivacyPolicy"] = "true",
        }, []);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Choisissez votre quartier", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Declaring_and_verifying_a_payment_notifies_finance_then_the_customer_with_a_receipt()
    {
        var finance = await StaffAsync(PlatformRole.Finance);
        await finance.SubmitAsync("/admin/paiements", "/admin/paiements/lien-wave", new Dictionary<string, string> { ["link"] = "https://pay.wave.com/m/M_sn_test/c/sn/?amount={montant}" });

        var customer = await SignUpAsync("Household", "HOME", annual: false, company: null);
        await AddPlaceAsync(customer, "Villa", "Villa des Almadies");
        var page = await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative));
        var invoiceId = InvoiceAction().Match(page).Groups[1].Value;
        await customer.SubmitAsync("/abonnement", $"/abonnement/factures/{invoiceId}/paiement", new Dictionary<string, string> { ["reference"] = "T_WAVE_77" });

        // Finance sees it in the bell.
        var bell = WebUtility.HtmlDecode(await finance.GetStringAsync(new Uri("/notifications", UriKind.Relative)));
        Assert.Contains("Paiement Wave à vérifier", bell, StringComparison.Ordinal);

        Guid paymentId;
        await using (var db = app.Database.CreateContext())
        {
            paymentId = await db.Payments.Where(p => p.InvoiceId == Guid.Parse(invoiceId)).Select(p => p.Id).SingleAsync();
        }

        await finance.SubmitAsync("/admin/paiements", $"/admin/paiements/{paymentId}/valider", new Dictionary<string, string>());

        // The customer: notified in the app (and by message), receipt as PDF.
        var customerBell = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/notifications", UriKind.Relative)));
        Assert.Contains("Paiement reçu, abonnement actif", customerBell, StringComparison.Ordinal);
        Assert.Contains(app.Messages.Messages, m => m.Text.Contains("Votre abonnement est actif", StringComparison.Ordinal));

        var pdf = await customer.GetAsync(new Uri($"/abonnement/factures/{invoiceId}.pdf", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4], StringComparison.Ordinal);

        // Nobody else gets it.
        var stranger = await SignUpAsync("Household", null, annual: false, company: null);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri($"/abonnement/factures/{invoiceId}.pdf", UriKind.Relative))).StatusCode);
    }

    private async Task<HttpClient> SignUpAsync(string profile, string? plan, bool annual, string? company)
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        var fields = new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = profile,
            ["AcceptsPrivacyPolicy"] = "true",
            ["Annual"] = annual ? "true" : "false",
        };
        if (plan is not null)
        {
            fields["PlanCode"] = plan;
        }

        if (company is not null)
        {
            fields["CompanyName"] = company;
        }

        var registered = await client.SubmitAsync("/inscription", "/inscription", fields);
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        return client;
    }

    private async Task<Guid> AddPlaceAsync(HttpClient client, string kind, string name)
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
            ["Landmark"] = "Derrière la mosquée, portail vert",
            ["NeighborhoodId"] = neighborhoodId.ToString(),
        }, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

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

    private static string Normalize(string html) => WebUtility.HtmlDecode(html).Replace(' ', ' ').Replace(' ', ' ');

    [GeneratedRegex("/abonnement/factures/([0-9a-f-]{36})/paiement")]
    private static partial Regex InvoiceAction();
}
