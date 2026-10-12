using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeneCare.Infrastructure.Persistence.Seed;

namespace SeneCare.Infrastructure.Persistence;

public static partial class DatabaseInitializer
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "ConnectionStrings:Default is not set: database migration and seeding skipped (see README).")]
    private static partial void LogMissingConnectionString(ILogger logger);

    /// <summary>
    /// Applies pending migrations and/or seeds reference data, according to
    /// <c>Database:MigrateOnStartup</c> and <c>Database:SeedOnStartup</c> (both on in Development only).
    /// In production, migrations are applied by the deployment pipeline.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var migrate = configuration.GetValue<bool>("Database:MigrateOnStartup");
        var seed = configuration.GetValue<bool>("Database:SeedOnStartup");
        if (!migrate && !seed)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(DependencyInjection.ConnectionStringName)))
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
            LogMissingConnectionString(logger);
            return;
        }

        if (migrate)
        {
            var db = scope.ServiceProvider.GetRequiredService<SeneCareDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (seed)
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
        }
    }
}
