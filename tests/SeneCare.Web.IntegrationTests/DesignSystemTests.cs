using System.Globalization;
using System.Net;
using SeneCare.Domain.Requests;
using SeneCare.Testing;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.IntegrationTests;

public class FormattingTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    [Fact]
    public void Amounts_use_french_grouping_and_a_no_break_space_before_fcfa()
    {
        using var _ = new CultureScope(French);

        var text = Formatting.Fcfa(1_500_000);

        Assert.Equal("1 500 000 FCFA", text.Replace(' ', ' ').Replace(' ', ' '));
        Assert.EndsWith(" FCFA", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", text, StringComparison.Ordinal); // never a breakable space
    }

    [Fact]
    public void Dates_are_short_french_in_dakar_time()
    {
        using var _ = new CultureScope(French);

        Assert.Equal("lun. 12 oct.", Formatting.ShortDate(new DateTimeOffset(2026, 10, 12, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal("lun. 12 oct. · 09:30", Formatting.ShortDateTime(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Every_status_has_a_badge_colour()
    {
        Assert.All(Enum.GetValues<RequestStatus>(), s => Assert.False(string.IsNullOrEmpty(Formatting.BadgeClass(s))));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(CultureInfo culture) => CultureInfo.CurrentCulture = culture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}

public class DesignPageTests(SeneCareWebFactory factory) : IClassFixture<SeneCareWebFactory>
{
    [Fact]
    public async Task Design_page_is_not_available_outside_development()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/design", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/img/icons.svg")]
    [InlineData("/img/motifs/wax.svg")]
    [InlineData("/img/motifs/manjak.svg")]
    [InlineData("/img/motifs/bogolan.svg")]
    [InlineData("/fonts/inter-latin-400-normal.woff2")]
    [InlineData("/fonts/inter-latin-700-normal.woff2")]
    [InlineData("/lib/htmx.min.js")]
    [InlineData("/js/app.js")]
    public async Task Design_assets_are_self_hosted(string path)
    {
        var response = await factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Motifs_stay_under_3_kilobytes()
    {
        foreach (var motif in new[] { "wax", "manjak", "bogolan" })
        {
            var bytes = await factory.CreateClient().GetByteArrayAsync(new Uri($"/img/motifs/{motif}.svg", UriKind.Relative));
            Assert.True(bytes.Length < 3 * 1024, $"{motif}.svg is {bytes.Length} bytes");
        }
    }
}

[Collection(WebAppTestGroup.Name)]
public class NeighborhoodSearchTests(WebAppFixture app)
{
    [PostgresFact]
    public async Task Search_ignores_accents_and_case()
    {
        var html = WebUtility.HtmlDecode(await app.CreateClient().GetStringAsync(new Uri("/quartiers/recherche?q=SACRE%20coeur&field=q1", UriKind.Relative)));

        Assert.Contains("Sacré-Cœur", html, StringComparison.Ordinal);
        Assert.Contains("data-pick=\"q1\"", html, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Unknown_neighborhood_says_so()
    {
        var html = WebUtility.HtmlDecode(await app.CreateClient().GetStringAsync(new Uri("/quartiers/recherche?q=zzzz&field=q1", UriKind.Relative)));

        Assert.Contains("Aucun quartier trouvé", html, StringComparison.Ordinal);
    }
}
