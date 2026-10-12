using SeneCare.Domain.Common;

namespace SeneCare.Domain.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Mermoz-Sacré-Cœur", "mermoz-sacre-coeur")]
    [InlineData("Patte d'Oie", "patte-d-oie")]
    [InlineData("  Thiès Est ", "thies-est")]
    [InlineData("Guédiawaye", "guediawaye")]
    public void Builds_url_friendly_keys(string input, string expected) => Assert.Equal(expected, Slug.From(input));
}
