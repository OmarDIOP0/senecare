using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SeneCare.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Creating migrations needs no database; to apply them, pass
/// <c>--connection "Server=…"</c> or set the <c>SENECARE_DB</c> environment variable.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SeneCareDbContext>
{
    public SeneCareDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SENECARE_DB")
            ?? "Server=localhost;Database=senecare;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<SeneCareDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SeneCareDbContext(options);
    }
}
