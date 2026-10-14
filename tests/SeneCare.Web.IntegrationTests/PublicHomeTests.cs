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
        request.Headers.Add("Cookie", "sc_bienvenue=1");

        var response = await client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Replace(' ', ' ').Replace(' ', ' ');

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"fr\">", html, StringComparison.Ordinal);
        Assert.Contains("Du plan à la réalité.", html, StringComparison.Ordinal);
        // Seeded prices, not written in the page.
        Assert.Contains("8 500 FCFA", html, StringComparison.Ordinal);
        Assert.Contains("2 500 000 FCFA", html, StringComparison.Ordinal);
        Assert.Contains("id=\"metiers\"", html, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Finishing_the_welcome_screens_is_remembered_on_the_device()
    {
        var client = app.CreateClient();

        var welcome = await client.GetStringAsync(new Uri("/bienvenue", UriKind.Relative));
        Assert.Contains("Signalez en 30 secondes", WebUtility.HtmlDecode(welcome), StringComparison.Ordinal);

        var finished = await client.SubmitAsync("/bienvenue", "/bienvenue", new Dictionary<string, string>());
        Assert.Equal("/connexion", finished.Location());

        var home = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }
}
