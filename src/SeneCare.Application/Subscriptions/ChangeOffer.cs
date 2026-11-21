using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;

namespace SeneCare.Application.Subscriptions;

/// <summary>An offer the customer may switch to (prices from the database, as configured by admins).</summary>
public sealed record OfferChoice(string Code, string Name, Money MonthlyPrice, int BillingPeriodMonths, bool OffersAnnual, int AnnualFreeMonths);

/// <summary>« Changer de formule » on the subscription page: the choices and the change already planned, if any.</summary>
public sealed record OfferChange(
    Guid SubscriptionId,
    IReadOnlyList<OfferChoice> Choices,
    string CurrentCode,
    bool CurrentAnnual,
    string? NextCode,
    string? NextName,
    bool NextAnnual,
    DateOnly? StartsOn);

/// <summary>
/// Change of offer (user decision of 21/11/2026): another plan, or monthly ↔ annual for homes, chosen by the
/// household owner or the company manager. It starts with the next period: nothing is refunded or recomputed
/// on the current one, and companies keep their quarterly diagnostic before that invoice.
/// </summary>
public sealed class ChangeOfferHandler(IApplicationDbContext db, AccessControl access, TimeProvider clock)
{
    /// <summary>Null when the user may not change this subscription (not owner / manager, not active and up to date).</summary>
    public async Task<OfferChange?> OptionsAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await AllowedAsync(userId, subscriptionId, cancellationToken);
        if (subscription is null || subscription.Status != SubscriptionStatus.Active)
        {
            return null;
        }

        var plans = await db.Plans.AsNoTracking()
            .Where(p => p.Segment == subscription.Segment && (p.IsActive || p.Id == subscription.PlanId))
            .ToListAsync(cancellationToken);
        var current = plans.FirstOrDefault(p => p.Id == subscription.PlanId);
        var next = subscription.NextPlanId is { } nextId ? plans.FirstOrDefault(p => p.Id == nextId) : null;
        return new OfferChange(
            subscription.Id,
            [.. plans.Where(p => p.IsActive).OrderBy(p => p.MonthlyPrice)
                .Select(p => new OfferChoice(p.Code, p.Name, p.MonthlyPrice, p.BillingPeriodMonths, p.OffersAnnual, p.AnnualFreeMonths))],
            current?.Code ?? string.Empty,
            subscription.IsAnnual,
            next?.Code,
            next?.Name,
            subscription.NextAnnual,
            await LockedRenewalAsync(subscription, cancellationToken) && subscription.CurrentPeriod is { } period
                ? period.Next(subscription.BillingPeriodMonths).End.AddDays(1)
                : subscription.NextDueDate);
    }

    /// <summary>Plans <paramref name="planCode"/> for the next period; the current offer again withdraws the change.</summary>
    public async Task<Result<Guid>> ScheduleAsync(Guid userId, Guid subscriptionId, string? planCode, bool annual, CancellationToken cancellationToken = default)
    {
        var subscription = await AllowedAsync(userId, subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return Result<Guid>.Failure("subscription.not_found");
        }

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);
        if (plan is null)
        {
            return Result<Guid>.Failure("subscription.plan_unknown");
        }

        try
        {
            subscription.ScheduleChange(plan, annual);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        // The next period's invoice is already out, its period has not started and nothing is paid nor declared on
        // it: it is issued again with the new offer. Otherwise (declared, or the period already running) the change
        // waits for the following period: a period in use is always billed at the offer it was invoiced with.
        if (await OpenRenewalAsync(subscription, cancellationToken) is { } open && Reissuable(open)
            && !await HasPaymentAsync(open, cancellationToken))
        {
            open.Cancel();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(subscription.Id);
    }

    public async Task<Result<Guid>> CancelAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await AllowedAsync(userId, subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return Result<Guid>.Failure("subscription.not_found");
        }

        subscription.CancelScheduledChange();
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(subscription.Id);
    }

    private Task<Invoice?> OpenRenewalAsync(Subscription subscription, CancellationToken cancellationToken) =>
        subscription.NextDueDate is not { } due
            ? Task.FromResult<Invoice?>(null)
            : db.Invoices.FirstOrDefaultAsync(
                i => i.SubscriptionId == subscription.Id && i.Period != null && i.Period.Start == due
                    && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue),
                cancellationToken);

    private async Task<bool> HasPaymentAsync(Invoice invoice, CancellationToken cancellationToken) =>
        invoice.AmountPaid > Money.Zero
        || await db.Payments.AnyAsync(p => p.InvoiceId == invoice.Id && p.Status == PaymentStatus.Pending, cancellationToken);

    private bool Reissuable(Invoice invoice) =>
        invoice.Status == InvoiceStatus.Issued && invoice.Period is { } period && period.Start > DakarCalendar.Today(clock);

    /// <summary>The next period's invoice is out and can no longer change: a change now starts with the period after it.</summary>
    private async Task<bool> LockedRenewalAsync(Subscription subscription, CancellationToken cancellationToken) =>
        await OpenRenewalAsync(subscription, cancellationToken) is { } open && (!Reissuable(open) || await HasPaymentAsync(open, cancellationToken));

    /// <summary>Money decisions: the household owner or a company manager (« Gestionnaire »), never a site manager or an employee.</summary>
    private async Task<Subscription?> AllowedAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var account = (await access.AccountsOfAsync(userId, cancellationToken)).FirstOrDefault(a => a.Id == subscription.AccountId);
        var member = account?.Members.FirstOrDefault(m => m.UserId == userId);
        return member is { Role: AccountRole.Owner or AccountRole.Manager } ? subscription : null;
    }
}
