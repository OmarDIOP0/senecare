using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Requests;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Business;

public sealed record SlaStats(int Measured, int Respected, int LateNow)
{
    /// <summary>Share of requests reached in time, in percent; null before the first measured request.</summary>
    public int? Rate => Measured == 0 ? null : (int)Math.Round(100.0 * Respected / Measured);
}

public sealed record DiagnosticSlot(Guid Id, DateTimeOffset ScheduledFor, DiagnosticStatus Status, DateRange Period, int Anomalies);

public sealed record SubscriptionState(Guid Id, PlanOffer Plan, SubscriptionStatus Status, DateRange? CurrentPeriod, DateOnly? NextDueDate);

public sealed record BusinessDashboard(
    string AccountName,
    AccountRole Role,
    bool RequiresApproval,
    int SiteCount,
    IReadOnlyDictionary<Urgency, int> OpenByUrgency,
    SlaStats Sla,
    DiagnosticSlot? NextDiagnostic,
    DiagnosticSlot? LastDiagnostic,
    Money? SpentThisYear,
    SubscriptionState? Subscription,
    IReadOnlyList<RequestSummary> PendingApprovals,
    IReadOnlyList<RequestSummary> OpenRequests,
    int TeamSize = 1,
    int RequestCount = 0)
{
    public int OpenTotal => OpenByUrgency.Values.Sum();

    /// <summary>Start-up checklist: site, team, plan, first report.</summary>
    public int StepsDone => (SiteCount > 0 ? 1 : 0) + (TeamSize > 1 ? 1 : 0) + (Subscription is not null ? 1 : 0) + (RequestCount > 0 ? 1 : 0);
}

/// <summary>
/// Business home: what a manager checks every morning. Each figure is limited to the sites the member
/// can see; money (spending, prices) only for the manager.
/// </summary>
public sealed class BusinessDashboardQuery(
    IApplicationDbContext db,
    BusinessAccess access,
    PlanCatalog catalog,
    RequestQueries requests,
    TimeProvider clock)
{
    /// <summary>SLA compliance is measured on the last 90 days.</summary>
    public static readonly TimeSpan SlaWindow = TimeSpan.FromDays(90);

    private static readonly RequestStatus[] Closed = [RequestStatus.ValidatedByCustomer, RequestStatus.Cancelled];

    public async Task<BusinessDashboard?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var siteIds = await access.SiteIdsAsync(member, cancellationToken);

        var open = await db.ServiceRequests.AsNoTracking()
            .Where(r => siteIds.Contains(r.PropertyId) && !Closed.Contains(r.Status))
            .OrderByDescending(r => r.Urgency == Urgency.Critical)
            .ThenBy(r => r.SlaDeadline ?? DateTimeOffset.MaxValue)
            .ToListAsync(cancellationToken);
        var openByUrgency = Enum.GetValues<Urgency>().ToDictionary(u => u, u => open.Count(r => r.Urgency == u));

        var since = now - SlaWindow;
        var measured = await db.ServiceRequests.AsNoTracking()
            .Where(r => siteIds.Contains(r.PropertyId) && r.SlaDeadline != null && r.CreatedAt >= since
                && (r.RespondedAt != null || r.SlaDeadline < now)
                && r.Status != RequestStatus.Cancelled)
            .Select(r => new { r.SlaDeadline, r.RespondedAt })
            .ToListAsync(cancellationToken);
        var sla = new SlaStats(
            measured.Count,
            measured.Count(r => r.RespondedAt is { } at && at <= r.SlaDeadline),
            open.Count(r => r.RespondedAt is null && r.SlaDeadline < now));

        var diagnostics = await db.Diagnostics.AsNoTracking()
            .Where(d => d.AccountId == member.Account.Id && d.Status != DiagnosticStatus.Cancelled)
            .OrderBy(d => d.ScheduledFor)
            .ToListAsync(cancellationToken);
        var next = diagnostics.FirstOrDefault(d => d.Status is DiagnosticStatus.Planned or DiagnosticStatus.InProgress or DiagnosticStatus.Submitted);
        var last = diagnostics.LastOrDefault(d => d.Status == DiagnosticStatus.Validated);

        var subscription = await access.SubscriptionAsync(member.Account.Id, cancellationToken);
        var plan = subscription is null ? null : await catalog.OfSubscriptionAsync(subscription, cancellationToken);

        Money? spent = null;
        if (member.IsManager)
        {
            var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var paid = await db.Invoices.AsNoTracking()
                .Where(i => i.AccountId == member.Account.Id && i.Status == InvoiceStatus.Paid && i.PaidAt >= yearStart)
                .Select(i => i.AmountPaid)
                .ToListAsync(cancellationToken);
            spent = paid.Aggregate(Money.Zero, (sum, amount) => sum + amount);
        }

        var pending = member.CanApprove
            ? open.Where(r => r.Status == RequestStatus.AwaitingApproval && member.CanAccess(r.PropertyId)).ToList()
            : [];

        return new BusinessDashboard(
            member.Account.DisplayName,
            member.Member.Role,
            member.Account.RequiresRequestApproval,
            siteIds.Count,
            openByUrgency,
            sla,
            next is null ? null : Slot(next),
            last is null ? null : Slot(last),
            spent,
            subscription is null || plan is null
                ? null
                : new SubscriptionState(subscription.Id, plan, subscription.Status, subscription.CurrentPeriod, subscription.NextDueDate),
            await requests.SummariesAsync(pending, cancellationToken),
            await requests.SummariesAsync([.. open.Where(r => r.Status != RequestStatus.AwaitingApproval).Take(10)], cancellationToken),
            member.Account.Members.Count,
            await db.ServiceRequests.CountAsync(r => siteIds.Contains(r.PropertyId), cancellationToken));
    }

    private static DiagnosticSlot Slot(Diagnostic d) => new(d.Id, d.ScheduledFor, d.Status, d.PeriodCovered, d.AnomalyCount);
}
