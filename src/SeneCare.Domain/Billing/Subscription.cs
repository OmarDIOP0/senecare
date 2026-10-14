using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Plans;

namespace SeneCare.Domain.Billing;

public enum SubscriptionStatus
{
    /// <summary>Created, waiting for registration fee + first period payment.</summary>
    PendingRegistration = 1,
    Active = 2,

    /// <summary>Due date passed without payment; service continues during the grace period.</summary>
    PastDue = 3,

    /// <summary>Grace period exhausted; no new requests until payment.</summary>
    Suspended = 4,
    Cancelled = 5,
}

public sealed record SubscriptionActivated(Guid SubscriptionId, Guid AccountId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SubscriptionSuspended(Guid SubscriptionId, Guid AccountId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Subscription of an account to a plan. Households: one subscription per home.
/// Businesses: one subscription for all sites, billed quarterly after a validated diagnostic.
/// Prices are snapshotted at creation so later plan changes don't affect it.
/// </summary>
public sealed class Subscription : AggregateRoot
{
    private readonly List<Guid> _coveredPropertyIds = [];

    private Subscription()
    {
    }

    private Subscription(CustomerAccount account, Plan plan, IReadOnlyCollection<Guid> propertyIds, bool annual, DateTimeOffset now)
    {
        AccountId = account.Id;
        PlanId = plan.Id;
        Segment = account.Kind;
        RegistrationFee = plan.RegistrationFee;
        MonthlyPrice = plan.MonthlyPrice;
        BillingPeriodMonths = annual ? 12 : plan.BillingPeriodMonths;
        FreeMonthsPerPeriod = annual ? plan.AnnualFreeMonths : 0;
        DiagnosticRequired = plan.DiagnosticRequired;
        Status = SubscriptionStatus.PendingRegistration;
        CreatedAt = now;
        _coveredPropertyIds.AddRange(propertyIds);
    }

    public Guid AccountId { get; private set; }

    public Guid PlanId { get; private set; }

    public AccountKind Segment { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public Money RegistrationFee { get; private set; }

    public Money MonthlyPrice { get; private set; }

    public int BillingPeriodMonths { get; private set; }

    /// <summary>Months not charged in each period: « 2 mois offerts » on a yearly payment.</summary>
    public int FreeMonthsPerPeriod { get; private set; }

    public bool IsAnnual => BillingPeriodMonths == 12 && Segment == AccountKind.Household;

    /// <summary>Months actually charged per period.</summary>
    public int ChargedMonths => BillingPeriodMonths - FreeMonthsPerPeriod;

    public bool DiagnosticRequired { get; private set; }

    public DateRange? CurrentPeriod { get; private set; }

    /// <summary>Day the next period must be paid (first day of the next period).</summary>
    public DateOnly? NextDueDate => CurrentPeriod?.End.AddDays(1);

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<Guid> CoveredPropertyIds => _coveredPropertyIds.AsReadOnly();

    public Money PeriodPrice => MonthlyPrice * ChargedMonths;

    /// <summary>Requests can be submitted while active or within the grace period.</summary>
    public bool AllowsRequests => Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    /// <param name="annual">Pay for 12 months at once (monthly plans only; business plans follow the quarterly diagnostic).</param>
    public static Subscription Create(CustomerAccount account, Plan plan, IEnumerable<Guid> propertyIds, DateTimeOffset now, bool annual = false)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(plan);
        var properties = propertyIds.Distinct().ToList();

        if (!plan.IsActive)
        {
            throw new DomainException("subscription.plan_inactive", "This offer is no longer available.");
        }

        if (plan.Segment != account.Kind)
        {
            throw new DomainException("subscription.plan_segment", "This offer is not available for this type of account.");
        }

        var validCount = account.Kind == AccountKind.Household ? properties.Count == 1 : properties.Count >= 1;
        if (!validCount)
        {
            throw new DomainException(
                "subscription.properties",
                "A home subscription covers exactly one home; a business subscription at least one site.");
        }

        if (annual && !plan.OffersAnnual)
        {
            throw new DomainException("subscription.annual_unavailable", "This offer is billed per quarter, after the diagnostic.");
        }

        return new Subscription(account, plan, properties, annual, now);
    }

    /// <summary>Registration fee and first period paid: the subscription starts on <paramref name="startDate"/>.</summary>
    public void Activate(DateOnly startDate, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.PendingRegistration)
        {
            throw new DomainException("subscription.already_active", "This subscription has already started.");
        }

        CurrentPeriod = DateRange.OfMonths(startDate, BillingPeriodMonths);
        Status = SubscriptionStatus.Active;
        ActivatedAt = now;
        Raise(new SubscriptionActivated(Id, AccountId, now));
    }

    /// <summary>
    /// Returns the next period to invoice. For plans requiring a diagnostic, refuses unless
    /// <paramref name="validatedDiagnostic"/> is validated and covers the current period.
    /// </summary>
    public DateRange PrepareRenewal(Diagnostic? validatedDiagnostic)
    {
        if (CurrentPeriod is null || Status is SubscriptionStatus.PendingRegistration or SubscriptionStatus.Cancelled)
        {
            throw new DomainException("subscription.not_renewable", "This subscription cannot be renewed.");
        }

        if (DiagnosticRequired && (validatedDiagnostic is null || !validatedDiagnostic.UnlocksRenewalOf(Id, CurrentPeriod)))
        {
            throw new DomainException(
                "subscription.diagnostic_required",
                "The quarterly diagnostic must be validated before billing the next period.");
        }

        return CurrentPeriod.Next(BillingPeriodMonths);
    }

    /// <summary>Payment of <paramref name="period"/> confirmed: move to it and restore service.</summary>
    public void RecordPeriodPaid(DateRange period, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (CurrentPeriod is null || Status == SubscriptionStatus.Cancelled)
        {
            throw new DomainException("subscription.not_renewable", "This subscription cannot be renewed.");
        }

        if (period != CurrentPeriod.Next(BillingPeriodMonths))
        {
            throw new DomainException("subscription.period_mismatch", "This payment is not for the next period.");
        }

        CurrentPeriod = period with { };
        if (Status != SubscriptionStatus.Active)
        {
            Status = SubscriptionStatus.Active;
            Raise(new SubscriptionActivated(Id, AccountId, now));
        }
    }

    /// <summary>Daily job: Active → PastDue after due date, PastDue → Suspended after the grace period.</summary>
    public void ApplyPaymentDeadlines(DateOnly today, int graceDays, DateTimeOffset now)
    {
        if (NextDueDate is not { } due)
        {
            return;
        }

        if (Status == SubscriptionStatus.Active && today > due)
        {
            Status = SubscriptionStatus.PastDue;
        }

        if (Status == SubscriptionStatus.PastDue && today > due.AddDays(graceDays))
        {
            Status = SubscriptionStatus.Suspended;
            Raise(new SubscriptionSuspended(Id, AccountId, now));
        }
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            return;
        }

        Status = SubscriptionStatus.Cancelled;
        CancelledAt = now;
    }

    public void AddProperty(Guid propertyId)
    {
        if (Segment == AccountKind.Household && _coveredPropertyIds.Count >= 1 && !_coveredPropertyIds.Contains(propertyId))
        {
            throw new DomainException("subscription.properties", "A home subscription covers exactly one home.");
        }

        if (!_coveredPropertyIds.Contains(propertyId))
        {
            _coveredPropertyIds.Add(propertyId);
        }
    }

    public bool Covers(Guid propertyId) => _coveredPropertyIds.Contains(propertyId);
}
