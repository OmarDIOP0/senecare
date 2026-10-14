using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Admin;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Neighborhoods;

namespace SeneCare.Application.Subscriptions;

public sealed record InvoiceSummary(Guid Id, string Number, Money Total, InvoiceStatus Status, DateOnly DueDate, DateTimeOffset? PaidAt);

public sealed record SubscriptionSummary(
    Guid Id,
    string PlanName,
    IReadOnlyList<string> PropertyNames,
    SubscriptionStatus Status,
    Money MonthlyPrice,
    Money RegistrationFee,
    int BillingPeriodMonths,
    DateRange? CurrentPeriod,
    DateOnly? NextDueDate,
    IReadOnlyList<InvoiceSummary> Invoices,
    int FreeMonthsPerPeriod = 0,
    Money PeriodPrice = default)
{
    public bool IsAnnual => BillingPeriodMonths == 12;
}

public sealed record NeighborhoodProgress(string Neighborhood, int Members, int Threshold, NeighborhoodProgramStatus Status, int TreesPlanted);

public sealed class SubscriptionQueries(IApplicationDbContext db, AccessControl access)
{
    public async Task<IReadOnlyList<SubscriptionSummary>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var accountIds = (await access.AccountsOfAsync(userId, cancellationToken)).Select(a => a.Id).ToList();
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => accountIds.Contains(s.AccountId) && s.Status != SubscriptionStatus.Cancelled)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        var planIds = subscriptions.Select(s => s.PlanId).Distinct().ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var propertyIds = subscriptions.SelectMany(s => s.CoveredPropertyIds).Distinct().ToList();
        var properties = await db.Properties.AsNoTracking().Where(p => propertyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var subscriptionIds = subscriptions.Select(s => s.Id).ToList();
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.SubscriptionId != null && subscriptionIds.Contains(i.SubscriptionId.Value) && i.Status != InvoiceStatus.Draft)
            .OrderByDescending(i => i.DueDate)
            .ToListAsync(cancellationToken);

        return [.. subscriptions.Select(s => new SubscriptionSummary(
            s.Id,
            plans.GetValueOrDefault(s.PlanId, string.Empty),
            [.. s.CoveredPropertyIds.Select(id => properties.GetValueOrDefault(id, string.Empty))],
            s.Status,
            s.MonthlyPrice,
            s.RegistrationFee,
            s.BillingPeriodMonths,
            s.CurrentPeriod,
            s.NextDueDate,
            [.. invoices.Where(i => i.SubscriptionId == s.Id).Select(i => new InvoiceSummary(i.Id, i.Number, i.Total, i.Status, i.DueDate, i.PaidAt))],
            s.FreeMonthsPerPeriod,
            s.PeriodPrice))];
    }

    /// <summary>« 347 / 500 voisins » for the neighborhood of the user's first home.</summary>
    public async Task<NeighborhoodProgress?> NeighborhoodAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var propertyIds = await access.PropertyIdsOfAsync(userId, cancellationToken);
        var neighborhoodId = await db.Properties.AsNoTracking()
            .Where(p => propertyIds.Contains(p.Id))
            .OrderBy(p => p.CreatedAt)
            .Select(p => (Guid?)p.Address.NeighborhoodId)
            .FirstOrDefaultAsync(cancellationToken);
        if (neighborhoodId is null)
        {
            return null;
        }

        var program = await db.NeighborhoodPrograms.AsNoTracking().FirstOrDefaultAsync(p => p.NeighborhoodId == neighborhoodId, cancellationToken);
        var name = await db.Neighborhoods.Where(n => n.Id == neighborhoodId).Select(n => n.Name).FirstAsync(cancellationToken);
        return program is null
            ? null
            : new NeighborhoodProgress(name, program.MemberCount, program.Threshold, program.Status, program.PublishedTreesPlanted);
    }
}

/// <summary>
/// Development only (the Web hides it elsewhere): activates a pending subscription without payment,
/// so the client journey can be tried before the payment phase.
/// </summary>
public sealed class SimulatedActivation(
    IApplicationDbContext db, AccessControl access, DiagnosticPlanner diagnostics, NeighborhoodMembership neighborhoods, TimeProvider clock)
{
    public async Task<Result<Guid>> ActivateAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        if (subscription is null || !await access.CanAccessAccountAsync(userId, subscription.AccountId, cancellationToken))
        {
            return Result<Guid>.Failure("subscription.not_found");
        }

        try
        {
            subscription.Activate(DakarCalendar.Today(clock), clock.GetUtcNow());
            await diagnostics.EnsurePlannedAsync(subscription, cancellationToken);
            await neighborhoods.RefreshForPropertiesAsync([.. subscription.CoveredPropertyIds], cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(subscription.Id);
    }
}
