using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SeneCare.Web.IntegrationTests;

public class HomePageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Home_page_renders_in_french_by_default()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"fr\">", html, StringComparison.Ordinal);
        Assert.Contains("Du plan à la réalité.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("Healthy", body);
    }
}
