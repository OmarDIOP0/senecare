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

    [PostgresFact]
    public async Task Erasure_anonymises_the_person_and_their_home_but_keeps_invoices()
    {
        var phone = TestData.NewPhone();
        var customer = app.CreateClient();
        await customer.SignInAsync(app.Messages, phone);
        await customer.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Ndèye Fall",
            ["Profile"] = "Household",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        var propertyId = await AddPropertyAsync(customer, null, "Maison de Ndèye à Ouakam");
        await customer.GetStringAsync(new Uri("/abonnement", UriKind.Relative)); // issues an invoice

        Guid userId;
        Guid accountId;
        Guid photoId;
        await using (var db = app.Database.CreateContext())
        {
            userId = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => u.Id).SingleAsync();
            var property = await db.Properties.SingleAsync(p => p.Id == propertyId);
            accountId = property.AccountId;
            photoId = property.Photos.Single().MediaFileId;
        }

        var admin = await StaffAsync(PlatformRole.SuperAdmin);
        var file = $"/admin/clients/{accountId}";
        // Without the explicit confirmation, nothing happens.
        await admin.SubmitAsync(file, $"/admin/donnees/{userId}/effacer", new Dictionary<string, string>());
        await using (var db = app.Database.CreateContext())
        {
            Assert.Null((await db.Users.SingleAsync(u => u.Id == userId)).AnonymizedAt);
        }

        await admin.SubmitAsync(file, $"/admin/donnees/{userId}/effacer", new Dictionary<string, string> { ["confirm"] = "true", ["returnUrl"] = file });

        await using (var db = app.Database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            Assert.NotNull(user.AnonymizedAt);
            Assert.True(user.PhoneNumber.IsAnonymized);
            Assert.Null(user.FullName);
            Assert.False(user.IsActive);
            Assert.DoesNotContain(user.Consents, c => c.IsActive);

            var property = await db.Properties.SingleAsync(p => p.Id == propertyId);
            Assert.Equal("[supprimé]", property.Name);
            Assert.Null(property.Address.Landmark);
            Assert.Empty(property.Photos);
            Assert.Equal(Domain.Billing.SubscriptionStatus.Cancelled, (await db.Subscriptions.SingleAsync(s => s.AccountId == accountId)).Status);
            Assert.True(await db.Invoices.AnyAsync(i => i.AccountId == accountId)); // kept: legal obligation
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "privacy.erase" && a.EntityId == userId.ToString() && a.IsSensitiveData));
        }

        // The photo file is gone, even for SENECARE.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri($"/fichiers/{photoId}", UriKind.Relative))).StatusCode);
        var export = await admin.GetStringAsync(new Uri($"/admin/donnees/{userId}/export.json", UriKind.Relative));
        Assert.DoesNotContain("Ndèye", export, StringComparison.Ordinal);
        Assert.DoesNotContain(phone.E164, export, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Erasure_is_refused_for_a_company_last_manager_and_for_staff()
    {
        var phone = TestData.NewPhone();
        var manager = app.CreateClient();
        await manager.SignInAsync(app.Messages, phone);
        await manager.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Ousmane Fall",
            ["Profile"] = "Business",
            ["CompanyName"] = "Ziguinchor Bois",
            ["AcceptsPrivacyPolicy"] = "true",
        });
        Guid managerId;
        await using (var db = app.Database.CreateContext())
        {
            managerId = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => u.Id).SingleAsync();
            var account = await db.CustomerAccounts.SingleAsync(a => a.Members.Any(m => m.UserId == managerId));
            var colleague = User.Register(TestData.NewPhone(), DateTimeOffset.UtcNow);
            db.Users.Add(colleague);
            account.AddMember(colleague.Id, Domain.Accounts.AccountRole.Reporter, [], DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var admin = await StaffAsync(PlatformRole.SuperAdmin);
        await admin.SubmitAsync("/admin/clients", $"/admin/donnees/{managerId}/effacer", new Dictionary<string, string> { ["confirm"] = "true" });

        Guid staffId;
        await using (var db = app.Database.CreateContext())
        {
            Assert.Null((await db.Users.SingleAsync(u => u.Id == managerId)).AnonymizedAt);
            var technician = User.Register(TestData.NewPhone(), DateTimeOffset.UtcNow);
            technician.GrantRole(PlatformRole.Technician, DateTimeOffset.UtcNow);
            db.Users.Add(technician);
            await db.SaveChangesAsync();
            staffId = technician.Id;
        }

        await admin.SubmitAsync("/admin/clients", $"/admin/donnees/{staffId}/effacer", new Dictionary<string, string> { ["confirm"] = "true" });
        await using var check = app.Database.CreateContext();
        Assert.Null((await check.Users.SingleAsync(u => u.Id == staffId)).AnonymizedAt);
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
