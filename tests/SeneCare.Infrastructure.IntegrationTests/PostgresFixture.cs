using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Infrastructure.Persistence.Seed;

namespace SeneCare.Infrastructure.IntegrationTests;

/// <summary>
/// Runs against a real local PostgreSQL. Set <c>SENECARE_TEST_DB</c> to a connection string of a user
/// allowed to create databases (e.g. <c>Host=localhost;Username=postgres;Password=…</c>).
/// Each test run creates a throw-away database and drops it at the end.
/// Tests are skipped when the variable is not set (Testcontainers can replace this once Docker is available).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "SENECARE_TEST_DB";

    private string? _adminConnectionString;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!IsConfigured)
        {
            return;
        }

        _adminConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable)!;
        var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = "senecare_tests_" + Guid.NewGuid().ToString("N")[..12],
        };
        ConnectionString = builder.ConnectionString;

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await new DatabaseSeeder(db, TimeProvider.System, NullLogger<DatabaseSeeder>.Instance).SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_adminConnectionString is null)
        {
            return;
        }

        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    public SeneCareDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SeneCareDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>A fact that needs PostgreSQL; skipped with an explanation when it is not configured.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresFixture.IsConfigured)
        {
            Skip = $"Set {PostgresFixture.EnvironmentVariable} to run database tests.";
        }
    }
}
