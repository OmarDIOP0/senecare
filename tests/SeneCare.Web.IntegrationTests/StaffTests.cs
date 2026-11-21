using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>
/// « Équipe SeneCare »: super-admins name other admins (temporary password shown once), change or remove their
/// rights with guards; one account may be admin and technician; a new technician gets a temporary password.
/// </summary>
[Collection(WebAppTestGroup.Name)]
public sealed partial class StaffTests(WebAppFixture app)
{
    [DatabaseFact]
    public async Task A_super_admin_names_an_admin_who_signs_in_with_the_temporary_password()
    {
        var super_ = await PasswordStaffAsync(PlatformRole.SuperAdmin);
        var phone = TestData.NewPhone();

        var added = await PostAsync(super_, "/admin/equipe", "/admin/equipe", [new("phone", phone.Formatted), new("fullName", "Mariama Ba"), new("roles", "Dispatcher"), new("roles", "Support")]);
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        var page = WebUtility.HtmlDecode(await super_.GetStringAsync(new Uri("/admin/equipe", UriKind.Relative)));
        var temporary = TemporaryPassword().Match(page).Groups[1].Value;
        Assert.Equal(10, temporary.Length);
        Assert.Contains("Mariama Ba", page, StringComparison.Ordinal);
        // Shown once only.
        Assert.DoesNotContain(temporary, WebUtility.HtmlDecode(await super_.GetStringAsync(new Uri("/admin/equipe", UriKind.Relative))), StringComparison.Ordinal);

        await using (var db = app.Database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.PhoneNumber == phone);
            Assert.True(user.HasRole(PlatformRole.Dispatcher) && user.HasRole(PlatformRole.Support));
            Assert.False(user.HasRole(PlatformRole.SuperAdmin));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "staff.add" && a.EntityId == user.Id.ToString()));
        }

        var member = app.CreatePasswordClient();
        var signedIn = await member.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = temporary });
        Assert.Equal("/compte/mot-de-passe", signedIn.Location());
    }

    [DatabaseFact]
    public async Task Rights_are_guarded_nobody_edits_themself_and_only_super_admins_reach_the_team()
    {
        var (super_, superPhone) = await StaffAsync(PlatformRole.SuperAdmin);
        Guid superId;
        await using (var db = app.Database.CreateContext())
        {
            superId = await db.Users.Where(u => u.PhoneNumber == superPhone).Select(u => u.Id).SingleAsync();
        }

        // Not on oneself: own rights stay.
        await PostAsync(super_, "/admin/equipe", $"/admin/equipe/{superId}/roles", [new("roles", "Support")]);
        await PostAsync(super_, "/admin/equipe", $"/admin/equipe/{superId}/retirer", []);
        await using (var db = app.Database.CreateContext())
        {
            Assert.True((await db.Users.SingleAsync(u => u.Id == superId)).HasRole(PlatformRole.SuperAdmin));
        }

        // Another member: rights changed, then removed; at least one right when changing.
        var (_, dispatcherPhone) = await StaffAsync(PlatformRole.Dispatcher);
        Guid dispatcherId;
        await using (var db = app.Database.CreateContext())
        {
            dispatcherId = await db.Users.Where(u => u.PhoneNumber == dispatcherPhone).Select(u => u.Id).SingleAsync();
        }

        await PostAsync(super_, "/admin/equipe", $"/admin/equipe/{dispatcherId}/roles", []);
        await PostAsync(super_, "/admin/equipe", $"/admin/equipe/{dispatcherId}/roles", [new("roles", "Finance")]);
        await using (var db = app.Database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == dispatcherId);
            Assert.True(user.HasRole(PlatformRole.Finance));
            Assert.False(user.HasRole(PlatformRole.Dispatcher));
        }

        await PostAsync(super_, "/admin/equipe", $"/admin/equipe/{dispatcherId}/retirer", []);
        await using (var db = app.Database.CreateContext())
        {
            Assert.False((await db.Users.SingleAsync(u => u.Id == dispatcherId)).IsStaff);
        }

        var (support, _) = await StaffAsync(PlatformRole.Support);
        Assert.StartsWith("/acces-refuse", (await support.GetAsync(new Uri("/admin/equipe", UriKind.Relative))).Location(), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task An_admin_can_also_be_a_technician_and_a_new_technician_gets_a_temporary_password()
    {
        var (super_, _) = await StaffAsync(PlatformRole.SuperAdmin);
        var (_, dispatcherPhone) = await PasswordStaffWithPhoneAsync(PlatformRole.Dispatcher);
        Guid communeId;
        await using (var db = app.Database.CreateContext())
        {
            communeId = await db.Neighborhoods.Where(n => n.Name == "Almadies").Select(n => n.CommuneId).SingleAsync();
        }

        // The dispatcher becomes technician too: same account, « Mes missions » in the admin menu.
        await PostAsync(super_, "/admin/techniciens", "/admin/techniciens", Technician(dispatcherPhone, communeId));
        // Signs in again: the technician role is in the new session.
        var dispatcher = app.CreatePasswordClient();
        await dispatcher.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = dispatcherPhone.Formatted, ["Password"] = "Teranga-Admin-2026" });
        var admin = WebUtility.HtmlDecode(await dispatcher.GetStringAsync(new Uri("/admin", UriKind.Relative)));
        Assert.Contains("href=\"/technicien\"", admin, StringComparison.Ordinal);
        Assert.Contains("Mes missions", admin, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await dispatcher.GetAsync(new Uri("/technicien", UriKind.Relative))).StatusCode);
        var team = WebUtility.HtmlDecode(await super_.GetStringAsync(new Uri("/admin/equipe", UriKind.Relative)));
        Assert.Contains("Aussi technicien", team, StringComparison.Ordinal);

        // A brand-new technician (password sign-in): a temporary password on their file, enough to sign in.
        var passwordAdmin = await PasswordStaffAsync(PlatformRole.SuperAdmin);
        var phone = TestData.NewPhone();
        var created = await PostAsync(passwordAdmin, "/admin/techniciens", "/admin/techniciens", Technician(phone, communeId));
        var file = WebUtility.HtmlDecode(await passwordAdmin.GetStringAsync(new Uri(created.Location(), UriKind.Relative)));
        var temporary = TemporaryPassword().Match(file).Groups[1].Value;
        Assert.Equal(10, temporary.Length);
        var technician = app.CreatePasswordClient();
        var signedIn = await technician.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = temporary });
        Assert.Equal("/compte/mot-de-passe", signedIn.Location());
    }

    private static List<KeyValuePair<string, string>> Technician(PhoneNumber phone, Guid communeId) =>
    [
        new("phone", phone.Formatted), new("fullName", "Cheikh Mbaye"), new("skills", "Plumbing"),
        new("communes", communeId.ToString()), new("days", "Monday"), new("start", "08:00"), new("end", "18:00"),
    ];

    private async Task<(HttpClient Client, PhoneNumber Phone)> StaffAsync(PlatformRole role)
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
        return (client, phone);
    }

    /// <summary>A staff member signed in by password (Auth:Mode = Password, where temporary passwords exist).</summary>
    private async Task<HttpClient> PasswordStaffAsync(PlatformRole role) => (await PasswordStaffWithPhoneAsync(role)).Client;

    private async Task<(HttpClient Client, PhoneNumber Phone)> PasswordStaffWithPhoneAsync(PlatformRole role)
    {
        var phone = TestData.NewPhone();
        Guid userId;
        await using (var db = app.Database.CreateContext())
        {
            var user = User.Register(phone, DateTimeOffset.UtcNow);
            user.GrantRole(role, DateTimeOffset.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        using (var scope = app.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<SeneCare.Application.Abstractions.IIdentityService>();
            await using var db = app.Database.CreateContext();
            await identity.EnsureIdentityUserAsync(await db.Users.SingleAsync(u => u.Id == userId));
            await identity.SetPasswordAsync(userId, "Teranga-Admin-2026", mustChange: false);
        }

        var client = app.CreatePasswordClient();
        await client.SubmitAsync("/connexion", "/connexion", new Dictionary<string, string> { ["Phone"] = phone.Formatted, ["Password"] = "Teranga-Admin-2026" });
        await client.SubmitAsync("/inscription", "/inscription", new Dictionary<string, string> { ["FullName"] = "Aminata Fall", ["AcceptsPrivacyPolicy"] = "true" });
        return (client, phone);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string url, List<KeyValuePair<string, string>> fields)
    {
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        fields.Add(new("__RequestVerificationToken", WebUtility.HtmlDecode(token)));
        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync(new Uri(url, UriKind.Relative), form);
    }

    [GeneratedRegex("select-all\">([a-z0-9]{10})<")]
    private static partial Regex TemporaryPassword();
}
