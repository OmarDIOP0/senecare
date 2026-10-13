using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Accounts;
using SeneCare.Application.Auth;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Application.Development;
using SeneCare.Application.Locations;
using SeneCare.Application.Media;
using SeneCare.Application.Payments;
using SeneCare.Application.Properties;
using SeneCare.Application.Requests;
using SeneCare.Application.Subscriptions;
using SeneCare.Application.Users;

namespace SeneCare.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers (plain classes, no mediator).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AccessControl>();
        services.AddScoped<Settings>();
        services.AddScoped<MediaUploader>();
        services.AddScoped<MediaAccess>();

        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<VerifyOtpHandler>();
        services.AddScoped<CompleteRegistrationHandler>();
        services.AddScoped<InviteMemberHandler>();
        services.AddScoped<GetInvitationHandler>();
        services.AddScoped<AcceptInvitationHandler>();
        services.AddScoped<GetUserProfileHandler>();
        services.AddScoped<GetTeamHandler>();
        services.AddScoped<SearchNeighborhoodsHandler>();
        services.AddScoped<TourProgressHandler>();

        services.AddScoped<AddPropertyHandler>();
        services.AddScoped<PropertyQueries>();
        services.AddScoped<CreateRequestHandler>();
        services.AddScoped<RequestQueries>();
        services.AddScoped<ClientRequestActions>();
        services.AddScoped<SubscriptionQueries>();
        services.AddScoped<PrepareInvoiceHandler>();
        services.AddScoped<DeclarePaymentHandler>();
        services.AddScoped<PaymentVerificationHandler>();

        services.AddScoped<BusinessAccess>();
        services.AddScoped<PlanCatalog>();
        services.AddScoped<ChoosePlanHandler>();
        services.AddScoped<BusinessDashboardQuery>();
        services.AddScoped<RequestApprovalHandler>();
        services.AddScoped<TeamManagementHandler>();
        services.AddScoped<DiagnosticPlanner>();
        services.AddScoped<DiagnosticQueries>();
        services.AddScoped<ConvertAnomalyHandler>();
        services.AddScoped<BusinessExports>();

        // Development tools (refused by the Web outside Development).
        services.AddScoped<SimulatedActivation>();
        services.AddScoped<RequestSimulator>();
        services.AddScoped<DiagnosticSimulator>();

        return services;
    }
}
