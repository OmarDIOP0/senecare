using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Accounts;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

public partial class PwaTests(SeneCareWebFactory factory) : IClassFixture<SeneCareWebFactory>
{
    [Fact]
    public async Task Service_worker_is_served_with_versioned_precache_that_all_resolve()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/sw.js", UriKind.Relative));
        var script = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("__PRECACHE__", script, StringComparison.Ordinal);
        Assert.Matches("const VERSION = \"[0-9a-f]{12}\"", script);

        var precache = JsonSerializer.Deserialize<string[]>(PrecacheList().Match(script).Groups[1].Value)!;
        Assert.Contains("/hors-ligne", precache);
        Assert.Contains(precache, url => url.StartsWith("/css/app.css?v=", StringComparison.Ordinal));
        foreach (var url in precache)
        {
            var asset = await client.GetAsync(new Uri(url, UriKind.Relative));
            Assert.True(asset.IsSuccessStatusCode, $"{url} → {(int)asset.StatusCode}");
        }
    }

    [Fact]
    public async Task Manifest_declares_icons_shortcuts_and_screenshots_that_exist()
    {
        var client = factory.CreateClient();

        using var manifest = JsonDocument.Parse(await client.GetStringAsync(new Uri("/manifest.webmanifest", UriKind.Relative)));
        var root = manifest.RootElement;

        Assert.Equal("SeneCare", root.GetProperty("short_name").GetString());
        Assert.Contains(root.GetProperty("icons").EnumerateArray(), i => i.GetProperty("purpose").GetString() == "maskable");
        Assert.Equal(2, root.GetProperty("shortcuts").GetArrayLength());

        var files = root.GetProperty("icons").EnumerateArray().Select(i => i.GetProperty("src").GetString()!)
            .Concat(root.GetProperty("screenshots").EnumerateArray().Select(s => s.GetProperty("src").GetString()!))
            .Concat(root.GetProperty("shortcuts").EnumerateArray().SelectMany(s => s.GetProperty("icons").EnumerateArray()).Select(i => i.GetProperty("src").GetString()!));
        foreach (var file in files.Distinct())
        {
            var response = await client.GetAsync(new Uri(file, UriKind.Relative));
            Assert.True(response.IsSuccessStatusCode, $"{file} → {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Offline_page_is_available_without_signing_in()
    {
        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync(new Uri("/hors-ligne?en-attente=1", UriKind.Relative)));

        Assert.Contains("Pas de connexion", html, StringComparison.Ordinal);
        Assert.Contains("partira automatiquement", html, StringComparison.Ordinal);
    }

    [GeneratedRegex("const PRECACHE = (\\[.*?\\]);")]
    private static partial Regex PrecacheList();
}

[Collection(WebAppTestGroup.Name)]
public class GuidedTourTests(WebAppFixture app)
{
    [PostgresFact]
    public async Task Tour_runs_once_and_can_be_replayed_from_the_profile()
    {
        var client = await RegisteredAsync(AccountKind.Household);

        var first = await client.GetStringAsync(new Uri("/espace", UriKind.Relative));
        Assert.Contains("id=\"sc-tour\"", first, StringComparison.Ordinal);
        Assert.Contains("\"key\":\"client\"", first, StringComparison.Ordinal);
        Assert.Contains("data-tour=\"signaler\"", first, StringComparison.Ordinal);

        var done = await client.SubmitAsync("/espace", "/visite/client/terminee", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        Assert.DoesNotContain("id=\"sc-tour\"", await client.GetStringAsync(new Uri("/espace", UriKind.Relative)), StringComparison.Ordinal);

        var replay = await client.SubmitAsync("/compte", "/visite/rejouer", new Dictionary<string, string>());
        Assert.Equal("/espace", replay.Location());
        Assert.Contains("id=\"sc-tour\"", await client.GetStringAsync(new Uri("/espace", UriKind.Relative)), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Tour_progress_is_stored_on_the_server_not_the_device()
    {
        var phone = TestData.NewPhone();
        var phoneA = await RegisteredAsync(AccountKind.Household, phone);
        await phoneA.SubmitAsync("/espace", "/visite/client/terminee", new Dictionary<string, string>());

        // Same person, another phone: signs in again, tour not shown.
        // (The previous code is aged so the 1-minute resend rule doesn't apply.)
        await using (var setup = app.Database.CreateContext())
        {
            await setup.OtpChallenges.Where(c => c.PhoneNumber == phone)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, c => c.CreatedAt.AddMinutes(-2)));
        }

        var phoneB = app.CreateClient();
        await phoneB.SignInAsync(app.Messages, phone);
        Assert.DoesNotContain("id=\"sc-tour\"", await phoneB.GetStringAsync(new Uri("/espace", UriKind.Relative)), StringComparison.Ordinal);

        await using var db = app.Database.CreateContext();
        Assert.Contains("client", (await db.Users.SingleAsync(u => u.PhoneNumber == phone)).CompletedTours);
    }

    [PostgresFact]
    public async Task Business_space_has_its_own_navigation_and_tour()
    {
        var client = await RegisteredAsync(AccountKind.Business);

        var html = await client.GetStringAsync(new Uri("/espace", UriKind.Relative));

        Assert.Contains("\"key\":\"entreprise\"", html, StringComparison.Ordinal);
        Assert.Contains("data-tour=\"diagnostics\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/entreprise/sites\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/logements\"", html, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Placeholder_sections_respect_policies()
    {
        var client = await RegisteredAsync(AccountKind.Household);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/interventions", UriKind.Relative))).StatusCode);
        Assert.StartsWith("/acces-refuse", (await client.GetAsync(new Uri("/entreprise/sites", UriKind.Relative))).Location(), StringComparison.Ordinal);
        Assert.StartsWith("/acces-refuse", (await client.GetAsync(new Uri("/admin/dispatch", UriKind.Relative))).Location(), StringComparison.Ordinal);
    }

    private async Task<HttpClient> RegisteredAsync(AccountKind profile, Domain.Users.PhoneNumber? phone = null)
    {
        var client = app.CreateClient();
        await client.SignInAsync(app.Messages, phone ?? TestData.NewPhone());
        var fields = new Dictionary<string, string>
        {
            ["FullName"] = "Awa Ndiaye",
            ["Profile"] = profile.ToString(),
            ["AcceptsPrivacyPolicy"] = "true",
        };
        if (profile == AccountKind.Business)
        {
            fields["CompanyName"] = "Keur Services";
        }

        Assert.Equal("/", (await client.SubmitAsync("/inscription", "/inscription", fields)).Location());
        return client;
    }
}
