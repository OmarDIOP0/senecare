using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Identity;

public static class SeneCareClaimTypes
{
    /// <summary>« {accountId}|{Household|Business}|{Owner|Manager|SiteManager|Reporter} », one claim per account.</summary>
    public const string Membership = "sc:membership";

    /// <summary>« true » once name + consent are given and the user has an account (or is staff).</summary>
    public const string Registered = "sc:registered";

    public const string Phone = "sc:phone";
}

public sealed record MembershipClaim(Guid AccountId, AccountKind Kind, AccountRole Role)
{
    public override string ToString() => $"{AccountId}|{Kind}|{Role}";

    public static MembershipClaim? Parse(string value)
    {
        var parts = value.Split('|');
        return parts.Length == 3
            && Guid.TryParse(parts[0], out var id)
            && Enum.TryParse<AccountKind>(parts[1], out var kind)
            && Enum.TryParse<AccountRole>(parts[2], out var role)
                ? new MembershipClaim(id, kind, role)
                : null;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static IReadOnlyList<MembershipClaim> GetMemberships(this ClaimsPrincipal principal) =>
        [.. (principal?.FindAll(SeneCareClaimTypes.Membership) ?? [])
            .Select(c => MembershipClaim.Parse(c.Value))
            .OfType<MembershipClaim>()];

    public static bool IsRegistered(this ClaimsPrincipal principal) =>
        principal?.HasClaim(SeneCareClaimTypes.Registered, "true") == true;
}

/// <summary>Authorization policies (CLAUDE.md §2). Views and controllers use these names, never ad-hoc role checks.</summary>
public static class Policies
{
    /// <summary>Signed in, registration possibly incomplete (registration pages only).</summary>
    public const string Authenticated = nameof(Authenticated);

    /// <summary>Signed in with name, consent and an account (or staff). Default policy.</summary>
    public const string Registered = nameof(Registered);

    /// <summary>Individual customer (household account).</summary>
    public const string ClientOnly = nameof(ClientOnly);

    /// <summary>« Gestionnaire » of a business account.</summary>
    public const string OrganizationManager = nameof(OrganizationManager);

    /// <summary>Any member of a business account (manager, site manager, reporter).</summary>
    public const string OrganizationMember = nameof(OrganizationMember);

    public const string Technician = nameof(Technician);

    public const string AdminDispatcher = nameof(AdminDispatcher);

    public const string AdminFinance = nameof(AdminFinance);

    public const string SuperAdmin = nameof(SuperAdmin);

    public static void AddSeneCarePolicies(this AuthorizationOptions options, params string[] authenticationSchemes)
    {
        ArgumentNullException.ThrowIfNull(options);

        AuthorizationPolicyBuilder Base() => new AuthorizationPolicyBuilder(authenticationSchemes).RequireAuthenticatedUser();
        AuthorizationPolicy Registered(Func<AuthorizationHandlerContext, bool>? extra = null) =>
            Base().RequireAssertion(c => c.User.IsRegistered() && (extra?.Invoke(c) ?? true)).Build();

        options.AddPolicy(Authenticated, Base().Build());
        options.AddPolicy(Policies.Registered, Registered());
        options.DefaultPolicy = options.GetPolicy(Policies.Registered)!;

        options.AddPolicy(ClientOnly, Registered(c =>
            c.User.GetMemberships().Any(m => m.Kind == AccountKind.Household)));
        options.AddPolicy(OrganizationManager, Registered(c =>
            c.User.GetMemberships().Any(m => m.Kind == AccountKind.Business && m.Role == AccountRole.Manager)));
        options.AddPolicy(OrganizationMember, Registered(c =>
            c.User.GetMemberships().Any(m => m.Kind == AccountKind.Business)));
        options.AddPolicy(Technician, Registered(c => c.User.IsInRole(nameof(PlatformRole.Technician))));
        options.AddPolicy(AdminDispatcher, Registered(c =>
            c.User.IsInRole(nameof(PlatformRole.Dispatcher)) || c.User.IsInRole(nameof(PlatformRole.SuperAdmin))));
        options.AddPolicy(AdminFinance, Registered(c =>
            c.User.IsInRole(nameof(PlatformRole.Finance)) || c.User.IsInRole(nameof(PlatformRole.SuperAdmin))));
        options.AddPolicy(SuperAdmin, Registered(c => c.User.IsInRole(nameof(PlatformRole.SuperAdmin))));
    }
}
