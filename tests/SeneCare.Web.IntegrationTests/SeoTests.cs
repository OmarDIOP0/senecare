using SeneCare.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Search engines: robots.txt, sitemap.xml, one description and canonical address per public page, no indexing of private pages.</summary>
[Collection(WebAppTestGroup.Name)]
public sealed class SeoTests(WebAppFixture app)
{
    [DatabaseFact]
    public async Task Public_pages_are_described_and_private_ones_kept_out_of_search()
    {
        var client = app.CreateClient();

        var robots = await client.GetStringAsync(new Uri("/robots.txt", UriKind.Relative));
        Assert.Contains("Disallow: /admin", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /entreprise/", robots, StringComparison.Ordinal);
        Assert.DoesNotContain("Disallow: /entreprises", robots, StringComparison.Ordinal);
        Assert.Contains("Sitemap: https://senecare.net/sitemap.xml", robots, StringComparison.Ordinal);

        var sitemap = await client.GetStringAsync(new Uri("/sitemap.xml", UriKind.Relative));
        Assert.Contains("<loc>https://senecare.net/tarifs</loc>", sitemap, StringComparison.Ordinal);
        Assert.Contains("<loc>https://senecare.net/metiers/", sitemap, StringComparison.Ordinal);
        Assert.DoesNotContain("/admin", sitemap, StringComparison.Ordinal);

        var home = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        Assert.Contains("<link rel=\"canonical\" href=\"https://senecare.net/\" />", home, StringComparison.Ordinal);
        Assert.Contains("og:image\" content=\"https://senecare.net/images/partage.jpg\"", home, StringComparison.Ordinal);
        Assert.Contains("\"@type\":\"HomeAndConstructionBusiness\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("noindex", home, StringComparison.Ordinal);

        // Each page its own description.
        var pricing = await client.GetStringAsync(new Uri("/tarifs", UriKind.Relative));
        var description = Description(home);
        Assert.NotEqual(description, Description(pricing));

        var signIn = await client.GetStringAsync(new Uri("/connexion", UriKind.Relative));
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\" />", signIn, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"canonical\"", signIn, StringComparison.Ordinal);
    }

    private static string Description(string html)
    {
        var start = html.IndexOf("<meta name=\"description\" content=\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        return html[start..html.IndexOf("/>", start, StringComparison.Ordinal)];
    }
}
