using System.Net;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Suspending a customer (reversible): no more sign-in, data kept, out of the list until reactivated.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class CustomerSuspensionTests(WebAppFixture app)
{
    [DatabaseFact]
    public async Task A_suspended_customer_cannot_sign_in_until_reactivated()
    {
        var admin = await SuperAdminAsync();
        var phone = TestData.NewPhone();
        var name = "Famille " + Guid.NewGuid().ToString("N")[..6];
        var customer = app.CreateClient();
        await customer.SignInAsync(app.Messages, phone);
        await customer.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye", ["Profile"] = "Household", ["AcceptsPrivacyPolicy"] = "true",
        });

        Guid accountId;
        await using (var db = app.Database.CreateContext())
        {
            var userId = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => u.Id).SingleAsync();
            var account = await db.CustomerAccounts.SingleAsync(a => a.Members.Any(m => m.UserId == userId));
            accountId = account.Id;
            account.Rename(name);
            await db.SaveChangesAsync();
        }

        // Without the confirmation box, nothing happens.
        await admin.SubmitAsync($"/admin/clients/{accountId}", $"/admin/clients/{accountId}/suspendre", new Dictionary<string, string>());
        await using (var db = app.Database.CreateContext())
        {
            Assert.Null((await db.CustomerAccounts.SingleAsync(a => a.Id == accountId)).SuspendedAt);
        }

        var suspended = await admin.SubmitAsync($"/admin/clients/{accountId}", $"/admin/clients/{accountId}/suspendre", new Dictionary<string, string> { ["confirm"] = "true" });
        Assert.Equal($"/admin/clients/{accountId}", suspended.Location());
        await using (var db = app.Database.CreateContext())
        {
            Assert.NotNull((await db.CustomerAccounts.SingleAsync(a => a.Id == accountId)).SuspendedAt);
            Assert.False((await db.Users.SingleAsync(u => u.PhoneNumber == phone)).IsActive);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "customer.suspend" && a.EntityId == accountId.ToString()));
        }

        // Out of the list, in « Archivés ».
        var list = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri("/admin/clients?q=" + Uri.EscapeDataString(name), UriKind.Relative)));
        Assert.DoesNotContain($"/admin/clients/{accountId}\"", list, StringComparison.Ordinal);
        var archived = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri("/admin/clients?archives=true&q=" + Uri.EscapeDataString(name), UriKind.Relative)));
        Assert.Contains($"/admin/clients/{accountId}\"", archived, StringComparison.Ordinal);

        // Signing in again is refused (the code is right, the account is not).
        await ForgetCodesAsync(phone);
        var again = app.CreateClient();
        var sent = await again.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Channel"] = "Sms" });
        Assert.Equal("/connexion/code", sent.Location());
        var verified = await again.SubmitAsync("/connexion/code", "/connexion/code", new Dictionary<string, string> { ["Code"] = app.Messages.LastCodeFor(phone) });
        Assert.NotEqual(HttpStatusCode.Redirect, verified.StatusCode);

        // Reactivated: back in the list, signs in again.
        await admin.SubmitAsync($"/admin/clients/{accountId}", $"/admin/clients/{accountId}/reactiver", new Dictionary<string, string>());
        await using (var db = app.Database.CreateContext())
        {
            Assert.Null((await db.CustomerAccounts.SingleAsync(a => a.Id == accountId)).SuspendedAt);
            Assert.True((await db.Users.SingleAsync(u => u.PhoneNumber == phone)).IsActive);
        }

        Assert.Contains($"/admin/clients/{accountId}\"", WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri("/admin/clients?q=" + Uri.EscapeDataString(name), UriKind.Relative))), StringComparison.Ordinal);
        await ForgetCodesAsync(phone);
        await app.CreateClient().SignInAsync(app.Messages, phone);
    }

    /// <summary>Past codes of this number, so that a new one can be sent at once (resend cooldown).</summary>
    private async Task ForgetCodesAsync(PhoneNumber phone)
    {
        await using var db = app.Database.CreateContext();
        db.OtpChallenges.RemoveRange(await db.OtpChallenges.Where(c => c.PhoneNumber == phone).ToListAsync());
        await db.SaveChangesAsync();
    }

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
}
