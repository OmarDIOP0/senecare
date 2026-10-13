using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Subscriptions;
using SeneCare.Application.Technicians;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Back-office: dashboard, dispatch queue, slots, assignment with suggestions, diagnostics.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class AdminDispatchTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task Dispatcher_proposes_slots_the_customer_chooses_then_a_suggested_technician_is_assigned()
    {
        var (customer, requestId) = await CustomerRequestAsync(Urgency.High);
        var dispatcher = await StaffAsync(PlatformRole.Dispatcher);
        var (technicianId, technicianName) = await TechnicianAsync(ServiceDomain.Plumbing);

        var queue = WebUtility.HtmlDecode(await dispatcher.GetStringAsync(new Uri("/admin/dispatch", UriKind.Relative)));
        Assert.Contains($"/admin/dispatch/{requestId}", queue, StringComparison.Ordinal);

        var page = $"/admin/dispatch/{requestId}";
        await dispatcher.SubmitAsync(page, $"{page}/prendre", new Dictionary<string, string>());
        Assert.Equal(RequestStatus.UnderReview, await StatusAsync(requestId));

        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var slots = new[] { tomorrow.AddHours(9), tomorrow.AddHours(15), tomorrow.AddDays(1).AddHours(10) }
            .Select(d => new KeyValuePair<string, string>("slots", d.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)))
            .ToList();
        var token = await TokenAsync(dispatcher, page);
        slots.Add(new("__RequestVerificationToken", token));
        using (var form = new FormUrlEncodedContent(slots))
        {
            await dispatcher.PostAsync(new Uri($"{page}/creneaux", UriKind.Relative), form);
        }

        Assert.Equal(RequestStatus.AppointmentProposed, await StatusAsync(requestId));

        // The customer picks the second slot in the app.
        var chosen = new DateTimeOffset(tomorrow.AddHours(15), TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);
        await customer.SubmitAsync($"/interventions/{requestId}", $"/interventions/{requestId}/creneau", new Dictionary<string, string> { ["slot"] = chosen });
        Assert.Equal(RequestStatus.AppointmentConfirmed, await StatusAsync(requestId));

        var detail = WebUtility.HtmlDecode(await dispatcher.GetStringAsync(new Uri(page, UriKind.Relative)));
        Assert.Contains(technicianName, detail, StringComparison.Ordinal); // suggested: has the trade
        Assert.Contains("Dans sa zone", detail, StringComparison.Ordinal);

        await dispatcher.SubmitAsync(page, $"{page}/affecter", new Dictionary<string, string> { ["technicianId"] = technicianId.ToString() });
        await using (var db = app.Database.CreateContext())
        {
            Assert.True(await db.Interventions.AnyAsync(i => i.RequestId == requestId && i.LeadTechnicianId == technicianId));
        }

        var calendar = WebUtility.HtmlDecode(await dispatcher.GetStringAsync(new Uri($"/admin/dispatch/calendrier?semaine={tomorrow:yyyy-MM-dd}", UriKind.Relative)));
        Assert.Contains($"/admin/dispatch/{requestId}", calendar, StringComparison.Ordinal);
        Assert.Contains(technicianName, calendar, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await dispatcher.GetAsync(new Uri("/admin/dispatch/carte", UriKind.Relative))).StatusCode);
    }

    [PostgresFact]
    public async Task A_request_requalified_critical_is_dispatched_at_once_with_the_offer_delay()
    {
        var (_, requestId) = await CustomerRequestAsync(Urgency.Normal);
        var dispatcher = await StaffAsync(PlatformRole.SuperAdmin);
        var (technicianId, _) = await TechnicianAsync(ServiceDomain.Plumbing);
        var page = $"/admin/dispatch/{requestId}";

        var before = DateTimeOffset.UtcNow;
        await dispatcher.SubmitAsync(page, $"{page}/urgence", new Dictionary<string, string> { ["urgency"] = "Critical" });
        await using (var db = app.Database.CreateContext())
        {
            var request = await db.ServiceRequests.SingleAsync(r => r.Id == requestId);
            Assert.Equal(Urgency.Critical, request.Urgency);
            // « Maisons & Villas »: critical within 4 h, counted from the requalification.
            Assert.InRange(request.SlaDeadline!.Value, before.AddHours(4).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(4).AddMinutes(1));
        }

        await dispatcher.SubmitAsync(page, $"{page}/maintenant", new Dictionary<string, string> { ["technicianId"] = technicianId.ToString() });
        await using var check = app.Database.CreateContext();
        Assert.Equal(RequestStatus.AppointmentConfirmed, (await check.ServiceRequests.SingleAsync(r => r.Id == requestId)).Status);
        Assert.True(await check.Interventions.AnyAsync(i => i.RequestId == requestId && i.LeadTechnicianId == technicianId));
    }

    [PostgresFact]
    public async Task Back_office_pages_follow_the_admin_sub_roles()
    {
        var (customer, _) = await CustomerRequestAsync(Urgency.Normal);
        Assert.StartsWith("/acces-refuse", (await customer.GetAsync(new Uri("/admin", UriKind.Relative))).Location(), StringComparison.Ordinal);

        var finance = await StaffAsync(PlatformRole.Finance);
        var dashboard = WebUtility.HtmlDecode(await finance.GetStringAsync(new Uri("/admin", UriKind.Relative)));
        Assert.Contains("Encaissements par mois", dashboard, StringComparison.Ordinal);
        Assert.StartsWith("/acces-refuse", (await finance.GetAsync(new Uri("/admin/dispatch", UriKind.Relative))).Location(), StringComparison.Ordinal);

        var dispatcher = await StaffAsync(PlatformRole.Dispatcher);
        Assert.Equal("/admin", (await dispatcher.GetAsync(new Uri("/espace", UriKind.Relative))).Location());
        Assert.StartsWith("/acces-refuse", (await dispatcher.GetAsync(new Uri("/admin/paiements", UriKind.Relative))).Location(), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Supervisor_assigns_the_quarterly_diagnostic_and_validates_the_report()
    {
        var manager = app.CreateClient();
        await manager.SignInAsync(app.Messages, TestData.NewPhone());
        await manager.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Ousmane Fall",
            ["Profile"] = "Business",
            ["CompanyName"] = "Kaolack Négoce",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        var siteId = await AddPropertyAsync(manager, "Office", "Entrepôt Kaolack");
        await manager.SubmitAsync("/entreprise/offre", "/entreprise/offre", new Dictionary<string, string> { ["plan"] = "ESSENTIEL" });
        Guid diagnosticId;
        await using (var db = app.Database.CreateContext())
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(siteId));
            var managerId = (await db.CustomerAccounts.SingleAsync(a => a.Id == subscription.AccountId)).Members.Single().UserId;
            await using var scope = app.Services.CreateAsyncScope();
            Assert.True((await scope.ServiceProvider.GetRequiredService<SimulatedActivation>().ActivateAsync(managerId, subscription.Id)).IsSuccess);
            diagnosticId = await db.Diagnostics.Where(d => d.SubscriptionId == subscription.Id).Select(d => d.Id).SingleAsync();
        }

        var dispatcher = await StaffAsync(PlatformRole.Dispatcher);
        var (technicianId, _) = await TechnicianAsync(ServiceDomain.Waterproofing);
        var page = $"/admin/dispatch/diagnostics/{diagnosticId}";
        Assert.Contains(page, await dispatcher.GetStringAsync(new Uri("/admin/dispatch/diagnostics", UriKind.Relative)), StringComparison.Ordinal);
        await dispatcher.SubmitAsync(page, $"{page}/affecter", new Dictionary<string, string> { ["technicianId"] = technicianId.ToString() });

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var technicianUserId = await TechnicianUserAsync(technicianId);
            var field = scope.ServiceProvider.GetRequiredService<DiagnosticFieldHandler>();
            Assert.True((await field.AddPointAsync(technicianUserId, diagnosticId, siteId, ServiceDomain.Waterproofing, "Toiture", ControlPointResult.Anomaly, "Fissures", null)).IsSuccess);
            Assert.True((await field.SubmitAsync(technicianUserId, diagnosticId, null)).IsSuccess);
        }

        Assert.Contains("Fissures", WebUtility.HtmlDecode(await dispatcher.GetStringAsync(new Uri(page, UriKind.Relative))), StringComparison.Ordinal);
        var validated = await dispatcher.SubmitAsync(page, $"{page}/valider", new Dictionary<string, string>());
        Assert.Equal("/admin/dispatch/diagnostics", validated.Location());

        await using var check = app.Database.CreateContext();
        Assert.Equal(DiagnosticStatus.Validated, (await check.Diagnostics.SingleAsync(d => d.Id == diagnosticId)).Status);
        Assert.True(await check.AuditLogs.AnyAsync(a => a.Action == "diagnostic.validate" && a.EntityId == diagnosticId.ToString()));
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
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Fatou Sarr",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        return client;
    }

    private async Task<(Guid Id, string Name)> TechnicianAsync(ServiceDomain skill)
    {
        var name = "Ibrahima Ndoye " + Guid.NewGuid().ToString("N")[..6];
        await using var db = app.Database.CreateContext();
        var user = User.Register(TestData.NewPhone(), DateTimeOffset.UtcNow);
        user.UpdateProfile(name, null, Language.French);
        user.GrantRole(PlatformRole.Technician, DateTimeOffset.UtcNow);
        db.Users.Add(user);
        // Covers the commune of Almadies, where the test buildings are.
        var commune = await (
                from n in db.Neighborhoods
                where n.Name == "Almadies"
                select n.CommuneId)
            .SingleAsync();
        var technician = new Technician(user.Id, [skill], [commune]);
        db.Technicians.Add(technician);
        await db.SaveChangesAsync();
        return (technician.Id, name);
    }

    private async Task<Guid> TechnicianUserAsync(Guid technicianId)
    {
        await using var db = app.Database.CreateContext();
        return await db.Technicians.Where(t => t.Id == technicianId).Select(t => t.UserId).SingleAsync();
    }

    private async Task<(HttpClient Client, Guid RequestId)> CustomerRequestAsync(Urgency urgency)
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        var propertyId = await AddPropertyAsync(client, "Villa", "Villa des Almadies");
        await using (var db = app.Database.CreateContext())
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(propertyId));
            subscription.Activate(DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var response = await client.SubmitMultipartAsync(
            "/signaler", "/signaler",
            [
                new("PropertyId", propertyId.ToString()),
                new("Domain", "Plumbing"),
                new("Description", "Fuite sous l'évier de la cuisine"),
                new("Urgency", urgency.ToString()),
                new("ClientReference", Guid.NewGuid().ToString("N")),
            ],
            [("photos", "fuite.png", Png)]);
        return (client, Guid.Parse(response.Location().Split('/')[^1]));
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
            ["Landmark"] = "Face à la pharmacie",
            ["Latitude"] = "14.7456",
            ["Longitude"] = "-17.5130",
            ["NeighborhoodId"] = neighborhoodId.ToString(),
        }, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }

    private async Task<RequestStatus> StatusAsync(Guid requestId)
    {
        await using var db = app.Database.CreateContext();
        return await db.ServiceRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync();
    }

    private static async Task<string> TokenAsync(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
