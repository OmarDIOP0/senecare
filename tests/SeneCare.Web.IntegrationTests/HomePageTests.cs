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
    public async Task Health_endpoint_reports_healthy()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("Healthy", body);
    }
}
