using System.Net;
using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>The public home reads offers and figures from the database.</summary>
[Collection(WebAppTestGroup.Name)]
public class PublicHomeTests(WebAppFixture app)
{
    [PostgresFact]
    public async Task Home_page_renders_in_french_with_the_offers_from_the_database()
    {
        var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");

        var response = await client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Replace(' ', ' ').Replace(' ', ' ');

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"fr\">", html, StringComparison.Ordinal);
        Assert.Contains("Du plan à la réalité.", html, StringComparison.Ordinal);
        Assert.Contains("id=\"metiers\"", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/demander\"", html, StringComparison.Ordinal);

        // Prices live on /tarifs and come from the seeded plans, not from the page.
        var pricing = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/tarifs", UriKind.Relative))).Replace(' ', ' ').Replace(' ', ' ');
        Assert.Contains("8 500 FCFA", pricing, StringComparison.Ordinal);
        Assert.Contains("2 500 000 FCFA", pricing, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Every_public_page_renders()
    {
        var client = app.CreateClient();
        foreach (var path in new[] { "/metiers", "/metiers/plomberie", "/particuliers", "/entreprises", "/quartiers", "/tarifs", "/faq", "/mentions-legales", "/cgu", "/confidentialite" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} → {response.StatusCode}");
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/metiers/inconnu", UriKind.Relative))).StatusCode);
    }

    [PostgresFact]
    public async Task The_quick_request_form_leads_to_sign_in_then_the_report_form()
    {
        var response = await app.CreateClient().GetAsync(new Uri("/demander?metier=Plumbing&telephone=77%20123%2045%2067", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Location();
        Assert.StartsWith("/connexion?returnUrl=", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("/signaler?domaine=Plumbing"), location, StringComparison.Ordinal);
        Assert.Contains("phone=77%20123%2045%2067", location, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task A_first_visit_lands_on_the_home_page_not_on_the_welcome_screens()
    {
        var home = await app.CreateClient().GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Créer mon compte", WebUtility.HtmlDecode(await home.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task The_welcome_screens_lead_on_to_where_the_new_customer_was_going()
    {
        var client = app.CreateClient();
        var page = "/bienvenue?suite=" + Uri.EscapeDataString("/abonnement");

        var welcome = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri(page, UriKind.Relative)));
        Assert.Contains("Signalez en 30 secondes", welcome, StringComparison.Ordinal);
        Assert.Contains("name=\"suite\" value=\"/abonnement\"", welcome, StringComparison.Ordinal);

        var skipped = await client.SubmitAsync(page, "/bienvenue", new Dictionary<string, string> { ["suite"] = "/abonnement" });
        Assert.Equal("/abonnement", skipped.Location());

        // Never towards another site.
        var outside = await client.SubmitAsync("/bienvenue?suite=%2F%2Fexemple.com", "/bienvenue", new Dictionary<string, string> { ["suite"] = "//exemple.com" });
        Assert.Equal("/", outside.Location());
    }
}
