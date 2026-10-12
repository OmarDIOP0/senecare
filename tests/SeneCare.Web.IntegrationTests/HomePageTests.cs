using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SeneCare.Web.IntegrationTests;

public class HomePageTests(SeneCareWebFactory factory) : IClassFixture<SeneCareWebFactory>
{
    [Fact]
    public async Task First_visit_goes_to_the_welcome_screens()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/bienvenue", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Home_page_renders_in_french_once_welcomed()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "sc_bienvenue=1");

        var response = await client.SendAsync(request);
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
