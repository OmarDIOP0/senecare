using System.Net;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Covered zones: admins open / close regions and communes; elsewhere no new home, a « Prévenez-moi » instead.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class ZonesTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [DatabaseFact]
    public async Task A_closed_region_takes_no_new_home_and_collects_waitlist_numbers()
    {
        var admin = await SuperAdminAsync();
        Guid regionId;
        Guid koldaNeighborhood;
        Guid koldaCommune;
        await using (var db = app.Database.CreateContext())
        {
            regionId = await db.Regions.Where(r => r.Name == "Kolda").Select(r => r.Id).SingleAsync();
            (koldaCommune, koldaNeighborhood) = await (
                    from n in db.Neighborhoods
                    join c in db.Communes on n.CommuneId equals c.Id
                    join d in db.Departments on c.DepartmentId equals d.Id
                    where d.RegionId == regionId && c.Name == "Kolda"
                    select new ValueTuple<Guid, Guid>(c.Id, n.Id))
                .FirstAsync();
        }

        try
        {
            Assert.Equal(HttpStatusCode.Redirect, (await admin.SubmitAsync(
                "/admin/referentiel", $"/admin/referentiel/regions/{regionId}/couverture", new Dictionary<string, string> { ["served"] = "false" })).StatusCode);

            // The picker shows Kolda as not covered, with « Prévenez-moi », and no way to pick it.
            var customer = await HouseholdAsync();
            var results = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/quartiers/recherche?q=kolda&field=NeighborhoodId", UriKind.Relative)));
            Assert.Contains("pas encore couvert", results, StringComparison.Ordinal);
            Assert.Contains($"/zones/attente?commune={koldaCommune}", results, StringComparison.Ordinal);
            Assert.DoesNotContain($"data-pick-id=\"{koldaNeighborhood}\"", results, StringComparison.Ordinal);

            // Even a crafted form cannot place a home there.
            var refused = await AddHomeAsync(customer, koldaNeighborhood);
            Assert.NotEqual(HttpStatusCode.Redirect, refused.StatusCode);
            await using (var db = app.Database.CreateContext())
            {
                Assert.False(await db.Properties.AnyAsync(p => p.Address.NeighborhoodId == koldaNeighborhood));
            }

            // « Prévenez-moi »: the number is kept for that zone; admins read it (audited) and erase it once handled.
            var visitor = app.CreateClient();
            var joined = await visitor.SubmitAsync($"/zones/attente?commune={koldaCommune}", "/zones/attente", new Dictionary<string, string>
            {
                ["phone"] = "77 555 12 34", ["fullName"] = "Mamadou Baldé", ["regionId"] = regionId.ToString(), ["communeId"] = koldaCommune.ToString(),
            });
            Assert.Equal("/", joined.Location());
            var list = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/referentiel/attente?region={regionId}", UriKind.Relative)));
            Assert.Contains("77 555 12 34", list, StringComparison.Ordinal);
            Assert.Contains("Mamadou Baldé", list, StringComparison.Ordinal);
            Guid requestId;
            await using (var db = app.Database.CreateContext())
            {
                requestId = await db.CoverageRequests.Where(c => c.RegionId == regionId).Select(c => c.Id).FirstAsync();
                Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "coverage.waitlist_read" && a.IsSensitiveData));
            }

            await admin.SubmitAsync("/admin/referentiel/attente", $"/admin/referentiel/attente/{requestId}/supprimer", new Dictionary<string, string>());
            await using (var db = app.Database.CreateContext())
            {
                Assert.False(await db.CoverageRequests.AnyAsync(c => c.Id == requestId));
            }

            // Opened again: the home is accepted.
            await admin.SubmitAsync("/admin/referentiel", $"/admin/referentiel/regions/{regionId}/couverture", new Dictionary<string, string> { ["served"] = "true" });
            Assert.Equal(HttpStatusCode.Redirect, (await AddHomeAsync(customer, koldaNeighborhood)).StatusCode);
        }
        finally
        {
            await using var db = app.Database.CreateContext();
            var region = await db.Regions.SingleAsync(r => r.Id == regionId);
            region.SetServed(true);
            await db.SaveChangesAsync();
        }
    }

    [DatabaseFact]
    public async Task Admins_add_a_commune_missing_from_the_reference_data()
    {
        var admin = await SuperAdminAsync();
        Guid departmentId;
        await using (var db = app.Database.CreateContext())
        {
            departmentId = await db.Departments.Where(d => d.Name == "Vélingara").Select(d => d.Id).FirstAsync();
        }

        var name = "Pakour " + Guid.NewGuid().ToString("N")[..4];
        var added = await admin.SubmitAsync("/admin/referentiel", "/admin/referentiel/communes", new Dictionary<string, string>
        {
            ["departmentId"] = departmentId.ToString(), ["name"] = name,
        });
        Assert.StartsWith("/admin/referentiel/", added.Location(), StringComparison.Ordinal);

        await using (var db = app.Database.CreateContext())
        {
            var commune = await db.Communes.SingleAsync(c => c.Name == name);
            Assert.True(commune.IsServed);
            Assert.True(await db.Neighborhoods.AnyAsync(n => n.CommuneId == commune.Id && n.Name == name));
        }

        // Customers find it at once.
        var customer = await HouseholdAsync();
        var results = WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri($"/quartiers/recherche?q={Uri.EscapeDataString(name)}&field=NeighborhoodId", UriKind.Relative)));
        Assert.Contains("data-pick-id=", results, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> AddHomeAsync(HttpClient client, Guid neighborhoodId) =>
        await client.SubmitMultipartAsync("/logements/nouveau", "/logements/nouveau", new Dictionary<string, string>
        {
            ["Kind"] = "Villa",
            ["Name"] = "Maison de Kolda",
            ["NeighborhoodId"] = neighborhoodId.ToString(),
            ["Latitude"] = "12.8833",
            ["Longitude"] = "-14.95",
        }, [("photos", "facade.png", Png)]);

    private async Task<HttpClient> SuperAdminAsync()
    {
        var phone = TestData.NewPhone();
        await using (var db = app.Database.CreateContext())
        {
            var user = User.Register(phone, DateTimeOffset.UtcNow);
            user.GrantRole(PlatformRole.SuperAdmin, DateTimeOffset.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, phone);
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string> { ["FullName"] = "Fatou Sarr", ["AcceptsPrivacyPolicy"] = "true" });
        return client;
    }

    private async Task<HttpClient> HouseholdAsync()
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        return client;
    }
}
