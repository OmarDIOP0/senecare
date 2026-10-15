using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Accounts;
using SeneCare.Application.Admin;
using SeneCare.Application.Auth;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Application.Development;
using SeneCare.Application.Locations;
using SeneCare.Application.Media;
using SeneCare.Application.Neighborhoods;
using SeneCare.Application.Payments;
using SeneCare.Application.Properties;
using SeneCare.Application.Requests;
using SeneCare.Application.Subscriptions;
using SeneCare.Application.Technicians;
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

        services.AddScoped<Public.GetPublicHomeHandler>();
        services.AddScoped<Public.GetContactHandler>();
        services.AddScoped<Users.Notifier>();
        services.AddScoped<Admin.ReviewModeration>();
        services.AddScoped<RequestOtpHandler>();
        services.AddScoped<VerifyOtpHandler>();
        services.AddScoped<CompleteRegistrationHandler>();
        services.AddScoped<InviteMemberHandler>();
        services.AddScoped<GetInvitationHandler>();
        services.AddScoped<AcceptInvitationHandler>();
        services.AddScoped<GetUserProfileHandler>();
        services.AddScoped<GetTeamHandler>();
        services.AddScoped<NotificationInbox>();
        services.AddScoped<SearchNeighborhoodsHandler>();
        services.AddScoped<ListCommunesHandler>();
        services.AddScoped<SuggestNeighborhoodHandler>();
        services.AddScoped<TourProgressHandler>();

        services.AddScoped<AddPropertyHandler>();
        services.AddScoped<PropertyQueries>();
        services.AddScoped<CreateRequestHandler>();
        services.AddScoped<RequestQueries>();
        services.AddScoped<ClientRequestActions>();
        services.AddScoped<SubscriptionQueries>();
        services.AddScoped<PrepareInvoiceHandler>();
        services.AddScoped<InvoiceDocumentQuery>();
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

        services.AddScoped<TechnicianAccess>();
        services.AddScoped<MissionQueries>();
        services.AddScoped<MissionActions>();
        services.AddScoped<AssignmentHandler>();
        services.AddScoped<DiagnosticFieldHandler>();
        services.AddScoped<PositionSharing>();

        services.AddScoped<AdminDashboardQuery>();
        services.AddScoped<DispatchQueries>();
        services.AddScoped<DispatchActions>();
        services.AddScoped<DiagnosticSupervision>();
        services.AddScoped<CustomerDirectory>();
        services.AddScoped<TechnicianManagement>();
        services.AddScoped<PlanManagement>();
        services.AddScoped<GeographyManagement>();
        services.AddScoped<SettingsManagement>();
        services.AddScoped<NeighborhoodMembership>();
        services.AddScoped<NeighborhoodPrograms>();
        services.AddScoped<AdviceManagement>();
        services.AddScoped<AuditQuery>();
        services.AddScoped<PersonalDataService>();

        services.AddScoped<NeighborhoodCommunity>();
        services.AddScoped<ReferralService>();

        // Development tools (refused by the Web outside Development).
        services.AddScoped<SimulatedActivation>();
        services.AddScoped<RequestSimulator>();
        services.AddScoped<DiagnosticSimulator>();

        return services;
    }
}
