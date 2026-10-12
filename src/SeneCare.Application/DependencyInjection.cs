using Microsoft.Extensions.DependencyInjection;

namespace SeneCare.Application;

public static class DependencyInjection
{
    /// <summary>Registers application use cases and validators (added in later phases).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
