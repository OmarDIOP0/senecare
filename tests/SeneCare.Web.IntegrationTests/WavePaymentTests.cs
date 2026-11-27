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

    [DatabaseFact]
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

    [DatabaseFact]
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

    [DatabaseFact]
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

    [DatabaseFact]
    public async Task Customer_pays_with_orange_money_and_the_subscription_starts_once_orange_confirms()
    {
        app.OrangeMoney.Configured = true;
        try
        {
            await SetWaveLinkAsync(WaveLink);
            var customer = await HouseholdAsync();
            var propertyId = await AddHomeAsync(customer);

            // Sandbox (test keys, no real money): customers are not offered Orange Money.
            app.OrangeMoney.IsLive = false;
            var sandbox = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
            Assert.DoesNotContain("value=\"OrangeMoney\"", sandbox, StringComparison.Ordinal);
            app.OrangeMoney.IsLive = true;

            // Both ways to pay; Orange Money has no reference to type, only its button.
            var page = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
            Assert.Contains("name=\"channel\" value=\"OrangeMoney\"", page, StringComparison.Ordinal);
            var invoiceId = Guid.Parse(InvoiceAction().Match(page).Groups[1].Value);
            Assert.Contains($"formaction=\"/abonnement/factures/{invoiceId}/orange-money\"", page, StringComparison.Ordinal);

            // Orange Money cannot be « declared » any more (confirmed by its API only).
            await customer.SubmitAsync("/abonnement", $"/abonnement/factures/{invoiceId}/paiement", new Dictionary<string, string> { ["channel"] = "OrangeMoney", ["reference"] = "X" });
            await using (var db = app.Database.CreateContext())
            {
                Assert.False(await db.Payments.AnyAsync(p => p.InvoiceId == invoiceId));
            }

            var started = await customer.SubmitAsync("/abonnement", $"/abonnement/factures/{invoiceId}/orange-money", new Dictionary<string, string>());
            Assert.StartsWith("/abonnement/orange-money/", started.Location(), StringComparison.Ordinal);
            var paymentId = Guid.Parse(started.Location().Split('/')[^1]);
            var reference = paymentId.ToString("N");
            // Max it sends the person back to this payment page.
            Assert.Contains(app.OrangeMoney.Requests, r => r.Reference == reference && r.Amount.Amount == 18_500 && r.ReturnPath == started.Location());

            var pay = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri(started.Location(), UriKind.Relative)));
            Assert.Contains("https://sugu.orange-sonatel.com/mp/test-maxit", pay, StringComparison.Ordinal);
            Assert.Contains("data:image/png;base64,", pay, StringComparison.Ordinal);
            Assert.Contains("data-maxit=\"https://sugu.orange-sonatel.com/mp/test-maxit\"", pay, StringComparison.Ordinal);

            // Kept with the payment: reloading the page (or coming back from Max it) still shows the request.
            await using (var db = app.Database.CreateContext())
            {
                var saved = await db.Payments.SingleAsync(p => p.Id == paymentId);
                Assert.Equal("https://sugu.orange-sonatel.com/mp/test-maxit", saved.CheckoutLink);
                Assert.NotNull(saved.CheckoutQrCode);
            }

            Assert.Contains("https://sugu.orange-sonatel.com/mp/test-maxit", WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri(started.Location(), UriKind.Relative))), StringComparison.Ordinal);

            var orange = app.CreateClient();
            // Not Orange Money (wrong key): refused, nothing changes.
            Assert.Equal(HttpStatusCode.Unauthorized, (await NotifyAsync(orange, reference, "Basic wrong")).StatusCode);
            // The right key but Orange has no payment yet: received, still pending (the body is never trusted).
            Assert.Equal(HttpStatusCode.Accepted, (await NotifyAsync(orange, reference, "Basic " + FakeOrangeMoneyGateway.CallbackKey)).StatusCode);
            await using (var db = app.Database.CreateContext())
            {
                Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(p => p.Id == paymentId)).Status);
            }

            // Paid in Max it: Orange confirms → invoice paid, subscription active; a second notification changes nothing.
            app.OrangeMoney.Report(reference, 18_500);
            Assert.Equal(HttpStatusCode.Accepted, (await NotifyAsync(orange, reference, "Basic " + FakeOrangeMoneyGateway.CallbackKey)).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, (await NotifyAsync(orange, reference, "Basic " + FakeOrangeMoneyGateway.CallbackKey)).StatusCode);
            await using (var db = app.Database.CreateContext())
            {
                var payment = await db.Payments.SingleAsync(p => p.Id == paymentId);
                Assert.Equal(PaymentStatus.Succeeded, payment.Status);
                Assert.StartsWith("MP", payment.ProviderReference, StringComparison.Ordinal);
                var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
                Assert.Equal(InvoiceStatus.Paid, invoice.Status);
                Assert.Equal(18_500, invoice.AmountPaid.Amount);
                Assert.Equal(SubscriptionStatus.Active, (await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(propertyId))).Status);
                Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.Action == "payment.orange_money_confirmed" && l.EntityId == paymentId.ToString()));
            }

            // The payment page sends the customer back to a running subscription.
            Assert.Equal("/abonnement", (await customer.GetAsync(new Uri(started.Location(), UriKind.Relative))).Location());
        }
        finally
        {
            app.OrangeMoney.Configured = false;
        }
    }

    [DatabaseFact]
    public async Task An_orange_money_payment_of_another_amount_is_never_credited()
    {
        app.OrangeMoney.Configured = true;
        try
        {
            var customer = await HouseholdAsync();
            await AddHomeAsync(customer);
            var page = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)));
            var invoiceId = OrangeAction().Match(page).Groups[1].Value;
            var started = await customer.SubmitAsync("/abonnement", $"/abonnement/factures/{invoiceId}/orange-money", new Dictionary<string, string>());
            var paymentId = Guid.Parse(started.Location().Split('/')[^1]);

            app.OrangeMoney.Report(paymentId.ToString("N"), 100);
            // « Vérifier mon paiement » asks Orange: wrong amount → still waiting, Finance warned.
            await customer.SubmitAsync(started.Location(), $"/abonnement/orange-money/{paymentId}/verifier", new Dictionary<string, string>());
            await using var db = app.Database.CreateContext();
            Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(p => p.Id == paymentId)).Status);
            Assert.Equal(InvoiceStatus.Issued, (await db.Invoices.SingleAsync(i => i.Id == Guid.Parse(invoiceId))).Status);
            Assert.True(await db.Notifications.AnyAsync(n => n.TemplateKey == "payment.mismatch"));

            // The right amount but no merchant code in Orange's answer: still never credited (fail closed).
            app.OrangeMoney.Report(paymentId.ToString("N"), 18_500, merchant: null);
            await customer.SubmitAsync(started.Location(), $"/abonnement/orange-money/{paymentId}/verifier", new Dictionary<string, string>());
            await using var after = app.Database.CreateContext();
            Assert.Equal(PaymentStatus.Pending, (await after.Payments.SingleAsync(p => p.Id == paymentId)).Status);
        }
        finally
        {
            app.OrangeMoney.Configured = false;
        }
    }

    private static Task<HttpResponseMessage> NotifyAsync(HttpClient client, string reference, string authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/paiements/orange-money/notification", UriKind.Relative))
        {
            Content = new StringContent($$"""{"reference":"{{reference}}","status":"SUCCESS","type":"MERCHANT_PAYMENT"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return client.SendAsync(request);
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
            ["Kind"] = "House",
            ["Name"] = "Villa des Almadies",
            ["Landmark"] = "Derrière la mosquée, portail vert",
            ["NeighborhoodId"] = neighborhoodId.ToString(),
        }, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

    [GeneratedRegex("/abonnement/factures/([0-9a-f-]{36})/paiement")]
    private static partial Regex InvoiceAction();

    [GeneratedRegex("/abonnement/factures/([0-9a-f-]{36})/orange-money")]
    private static partial Regex OrangeAction();
}
