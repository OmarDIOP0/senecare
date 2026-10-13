using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Plans;

namespace SeneCare.Application.Business;

/// <summary>The signed-in user's business account and what their sub-role allows.</summary>
public sealed record BusinessMember(CustomerAccount Account, AccountMember Member)
{
    /// <summary>« Gestionnaire »: subscription, money, team, every site.</summary>
    public bool IsManager => Member.Role == AccountRole.Manager;

    /// <summary>Managers and site managers approve reporters' requests (site managers on their sites).</summary>
    public bool CanApprove => Member.Role is AccountRole.Manager or AccountRole.SiteManager;

    public bool CanAccess(Guid propertyId) => Member.CanAccess(propertyId);
}

/// <summary>
/// Resolves the business membership. Every business use case starts here, then checks the sub-role:
/// rights live in one place, never in views.
/// </summary>
public sealed class BusinessAccess(IApplicationDbContext db)
{
    public async Task<BusinessMember?> OfAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await db.CustomerAccounts
            .Where(a => a.Kind == AccountKind.Business && a.Members.Any(m => m.UserId == userId))
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return account is null ? null : new BusinessMember(account, account.FindMember(userId)!);
    }

    /// <summary>Sites of the account the member can see (all for managers, their scope otherwise).</summary>
    public async Task<IReadOnlyList<Guid>> SiteIdsAsync(BusinessMember member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        var all = await db.Properties.Where(p => p.AccountId == member.Account.Id).Select(p => p.Id).ToListAsync(cancellationToken);
        return member.Member.HasFullAccess ? all : [.. all.Where(member.CanAccess)];
    }

    /// <summary>The account's live subscription (not cancelled), if any.</summary>
    public Task<Subscription?> SubscriptionAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        db.Subscriptions
            .Where(s => s.AccountId == accountId && s.Status != SubscriptionStatus.Cancelled)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed record PlanOffer(
    Guid Id,
    string Code,
    string Name,
    Money RegistrationFee,
    Money MonthlyPrice,
    int BillingPeriodMonths,
    int Priority,
    int? IncludedVisitsPerPeriod,
    IReadOnlyList<PlanSlaPolicy> Sla,
    IReadOnlyList<ServiceDomain> Domains,
    bool DiagnosticRequired)
{
    /// <summary>Registration fee + first period: what is paid to start.</summary>
    public Money FirstPayment => RegistrationFee + (MonthlyPrice * BillingPeriodMonths);

    public TimeSpan? DelayFor(Urgency urgency) => Sla.FirstOrDefault(s => s.Urgency == urgency)?.MaxDelay;

    public static PlanOffer From(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new PlanOffer(
            plan.Id, plan.Code, plan.Name, plan.RegistrationFee, plan.MonthlyPrice, plan.BillingPeriodMonths,
            plan.Priority, plan.IncludedVisitsPerPeriod,
            [.. plan.SlaPolicies.OrderBy(p => p.MaxDelay)], [.. plan.IncludedDomains.Order()], plan.DiagnosticRequired);
    }
}

/// <summary>Business offers as configured by admins (prices, delays, visits): nothing is hard-coded.</summary>
public sealed class PlanCatalog(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<PlanOffer>> BusinessOffersAsync(CancellationToken cancellationToken = default)
    {
        var plans = await db.Plans.AsNoTracking()
            .Where(p => p.Segment == AccountKind.Business && p.IsActive)
            .ToListAsync(cancellationToken);
        return [.. plans.OrderBy(p => p.MonthlyPrice).Select(PlanOffer.From)];
    }

    public async Task<PlanOffer?> OfSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken);
        return plan is null ? null : PlanOffer.From(plan);
    }
}

/// <summary>
/// The manager picks Essentiel / Professionnel / Premium. The subscription covers every site and waits
/// for the registration fee + first quarter (same payment flow as households).
/// </summary>
public sealed class ChoosePlanHandler(IApplicationDbContext db, BusinessAccess access, TimeProvider clock)
{
    public async Task<Result<Guid>> HandleAsync(Guid userId, string? planCode, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        if (member is not { IsManager: true })
        {
            return Result<Guid>.Failure("business.manager_only");
        }

        if (await access.SubscriptionAsync(member.Account.Id, cancellationToken) is not null)
        {
            return Result<Guid>.Failure("business.already_subscribed");
        }

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == planCode && p.Segment == AccountKind.Business, cancellationToken);
        if (plan is null)
        {
            return Result<Guid>.Failure("business.plan_unknown");
        }

        var sites = await db.Properties.Where(p => p.AccountId == member.Account.Id).Select(p => p.Id).ToListAsync(cancellationToken);
        if (sites.Count == 0)
        {
            return Result<Guid>.Failure("business.no_site");
        }

        Subscription subscription;
        try
        {
            subscription = Subscription.Create(member.Account, plan, sites, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(subscription.Id);
    }
}
