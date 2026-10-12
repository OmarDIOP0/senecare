using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Abstractions;
using SeneCare.Testing;

namespace SeneCare.Api.IntegrationTests;

/// <summary>The API in a "Testing" environment with test keys; database only when PostgreSQL is configured.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public PostgresFixture Database { get; } = new();

    public CapturingMessageSender Messages { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        var connectionString = PostgresFixture.IsConfigured ? Database.ConnectionString : "Host=localhost;Database=unused";

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in TestData.HostSettings(connectionString))
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISmsSender>(Messages);
                services.AddSingleton<IWhatsAppSender>(Messages);
            });
        });
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
