using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SeneCare.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Creating migrations needs no database; to apply them, pass
/// <c>--connection "Host=…"</c> or set the <c>SENECARE_DB</c> environment variable.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SeneCareDbContext>
{
    public SeneCareDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SENECARE_DB")
            ?? "Host=localhost;Database=senecare;Username=senecare";

        var options = new DbContextOptionsBuilder<SeneCareDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SeneCareDbContext(options);
    }
}
