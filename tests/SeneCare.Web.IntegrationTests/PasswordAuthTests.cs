using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>
/// Default sign-in (Auth:Mode = Password): the sign-up asks for a number and a password, the sign-in checks it
/// (same message whatever is wrong, lockout after 5 mistakes), and a temporary password given by support must be
/// replaced before anything else.
/// </summary>
[Collection(WebAppTestGroup.Name)]
public sealed class PasswordAuthTests(WebAppFixture app)
{
    private const string Password = "Teranga2026";

    [DatabaseFact]
    public async Task Signs_up_with_a_number_and_a_password_then_signs_in_with_them()
    {
        var phone = TestData.NewPhone();
        var client = app.CreatePasswordClient();

        var signup = await client.SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
        {
            ["Signup"] = "true",
            ["Profile"] = "Household",
            ["Phone"] = phone.Formatted,
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
        });
        Assert.Equal("/inscription", signup.Location());

        // Same number again: refused, the person is told to sign in.
        var other = app.CreatePasswordClient();
        var again = await other.SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
        {
            ["Signup"] = "true",
            ["Phone"] = phone.Formatted,
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
        });
        Assert.Contains("déjà un compte", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()), StringComparison.Ordinal);

        var wrong = await other.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = "pas-le-bon" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Numéro ou mot de passe incorrect", WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()), StringComparison.Ordinal);

        var right = await other.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = Password });
        Assert.Equal("/inscription", right.Location());
    }

    [DatabaseFact]
    public async Task Signed_in_pages_are_not_kept_by_the_browser_public_pages_are()
    {
        var client = app.CreatePasswordClient();
        Assert.False((await client.GetAsync(new Uri("/tarifs", UriKind.Relative))).Headers.CacheControl?.NoStore ?? false);

        await client.SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
        {
            ["Signup"] = "true", ["Phone"] = TestData.NewPhone().Formatted, ["Password"] = Password, ["ConfirmPassword"] = Password,
        });

        // « Retour » after signing out must ask the server again (which sends to the sign-in page).
        var page = await client.GetAsync(new Uri("/inscription", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
    }

    [DatabaseFact]
    public async Task Refuses_a_short_password_or_two_different_ones()
    {
        var client = app.CreatePasswordClient();
        foreach (var (password, confirm, message) in new[] { ("court", "court", "au moins 8"), (Password, Password + "x", "pas identiques") })
        {
            var response = await client.SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
            {
                ["Signup"] = "true",
                ["Phone"] = TestData.NewPhone().Formatted,
                ["Password"] = password,
                ["ConfirmPassword"] = confirm,
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        }
    }

    [DatabaseFact]
    public async Task Locks_the_account_after_five_wrong_passwords()
    {
        var phone = TestData.NewPhone();
        var client = app.CreatePasswordClient();
        await client.SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
        {
            ["Signup"] = "true", ["Phone"] = phone.Formatted, ["Password"] = Password, ["ConfirmPassword"] = Password,
        });

        var other = app.CreatePasswordClient();
        for (var i = 0; i < 5; i++)
        {
            await other.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = "faux-" + i });
        }

        var locked = await other.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("bloqué", WebUtility.HtmlDecode(await locked.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_temporary_password_from_support_must_be_replaced_before_anything_else()
    {
        // A finished household account.
        var phone = TestData.NewPhone();
        var customer = app.CreatePasswordClient();
        await customer.SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
        {
            ["Signup"] = "true", ["Phone"] = phone.Formatted, ["Password"] = Password, ["ConfirmPassword"] = Password,
        });
        await customer.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye", ["Profile"] = "Household", ["AcceptsPrivacyPolicy"] = "true",
        });

        Guid accountId, userId;
        await using (var db = app.Database.CreateContext())
        {
            userId = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => u.Id).SingleAsync();
            accountId = await db.CustomerAccounts.Where(a => a.Members.Any(m => m.UserId == userId)).Select(a => a.Id).SingleAsync();
        }

        // Support resets it: the temporary password is shown once on the customer file.
        var support = await StaffAsync(PlatformRole.Support);
        var reset = await support.SubmitAsync($"/admin/clients/{accountId}", $"/admin/clients/{accountId}/membres/{userId}/mot-de-passe", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        var file = WebUtility.HtmlDecode(await support.GetStringAsync(new Uri($"/admin/clients/{accountId}", UriKind.Relative)));
        var temporary = System.Text.RegularExpressions.Regex.Match(file, "select-all\">([a-z0-9]{10})<").Groups[1].Value;
        Assert.Equal(10, temporary.Length);

        // The old password no longer works; the temporary one leads to « choose your password », and only there.
        var phoneClient = app.CreatePasswordClient();
        var old = await phoneClient.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        var signedIn = await phoneClient.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = temporary });
        Assert.Equal("/compte/mot-de-passe", signedIn.Location());
        Assert.Equal("/compte/mot-de-passe", (await phoneClient.GetAsync(new Uri("/abonnement", UriKind.Relative))).Location());

        var changed = await phoneClient.SubmitAsync("/compte/mot-de-passe", "/compte/mot-de-passe", new Dictionary<string, string>
        {
            ["NewPassword"] = "Dakar-Yoff-2026", ["ConfirmPassword"] = "Dakar-Yoff-2026",
        });
        Assert.Equal("/", changed.Location());
        Assert.Equal(HttpStatusCode.OK, (await phoneClient.GetAsync(new Uri("/abonnement", UriKind.Relative))).StatusCode);
    }

    [DatabaseFact]
    public async Task A_first_admin_not_registered_yet_can_choose_a_password_then_finish_registration()
    {
        // Like Bootstrap:SuperAdminPhones + SuperAdminPassword: a number with a temporary password, no name yet.
        var phone = TestData.NewPhone();
        await app.CreatePasswordClient().SubmitAsync("/inscription", "/connexion", new Dictionary<string, string>
        {
            ["Signup"] = "true", ["Phone"] = phone.Formatted, ["Password"] = Password, ["ConfirmPassword"] = Password,
        });
        Guid userId;
        await using (var db = app.Database.CreateContext())
        {
            userId = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => u.Id).SingleAsync();
        }

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SeneCare.Application.Abstractions.IIdentityService>()
                .SetPasswordAsync(userId, "Provisoire-2026", mustChange: true);
        }

        var admin = app.CreatePasswordClient();
        var signedIn = await admin.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = "Provisoire-2026" });
        Assert.Equal("/compte/mot-de-passe", signedIn.Location());

        // No loop: the page opens, and so does the service worker.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri("/compte/mot-de-passe", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri("/sw.js", UriKind.Relative))).StatusCode);
        Assert.Equal("/compte/mot-de-passe", (await admin.GetAsync(new Uri("/inscription", UriKind.Relative))).Location());

        var changed = await admin.SubmitAsync("/compte/mot-de-passe", "/compte/mot-de-passe", new Dictionary<string, string>
        {
            ["NewPassword"] = "Dakar-Yoff-2026", ["ConfirmPassword"] = "Dakar-Yoff-2026",
        });
        Assert.Equal("/inscription", changed.Location());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri("/inscription", UriKind.Relative))).StatusCode);
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

        // Staff sign in with the code here (the OTP host); the reset itself does not depend on the mode.
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, phone);
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string> { ["FullName"] = "Fatou Sarr", ["AcceptsPrivacyPolicy"] = "true" });
        return client;
    }
}
