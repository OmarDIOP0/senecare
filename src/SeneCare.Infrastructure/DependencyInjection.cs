using Microsoft.Extensions.DependencyInjection;

namespace SeneCare.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure services (EF Core, SMS, payments… added in later phases).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        return services;
    }
}
