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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bootstrap:SuperAdminPhones is empty: no administrator number is configured (set Bootstrap__SuperAdminPhones).")]
    private static partial void LogNoAdminConfigured(ILogger logger);

    /// <summary>
    /// Applies pending migrations and/or seeds reference data, according to
    /// <c>Database:MigrateOnStartup</c> and <c>Database:SeedOnStartup</c> (both on in Development only).
    /// In production, migrations are applied by the deployment pipeline.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var migrate = configuration.GetValue<bool>("Database:MigrateOnStartup");
        var seed = configuration.GetValue<bool>("Database:SeedOnStartup");
        var bootstrapAdmins = !string.IsNullOrWhiteSpace(configuration[AdminBootstrapper.ConfigurationKey]);
        await using var scope = services.CreateAsyncScope();
        if (!bootstrapAdmins)
        {
            // Visible in the host's logs (Render…): the usual reason why « my admin number » goes to the sign-up.
            LogNoAdminConfigured(scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer)));
        }

        if (!migrate && !seed && !bootstrapAdmins)
        {
            return;
        }

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

        // Also in production: the first administrators come from configuration, not from the database.
        if (bootstrapAdmins)
        {
            await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync(cancellationToken);
        }
    }
}
