using System.Security.Claims;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Identity;

namespace SeneCare.Web.Infrastructure;

/// <param name="Tour">Value of <c>data-tour</c> so the guided tour can point at it.</param>
public sealed record NavItem(string Key, string Url, string Icon, string LabelKey, string? Tour = null);

/// <summary>Navigation of the signed-in space: tabs + optional central action, and the matching guided tour.</summary>
public sealed record AppNav(IReadOnlyList<NavItem> Items, NavItem? Primary, string TourKey);

public static class AppNavigation
{
    private static readonly NavItem Report = new("signaler", "/signaler", "plus", "Tab_Report", "signaler");
    private static readonly NavItem Profile = new("profil", "/compte", "circle-user", "Tab_Profile", "profil");

    /// <summary>
    /// One navigation per kind of user. Priority when someone has several roles:
    /// admin &gt; technician &gt; business &gt; household.
    /// </summary>
    public static AppNav For(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (IsAdmin(user))
        {
            List<NavItem> items =
            [
                new("espace", "/admin", "house", "Tab_Dashboard", "tableau-de-bord"),
            ];
            if (user.IsInRole(nameof(PlatformRole.Dispatcher)) || user.IsInRole(nameof(PlatformRole.SuperAdmin)))
            {
                items.Add(new("dispatch", "/admin/dispatch", "clipboard-list", "Tab_Dispatch", "dispatch"));
            }

            if (user.IsInRole(nameof(PlatformRole.Finance)) || user.IsInRole(nameof(PlatformRole.SuperAdmin)))
            {
                items.Add(new("paiements", "/admin/paiements", "wallet", "Tab_Payments"));
            }

            items.Add(new("gestion", "/admin/gestion", "building-2", "Tab_Management"));
            items.Add(Profile);
            return new AppNav(items, null, TourKeys.Admin);
        }

        if (user.IsInRole(nameof(PlatformRole.Technician)))
        {
            return new AppNav(
                [
                    new("espace", "/technicien", "truck", "Tab_Missions", "missions"),
                    new("rapports", "/technicien/rapports", "receipt", "Tab_Reports", "rapports"),
                    Profile,
                ],
                null,
                TourKeys.Technician);
        }

        if (IsBusiness(user))
        {
            // Money and team are the manager's; site managers and employees follow their requests instead.
            return new AppNav(
                IsBusinessManager(user)
                    ?
                    [
                        new("espace", "/espace", "house", "Tab_Home"),
                        new("sites", "/entreprise/sites", "building-2", "Tab_Sites", "sites"),
                        new("equipe", "/entreprise/equipe", "users", "Tab_Team", "equipe"),
                        new("factures", "/entreprise/factures", "receipt", "Tab_Invoices", "factures"),
                    ]
                    :
                    [
                        new("espace", "/espace", "house", "Tab_Home"),
                        new("sites", "/entreprise/sites", "building-2", "Tab_Sites", "sites"),
                        new("interventions", "/interventions", "clipboard-list", "Tab_Requests", "suivi"),
                        Profile,
                    ],
                Report,
                TourKeys.Business);
        }

        return new AppNav(
            [
                new("espace", "/espace", "house", "Tab_Home"),
                new("interventions", "/interventions", "clipboard-list", "Tab_Interventions", "suivi"),
                new("logements", "/logements", "building-2", "Tab_Property", "logement"),
                Profile,
            ],
            Report,
            TourKeys.Client);
    }

    public static bool IsBusiness(ClaimsPrincipal user) =>
        user.GetMemberships().Any(m => m.Kind == AccountKind.Business);

    public static bool IsBusinessManager(ClaimsPrincipal user) =>
        user.GetMemberships().Any(m => m.Kind == AccountKind.Business && m.Role == AccountRole.Manager);

    private static bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole(nameof(PlatformRole.SuperAdmin))
        || user.IsInRole(nameof(PlatformRole.Dispatcher))
        || user.IsInRole(nameof(PlatformRole.Finance))
        || user.IsInRole(nameof(PlatformRole.Support));
}

/// <summary>Coach-mark steps of each guided tour: <c>data-tour</c> target + resource keys for title and text.</summary>
public static class TourDefinitions
{
    public sealed record Step(string Target, string TitleKey, string TextKey);

    public static IReadOnlyList<Step> For(string tourKey) => tourKey switch
    {
        TourKeys.Client =>
        [
            new("signaler", "Tour_Client_Report_Title", "Tour_Client_Report_Text"),
            new("suivi", "Tour_Client_Track_Title", "Tour_Client_Track_Text"),
            new("abonnement", "Tour_Client_Subscription_Title", "Tour_Client_Subscription_Text"),
            new("logement", "Tour_Client_Property_Title", "Tour_Client_Property_Text"),
        ],
        TourKeys.Business =>
        [
            new("sites", "Tour_Business_Sites_Title", "Tour_Business_Sites_Text"),
            new("equipe", "Tour_Business_Team_Title", "Tour_Business_Team_Text"),
            new("diagnostics", "Tour_Business_Diagnostics_Title", "Tour_Business_Diagnostics_Text"),
            new("factures", "Tour_Business_Invoices_Title", "Tour_Business_Invoices_Text"),
        ],
        TourKeys.Technician =>
        [
            new("missions", "Tour_Tech_Missions_Title", "Tour_Tech_Missions_Text"),
            new("demarrer", "Tour_Tech_Start_Title", "Tour_Tech_Start_Text"),
            new("rapports", "Tour_Tech_Report_Title", "Tour_Tech_Report_Text"),
        ],
        TourKeys.Admin =>
        [
            new("tableau-de-bord", "Tour_Admin_Dashboard_Title", "Tour_Admin_Dashboard_Text"),
            new("dispatch", "Tour_Admin_Dispatch_Title", "Tour_Admin_Dispatch_Text"),
        ],
        _ => [],
    };
}
