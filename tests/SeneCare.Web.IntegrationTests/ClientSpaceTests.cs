using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Development;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Requests;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

[Collection(WebAppTestGroup.Name)]
public partial class ClientSpaceTests(WebAppFixture app)
{
    // 1×1 PNG: a real image as far as the upload rules are concerned.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task Adding_a_home_creates_a_pending_subscription_and_blocks_reports_until_active()
    {
        var client = await HouseholdAsync();
        var propertyId = await AddHomeAsync(client);

        await using (var db = app.Database.CreateContext())
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(propertyId));
            Assert.Equal(SubscriptionStatus.PendingRegistration, subscription.Status);
            Assert.Equal(8_500, subscription.MonthlyPrice.Amount);

            // Regression: the map sends « 14.7456 »; French number parsing used to drop it.
            var home = await db.Properties.SingleAsync(p => p.Id == propertyId);
            Assert.Equal(14.7456, home.Address.Location!.Latitude, 4);
            Assert.Equal(-17.5130, home.Address.Location.Longitude, 4);
        }

        var report = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/signaler", UriKind.Relative)));
        Assert.Contains("Activez votre abonnement", report, StringComparison.Ordinal);

        var health = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/logements/{propertyId}", UriKind.Relative)));
        Assert.Contains("Carnet de santé", health, StringComparison.Ordinal);
        Assert.Contains("Étanchéité avant l'hivernage", health, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Report_is_created_with_number_sla_and_photo_and_is_idempotent_for_offline_replays()
    {
        var client = await HouseholdAsync();
        var propertyId = await AddHomeAsync(client);
        await ActivateAsync(propertyId);
        var reference = Guid.NewGuid().ToString("N");

        var created = await ReportAsync(client, propertyId, reference);
        var requestId = Guid.Parse(created.Location().Split('/')[^1]);

        await using (var db = app.Database.CreateContext())
        {
            var request = await db.ServiceRequests.SingleAsync(r => r.Id == requestId);
            Assert.Matches(@"^SC-\d{4}-\d{6}$", request.Number);
            Assert.Equal(RequestStatus.Submitted, request.Status);
            Assert.NotNull(request.SlaDeadline);
            Assert.Single(request.Attachments);
            Assert.Single(request.PreferredSlots);
        }

        // The offline queue replays the same form: no duplicate.
        var replay = await ReportAsync(client, propertyId, reference);
        Assert.Equal(created.Location(), replay.Location());
        await using var check = app.Database.CreateContext();
        Assert.Equal(1, await check.ServiceRequests.CountAsync(r => r.ClientReference == reference));
    }

    [PostgresFact]
    public async Task A_customer_never_sees_another_customers_request_or_photos()
    {
        var owner = await HouseholdAsync();
        var propertyId = await AddHomeAsync(owner);
        await ActivateAsync(propertyId);
        var requestUrl = (await ReportAsync(owner, propertyId, Guid.NewGuid().ToString("N"))).Location();

        Guid photoId;
        await using (var db = app.Database.CreateContext())
        {
            var requestId = Guid.Parse(requestUrl.Split('/')[^1]);
            photoId = (await db.ServiceRequests.SingleAsync(r => r.Id == requestId)).Attachments.Single().MediaFileId;
        }

        var stranger = await HouseholdAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri(requestUrl, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri($"/fichiers/{photoId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri($"/logements/{propertyId}", UriKind.Relative))).StatusCode);
        Assert.DoesNotContain(requestUrl, await stranger.GetStringAsync(new Uri("/interventions", UriKind.Relative)), StringComparison.Ordinal);

        var photo = await owner.GetAsync(new Uri($"/fichiers/{photoId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
    }

    [PostgresFact]
    public async Task Files_that_are_not_real_images_are_refused()
    {
        var client = await HouseholdAsync();
        var fields = HomeFields();
        await using (var db = app.Database.CreateContext())
        {
            fields["NeighborhoodId"] = (await db.Neighborhoods.Select(n => n.Id).FirstAsync()).ToString();
        }

        var response = await client.SubmitMultipartAsync(
            "/logements/nouveau", "/logements/nouveau", fields,
            [("photos", "facture.jpg", "MZ this is not an image"u8.ToArray())]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("n'est pas une photo", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Full_journey_slot_choice_intervention_validation_and_health_book()
    {
        var client = await HouseholdAsync();
        var propertyId = await AddHomeAsync(client);
        await ActivateAsync(propertyId);
        var requestUrl = (await ReportAsync(client, propertyId, Guid.NewGuid().ToString("N"))).Location();
        var requestId = Guid.Parse(requestUrl.Split('/')[^1]);

        await SimulateAsync(requestId, SimulationStep.ProposeSlots);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri(requestUrl, UriKind.Relative)));
        Assert.Contains("Choisissez votre créneau", page, StringComparison.Ordinal);
        var slot = SlotValue().Match(page).Groups[1].Value;

        var chosen = await client.SubmitAsync(requestUrl, $"{requestUrl}/creneau", new Dictionary<string, string> { ["slot"] = slot });
        Assert.Equal(requestUrl, chosen.Location());

        await SimulateAsync(requestId, SimulationStep.OnTheWay);
        page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri(requestUrl, UriKind.Relative)));
        Assert.Contains("Moussa Diallo (démo)", page, StringComparison.Ordinal);
        Assert.Contains("https://wa.me/221700000001", page, StringComparison.Ordinal);

        await SimulateAsync(requestId, SimulationStep.StartWork);
        await SimulateAsync(requestId, SimulationStep.Complete);

        var validated = await client.SubmitAsync(requestUrl, $"{requestUrl}/valider", new Dictionary<string, string> { ["score"] = "4", ["comment"] = "Rapide et propre" });
        Assert.Equal(requestUrl, validated.Location());

        await using (var db = app.Database.CreateContext())
        {
            var request = await db.ServiceRequests.SingleAsync(r => r.Id == requestId);
            Assert.Equal(RequestStatus.ValidatedByCustomer, request.Status);
            Assert.Equal(4, request.Rating!.Score);
            Assert.True(await db.Technicians.AnyAsync(t => t.RatingCount > 0));
        }

        var health = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/logements/{propertyId}", UriKind.Relative)));
        Assert.Contains("Garanties en cours", health, StringComparison.Ordinal);
        Assert.Contains("Fuite sous l'évier", health, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task A_review_is_published_only_with_consent_and_until_hidden_by_SENECARE()
    {
        var client = await HouseholdAsync();
        var propertyId = await AddHomeAsync(client);
        await ActivateAsync(propertyId);
        var requestUrl = (await ReportAsync(client, propertyId, Guid.NewGuid().ToString("N"))).Location();
        var requestId = Guid.Parse(requestUrl.Split('/')[^1]);
        await SimulateAsync(requestId, SimulationStep.ProposeSlots);
        var slot = SlotValue().Match(WebUtility.HtmlDecode(await client.GetStringAsync(new Uri(requestUrl, UriKind.Relative)))).Groups[1].Value;
        await client.SubmitAsync(requestUrl, $"{requestUrl}/creneau", new Dictionary<string, string> { ["slot"] = slot });
        foreach (var step in new[] { SimulationStep.OnTheWay, SimulationStep.StartWork, SimulationStep.Complete })
        {
            await SimulateAsync(requestId, step);
        }

        var comment = "Travail soigné " + Guid.NewGuid().ToString("N")[..8];
        await client.SubmitAsync(requestUrl, $"{requestUrl}/valider", new Dictionary<string, string> { ["score"] = "5", ["comment"] = comment, ["publish"] = "true" });

        Assert.Contains(comment, await PublicHomeAsync(), StringComparison.Ordinal);
        Assert.Contains("Awa N.", await PublicHomeAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("Ndiaye", await PublicHomeAsync(), StringComparison.Ordinal);

        var admin = await AdminAsync();
        await admin.SubmitAsync("/admin/avis", $"/admin/avis/{requestId}/visibilite", new Dictionary<string, string> { ["hidden"] = "true" });

        Assert.DoesNotContain(comment, await PublicHomeAsync(), StringComparison.Ordinal);
        await using var db = app.Database.CreateContext();
        Assert.True((await db.ServiceRequests.SingleAsync(r => r.Id == requestId)).Rating!.IsHidden);
    }

    [PostgresFact]
    public async Task A_review_without_consent_stays_private()
    {
        // The full journey validates « Rapide et propre » without ticking the consent box.
        await Full_journey_slot_choice_intervention_validation_and_health_book();

        Assert.DoesNotContain("Rapide et propre", await PublicHomeAsync(), StringComparison.Ordinal);
    }

    private async Task<string> PublicHomeAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "sc_bienvenue=1");
        var response = await app.CreateClient().SendAsync(request);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> AdminAsync()
    {
        var phone = TestData.NewPhone();
        await using (var db = app.Database.CreateContext())
        {
            var user = Domain.Users.User.Register(phone, DateTimeOffset.UtcNow);
            user.GrantRole(Domain.Users.PlatformRole.SuperAdmin, DateTimeOffset.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, phone);
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string> { ["FullName"] = "Fatou Sarr", ["AcceptsPrivacyPolicy"] = "true" });
        return client;
    }

    [PostgresFact]
    public async Task Home_shows_the_request_in_progress_and_the_neighborhood_gauge()
    {
        var client = await HouseholdAsync();
        var propertyId = await AddHomeAsync(client);
        await ActivateAsync(propertyId);
        await ReportAsync(client, propertyId, Guid.NewGuid().ToString("N"));

        var home = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/espace", UriKind.Relative)));

        Assert.Contains("Fuite sous l'évier", home, StringComparison.Ordinal);
        Assert.Contains("Analyse par notre équipe", home, StringComparison.Ordinal);
        Assert.Contains("Programme Quartiers", home, StringComparison.Ordinal);
        Assert.Contains("Almadies", home, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> HomeFields() => new()
    {
        ["Kind"] = "Villa",
        ["Name"] = "Villa des Almadies",
        ["Landmark"] = "Derrière la mosquée, portail vert",
        ["Latitude"] = "14.7456",
        ["Longitude"] = "-17.5130",
    };

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

        var fields = HomeFields();
        fields["NeighborhoodId"] = neighborhoodId.ToString();
        var response = await client.SubmitMultipartAsync("/logements/nouveau", "/logements/nouveau", fields, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

    private async Task ActivateAsync(Guid propertyId)
    {
        await using var db = app.Database.CreateContext();
        var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(propertyId));
        subscription.Activate(DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> ReportAsync(HttpClient client, Guid propertyId, string reference)
    {
        var tomorrow = DateTime.UtcNow.Date.AddDays(1).AddHours(8);
        var response = await client.SubmitMultipartAsync(
            "/signaler", "/signaler",
            [
                new("PropertyId", propertyId.ToString()),
                new("Domain", "Plumbing"),
                new("Description", "Fuite sous l'évier de la cuisine"),
                new("Urgency", "High"),
                new("Slots", new DateTimeOffset(tomorrow, TimeSpan.Zero).ToString("O")),
                new("ClientReference", reference),
            ],
            [("photos", "fuite.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/interventions/", response.Location(), StringComparison.Ordinal);
        return response;
    }

    private async Task SimulateAsync(Guid requestId, SimulationStep step)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<RequestSimulator>().RunAsync(requestId, step);

        Assert.True(result.IsSuccess, result.Error?.Code);
    }

    [GeneratedRegex("name=\"slot\" value=\"([^\"]+)\"")]
    private static partial Regex SlotValue();
}
