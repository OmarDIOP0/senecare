using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SeneCare.Api.IntegrationTests;

public class StatusEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task V1_status_endpoint_responds()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("Healthy", body);
    }
}
