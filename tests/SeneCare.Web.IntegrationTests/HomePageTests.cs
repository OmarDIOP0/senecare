using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SeneCare.Web.IntegrationTests;

public class HomePageTests(SeneCareWebFactory factory) : IClassFixture<SeneCareWebFactory>
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("Healthy", body);
    }
}
