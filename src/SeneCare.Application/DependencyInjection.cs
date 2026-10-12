using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Accounts;
using SeneCare.Application.Auth;
using SeneCare.Application.Users;

namespace SeneCare.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers (plain classes, no mediator).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<VerifyOtpHandler>();
        services.AddScoped<CompleteRegistrationHandler>();
        services.AddScoped<InviteMemberHandler>();
        services.AddScoped<GetInvitationHandler>();
        services.AddScoped<AcceptInvitationHandler>();
        services.AddScoped<GetUserProfileHandler>();
        services.AddScoped<GetTeamHandler>();

        return services;
    }
}
