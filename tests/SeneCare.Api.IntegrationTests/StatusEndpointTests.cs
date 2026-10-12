using System.Net;

namespace SeneCare.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public class StatusEndpointTests(ApiFixture api)
{
    [Fact]
    public async Task V1_status_endpoint_responds()
    {
        var client = api.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = api.Factory.CreateClient();

        var body = await client.GetStringAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        var client = api.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
