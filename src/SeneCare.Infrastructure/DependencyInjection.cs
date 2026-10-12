using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Infrastructure.Persistence;
using SeneCare.Infrastructure.Persistence.Seed;

namespace SeneCare.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";
    public const string ReadinessTag = "ready";

    /// <summary>Registers infrastructure services: PostgreSQL (EF Core), seeding, health checks.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SeneCareDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString(ConnectionStringName),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<DatabaseSeeder>();
        services.AddSingleton(TimeProvider.System);

        services.AddHealthChecks()
            .AddDbContextCheck<SeneCareDbContext>("database", tags: [ReadinessTag]);

        return services;
    }
}
