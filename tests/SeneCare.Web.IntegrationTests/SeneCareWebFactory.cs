using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SeneCare.Web.IntegrationTests;

/// <summary>Runs the site in a "Testing" environment: no database migration or seeding at startup.</summary>
public sealed class SeneCareWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
