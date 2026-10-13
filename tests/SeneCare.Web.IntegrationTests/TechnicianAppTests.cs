using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Development;
using SeneCare.Application.Subscriptions;
using SeneCare.Application.Technicians;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Technician app: missions, one-tap statuses, field report, live position, diagnostics.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class TechnicianAppTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task Technician_goes_from_en_route_to_done_and_the_customer_gets_the_report()
    {
        var (customer, requestId) = await CustomerRequestAsync();
        var (technician, technicianId, technicianUserId) = await TechnicianAsync();
        var interventionId = await AssignAsync(requestId, technicianId);
        var mission = $"/technicien/missions/{interventionId}";

        var list = WebUtility.HtmlDecode(await technician.GetStringAsync(new Uri("/technicien", UriKind.Relative)));
        Assert.Contains(mission, list, StringComparison.Ordinal);
        Assert.Contains("Almadies", list, StringComparison.Ordinal);
        var page = await technician.GetAsync(new Uri(mission, UriKind.Relative));
        Assert.Equal("page", page.Headers.GetValues("X-SeneCare-Offline").Single()); // kept on the phone for the day
        Assert.Contains("La prise du salon fait des étincelles", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()), StringComparison.Ordinal);

        await technician.SubmitAsync(mission, $"{mission}/etape/en-route", new Dictionary<string, string>());
        Assert.Equal(RequestStatus.OnTheWay, await StatusAsync(requestId));

        // Live position: the customer follows, a stranger cannot.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var sharing = scope.ServiceProvider.GetRequiredService<PositionSharing>();
            Assert.NotNull(await sharing.ShareAsync(technicianUserId, interventionId, 14.7456, -17.5130));
            Assert.Null(await sharing.ShareAsync(technicianUserId, interventionId, 48.85, 2.35)); // not in Senegal
            var customerId = await UserIdOfRequestAsync(requestId);
            var (allowed, position) = await sharing.FollowAsync(customerId, requestId);
            Assert.True(allowed);
            Assert.Equal(14.7456, position!.Latitude, 4);
            Assert.False((await sharing.FollowAsync(technicianUserId, requestId)).Allowed);
        }

        var tracking = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri($"/interventions/{requestId}", UriKind.Relative)));
        Assert.Contains("data-tracking", tracking, StringComparison.Ordinal);

        await technician.SubmitAsync(mission, $"{mission}/etape/sur-place", new Dictionary<string, string>());
        await technician.SubmitAsync(mission, $"{mission}/etape/en-cours", new Dictionary<string, string>());
        Guid firstItem;
        await using (var db = app.Database.CreateContext())
        {
            var request = await db.ServiceRequests.SingleAsync(r => r.Id == requestId);
            Assert.Equal(RequestStatus.InProgress, request.Status);
            Assert.NotNull(request.RespondedAt); // the SLA stopped at « sur place »
            firstItem = (await db.Interventions.SingleAsync(i => i.Id == interventionId)).Checklist.First().Id;
        }

        await technician.SubmitAsync(mission, $"{mission}/checklist/{firstItem}", new Dictionary<string, string> { ["done"] = "true" });
        await technician.SubmitAsync(mission, $"{mission}/pieces", new Dictionary<string, string> { ["name"] = "Câble 2,5 mm²", ["quantity"] = "2,5", ["unit"] = "m" });
        await technician.SubmitMultipartAsync(mission, $"{mission}/photos", new Dictionary<string, string> { ["moment"] = "avant" }, [("photos", "avant.png", Png)]);

        // Closing without an « after » photo is refused.
        await technician.SubmitAsync(mission, $"{mission}/terminer", new Dictionary<string, string> { ["findings"] = "Prise grillée remplacée", ["noSignatureReason"] = "Client absent" });
        Assert.Equal(RequestStatus.InProgress, await StatusAsync(requestId));

        await technician.SubmitMultipartAsync(mission, $"{mission}/photos", new Dictionary<string, string> { ["moment"] = "apres" }, [("photos", "apres.png", Png)]);
        await technician.SubmitMultipartAsync(mission, $"{mission}/signature", new Dictionary<string, string> { ["signedBy"] = "Awa Ndiaye" }, [("signature", "signature.png", Png)]);
        var done = await technician.SubmitAsync(mission, $"{mission}/terminer", new Dictionary<string, string> { ["findings"] = "Prise grillée remplacée, circuit testé." });
        Assert.Equal("/technicien", done.Location());

        await using (var db = app.Database.CreateContext())
        {
            var request = await db.ServiceRequests.SingleAsync(r => r.Id == requestId);
            Assert.Equal(RequestStatus.Completed, request.Status);
            var intervention = await db.Interventions.SingleAsync(i => i.Id == interventionId);
            Assert.Equal("Awa Ndiaye", intervention.SignedByName);
            Assert.NotNull(intervention.WarrantyEndsOn);
            Assert.StartsWith("Prise grillée remplacée, circuit testé.", intervention.Report, StringComparison.Ordinal);
            Assert.Contains("Étapes réalisées : Couper le courant au tableau.", intervention.Report, StringComparison.Ordinal);
            Assert.Contains("Pièces utilisées : Câble 2,5 mm² × 2,5 m.", intervention.Report, StringComparison.Ordinal);
            Assert.Equal(2, intervention.Photos.Count);
        }

        // A status tap queued offline and replayed after closing changes nothing.
        await technician.SubmitAsync(mission, $"{mission}/etape/en-route", new Dictionary<string, string>());
        Assert.Equal(RequestStatus.Completed, await StatusAsync(requestId));

        var pdf = await customer.GetAsync(new Uri($"/interventions/{requestId}/rapport.pdf", UriKind.Relative));
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4], StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Technicians_only_reach_their_own_missions_and_customers_files()
    {
        var (customer, requestId) = await CustomerRequestAsync();
        var (_, technicianId, _) = await TechnicianAsync();
        var interventionId = await AssignAsync(requestId, technicianId);
        var (other, _, _) = await TechnicianAsync();
        var mission = $"/technicien/missions/{interventionId}";

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri(mission, UriKind.Relative))).StatusCode);
        await other.SubmitAsync("/technicien", $"{mission}/etape/en-route", new Dictionary<string, string>());
        Assert.Equal(RequestStatus.AppointmentConfirmed, await StatusAsync(requestId));
        Assert.DoesNotContain(mission, await other.GetStringAsync(new Uri("/technicien", UriKind.Relative)), StringComparison.Ordinal);

        Guid photoId;
        await using (var db = app.Database.CreateContext())
        {
            photoId = (await db.ServiceRequests.SingleAsync(r => r.Id == requestId)).Attachments.First().MediaFileId;
        }

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"/fichiers/{photoId}", UriKind.Relative))).StatusCode);
        Assert.StartsWith("/acces-refuse", (await customer.GetAsync(new Uri("/technicien", UriKind.Relative))).Location(), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Technician_records_the_quarterly_diagnostic_on_site()
    {
        var manager = app.CreateClient();
        await manager.SignInAsync(app.Messages, TestData.NewPhone());
        await manager.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Ousmane Fall",
            ["Profile"] = "Business",
            ["CompanyName"] = "Sahel Distribution",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        var siteId = await AddPropertyAsync(manager, "Office", "Magasin Pikine");
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

        var (technician, technicianId, _) = await TechnicianAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<AssignmentHandler>().AssignDiagnosticAsync(diagnosticId, technicianId)).IsSuccess);
        }

        var page = $"/technicien/diagnostics/{diagnosticId}";
        Assert.Contains(page, await technician.GetStringAsync(new Uri("/technicien", UriKind.Relative)), StringComparison.Ordinal);

        await technician.SubmitMultipartAsync(page, $"{page}/points", new Dictionary<string, string>
        {
            ["siteId"] = siteId.ToString(),
            ["domain"] = "Waterproofing",
            ["label"] = "Toiture-terrasse",
            ["result"] = "Anomaly",
            ["comment"] = "Évacuation bouchée par le sable",
        }, [("photo", "terrasse.png", Png)]);
        await technician.SubmitAsync(page, $"{page}/remettre", new Dictionary<string, string> { ["recommendations"] = "Déboucher avant l'hivernage." });

        await using (var db = app.Database.CreateContext())
        {
            var diagnostic = await db.Diagnostics.SingleAsync(d => d.Id == diagnosticId);
            Assert.Equal(DiagnosticStatus.Submitted, diagnostic.Status);
            Assert.Equal(technicianId, diagnostic.TechnicianId);
            var point = Assert.Single(diagnostic.ControlPoints);
            Assert.Equal(ControlPointResult.Anomaly, point.Result);
            Assert.Single(point.Photos);
        }

        var report = WebUtility.HtmlDecode(await manager.GetStringAsync(new Uri($"/entreprise/diagnostics/{diagnosticId}", UriKind.Relative)));
        Assert.Contains("Évacuation bouchée par le sable", report, StringComparison.Ordinal);
    }

    private async Task<(HttpClient Client, Guid TechnicianId, Guid UserId)> TechnicianAsync()
    {
        var phone = TestData.NewPhone();
        Guid technicianId;
        Guid userId;
        await using (var db = app.Database.CreateContext())
        {
            var user = User.Register(phone, DateTimeOffset.UtcNow);
            user.GrantRole(PlatformRole.Technician, DateTimeOffset.UtcNow);
            db.Users.Add(user);
            var communes = await db.Communes.Select(c => c.Id).Take(1).ToListAsync();
            var technician = new Technician(user.Id, Enum.GetValues<ServiceDomain>(), communes);
            db.Technicians.Add(technician);
            await db.SaveChangesAsync();
            technicianId = technician.Id;
            userId = user.Id;
        }

        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, phone);
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Mamadou Diop",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        return (client, technicianId, userId);
    }

    /// <summary>A household with an active home and a request whose appointment is confirmed.</summary>
    private async Task<(HttpClient Client, Guid RequestId)> CustomerRequestAsync()
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
                new("Domain", "Electricity"),
                new("Description", "La prise du salon fait des étincelles"),
                new("Urgency", "High"),
                new("ClientReference", Guid.NewGuid().ToString("N")),
            ],
            [("photos", "prise.png", Png)]);
        var requestId = Guid.Parse(response.Location().Split('/')[^1]);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var simulator = scope.ServiceProvider.GetRequiredService<RequestSimulator>();
            Assert.True((await simulator.RunAsync(requestId, SimulationStep.ProposeSlots)).IsSuccess);
        }

        await using (var db = app.Database.CreateContext())
        {
            var request = await db.ServiceRequests.SingleAsync(r => r.Id == requestId);
            request.ConfirmAppointment(request.CurrentAppointment!.ProposedSlots.First(), request.ReportedByUserId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        return (client, requestId);
    }

    private async Task<Guid> AssignAsync(Guid requestId, Guid technicianId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AssignmentHandler>().AssignInterventionAsync(requestId, technicianId);
        Assert.True(result.IsSuccess, result.Error?.Code);
        return result.Value;
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
            ["Landmark"] = "Derrière la mosquée, portail vert",
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

    private async Task<Guid> UserIdOfRequestAsync(Guid requestId)
    {
        await using var db = app.Database.CreateContext();
        return await db.ServiceRequests.Where(r => r.Id == requestId).Select(r => r.ReportedByUserId).SingleAsync();
    }
}
