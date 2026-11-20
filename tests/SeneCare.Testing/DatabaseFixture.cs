using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Infrastructure.Persistence.Seed;
using Xunit;

namespace SeneCare.Testing;

/// <summary>
/// Runs against a real local SQL Server. Set <c>SENECARE_TEST_DB</c> to a connection string of a login
/// allowed to create databases (e.g. <c>Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True</c>).
/// Each fixture creates a throw-away database (migrated + seeded) and drops it at the end.
/// Tests are skipped when the variable is not set (Testcontainers can replace this once Docker is available).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
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
        var builder = new SqlConnectionStringBuilder(_adminConnectionString)
        {
            InitialCatalog = "senecare_tests_" + Guid.NewGuid().ToString("N")[..12],
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

        SqlConnection.ClearAllPools();
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    public SeneCareDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SeneCareDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options);
}

/// <summary>A fact that needs the database; skipped with an explanation when it is not configured.</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!DatabaseFixture.IsConfigured)
        {
            Skip = $"Set {DatabaseFixture.EnvironmentVariable} to run database tests.";
        }
    }
}
