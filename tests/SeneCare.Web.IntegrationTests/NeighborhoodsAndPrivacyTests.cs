using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Subscriptions;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Programme Quartiers, preventive advice, audit log and personal-data export.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class NeighborhoodsAndPrivacyTests(WebAppFixture app)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    [PostgresFact]
    public async Task A_neighborhood_reaching_its_threshold_gets_greening_operations_members_see_once_published()
    {
        var admin = await StaffAsync(PlatformRole.SuperAdmin);
        Guid communeId;
        await using (var db = app.Database.CreateContext())
        {
            communeId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.CommuneId).SingleAsync();
        }

        var name = "Cité Verte " + Guid.NewGuid().ToString("N")[..6];
        await admin.SubmitAsync($"/admin/referentiel/{communeId}", $"/admin/referentiel/{communeId}/quartiers", new Dictionary<string, string> { ["name"] = name });
        Guid neighborhoodId;
        await using (var db = app.Database.CreateContext())
        {
            neighborhoodId = await db.Neighborhoods.Where(n => n.Name == name).Select(n => n.Id).SingleAsync();
        }

        var (customer, propertyId) = await HouseholdAsync(neighborhoodId);
        await using (var db = app.Database.CreateContext())
        {
            var subscription = await db.Subscriptions.SingleAsync(s => s.CoveredPropertyIds.Contains(propertyId));
            var ownerId = (await db.CustomerAccounts.SingleAsync(a => a.Id == subscription.AccountId)).Members.Single().UserId;
            await using var scope = app.Services.CreateAsyncScope();
            Assert.True((await scope.ServiceProvider.GetRequiredService<SimulatedActivation>().ActivateAsync(ownerId, subscription.Id)).IsSuccess);
        }

        await using (var db = app.Database.CreateContext())
        {
            Assert.Equal(1, (await db.NeighborhoodPrograms.SingleAsync(p => p.NeighborhoodId == neighborhoodId)).MemberCount);
        }

        var page = $"/admin/quartiers/{neighborhoodId}";
        await admin.SubmitAsync(page, $"{page}/seuil", new Dictionary<string, string> { ["threshold"] = "1" });
        await admin.SubmitMultipartAsync(page, $"{page}/operations", new Dictionary<string, string>
        {
            ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["trees"] = "25",
            ["description"] = "25 neems plantés devant l'école",
        }, [("photos", "arbres.png", Png)]);

        Guid operationId;
        await using (var db = app.Database.CreateContext())
        {
            var program = await db.NeighborhoodPrograms.SingleAsync(p => p.NeighborhoodId == neighborhoodId);
            Assert.Equal(NeighborhoodProgramStatus.Active, program.Status);
            var operation = Assert.Single(program.Operations);
            Assert.Single(operation.Photos);
            operationId = operation.Id;
        }

        Assert.Contains("0 arbres plantés", WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/espace", UriKind.Relative))), StringComparison.Ordinal);
        await admin.SubmitAsync(page, $"{page}/operations/{operationId}/publier", new Dictionary<string, string>());
        Assert.Contains("25 arbres plantés", WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/espace", UriKind.Relative))), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Published_advice_of_the_month_replaces_the_built_in_one()
    {
        var support = await StaffAsync(PlatformRole.Support);
        var title = "Vérifiez vos gouttières " + Guid.NewGuid().ToString("N")[..6];
        var month = DateTime.UtcNow.Month.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await support.SubmitAsync("/admin/conseils", "/admin/conseils", new Dictionary<string, string>
        {
            ["title"] = title,
            ["body"] = "Videz les feuilles et le sable avant les premières pluies.",
            ["months"] = month,
        });

        Guid adviceId;
        await using (var db = app.Database.CreateContext())
        {
            adviceId = await db.PreventiveAdvices.Where(a => a.Title == title).Select(a => a.Id).SingleAsync();
        }

        var (customer, _) = await HouseholdAsync(null);
        Assert.DoesNotContain(title, WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/espace", UriKind.Relative))), StringComparison.Ordinal);

        await support.SubmitAsync("/admin/conseils", $"/admin/conseils/{adviceId}/publication", new Dictionary<string, string> { ["published"] = "true" });
        try
        {
            Assert.Contains(title, WebUtility.HtmlDecode(await customer.GetStringAsync(new Uri("/espace", UriKind.Relative))), StringComparison.Ordinal);
        }
        finally
        {
            await support.SubmitAsync("/admin/conseils", $"/admin/conseils/{adviceId}/publication", new Dictionary<string, string> { ["published"] = "false" });
        }
    }

    [PostgresFact]
    public async Task Access_to_personal_data_is_logged_and_the_export_contains_the_persons_data()
    {
        var phone = TestData.NewPhone();
        var customer = app.CreateClient();
        await customer.SignInAsync(app.Messages, phone);
        await customer.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Mariama Ba",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        await AddPropertyAsync(customer, null, "Maison de Yoff");

        Guid userId;
        Guid accountId;
        await using (var db = app.Database.CreateContext())
        {
            userId = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => u.Id).SingleAsync();
            accountId = (await db.CustomerAccounts.SingleAsync(a => a.Members.Any(m => m.UserId == userId))).Id;
        }

        var admin = await StaffAsync(PlatformRole.SuperAdmin);
        Assert.Contains("/admin/donnees/", await admin.GetStringAsync(new Uri($"/admin/clients/{accountId}", UriKind.Relative)), StringComparison.Ordinal);

        var export = await admin.GetAsync(new Uri($"/admin/donnees/{userId}/export.json", UriKind.Relative));
        Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
        var json = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains(phone.E164, json, StringComparison.Ordinal);
        Assert.Contains("Maison de Yoff", json, StringComparison.Ordinal);

        await using (var db = app.Database.CreateContext())
        {
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "privacy.view_customer" && a.EntityId == accountId.ToString() && a.IsSensitiveData));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "privacy.export" && a.EntityId == userId.ToString() && a.IsSensitiveData));
        }

        var journal = await admin.GetStringAsync(new Uri("/admin/journal?sensibles=true&q=privacy.", UriKind.Relative));
        Assert.Contains(userId.ToString(), journal, StringComparison.Ordinal);

        var support = await StaffAsync(PlatformRole.Support);
        Assert.StartsWith("/acces-refuse", (await support.GetAsync(new Uri($"/admin/donnees/{userId}/export.json", UriKind.Relative))).Location(), StringComparison.Ordinal);
        Assert.StartsWith("/acces-refuse", (await support.GetAsync(new Uri("/admin/journal", UriKind.Relative))).Location(), StringComparison.Ordinal);
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

    private async Task<(HttpClient Client, Guid PropertyId)> HouseholdAsync(Guid? neighborhoodId)
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, TestData.NewPhone());
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        return (client, await AddPropertyAsync(client, neighborhoodId, "Villa des Almadies"));
    }

    private async Task<Guid> AddPropertyAsync(HttpClient client, Guid? neighborhoodId, string name)
    {
        if (neighborhoodId is null)
        {
            await using var db = app.Database.CreateContext();
            neighborhoodId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.Id).SingleAsync();
        }

        var response = await client.SubmitMultipartAsync("/logements/nouveau", "/logements/nouveau", new Dictionary<string, string>
        {
            ["Kind"] = "Villa",
            ["Name"] = name,
            ["Landmark"] = "Près de la mosquée",
            ["NeighborhoodId"] = neighborhoodId.Value.ToString(),
        }, [("photos", "facade.png", Png)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(response.Location().Split('/')[^1]);
    }
}
