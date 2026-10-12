using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;

namespace SeneCare.Domain.Plans;

/// <summary>Maximum response delay for an urgency level, for a given plan.</summary>
public sealed record PlanSlaPolicy(Urgency Urgency, TimeSpan MaxDelay);

/// <summary>
/// Commercial offer (HOME, ESSENTIEL, PROFESSIONNEL, PREMIUM). Every price and service level is data,
/// editable by admins — never hard-coded. Existing subscriptions keep their price snapshot.
/// </summary>
public sealed class Plan : AggregateRoot
{
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 80;

    /// <summary>Default delays from the specification: Critical &lt; 4 h, High &lt; 24 h, Normal &lt; 72 h.</summary>
    public static readonly IReadOnlyList<PlanSlaPolicy> DefaultSlaPolicies =
    [
        new(Urgency.Critical, TimeSpan.FromHours(4)),
        new(Urgency.High, TimeSpan.FromHours(24)),
        new(Urgency.Normal, TimeSpan.FromHours(72)),
    ];

    private readonly List<PlanSlaPolicy> _slaPolicies = [];
    private readonly List<ServiceDomain> _includedDomains = [];

    private Plan()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Plan(
        string code,
        string name,
        AccountKind segment,
        Money registrationFee,
        Money monthlyPrice,
        int billingPeriodMonths,
        bool diagnosticRequired)
    {
        Code = Guard.Required(code, "plan.code", CodeMaxLength).ToUpperInvariant();
        Segment = segment;
        // Own copies: a value object instance must never be shared between two plans (EF Core owned types).
        _slaPolicies.AddRange(DefaultSlaPolicies.Select(p => p with { }));
        _includedDomains.AddRange(Enum.GetValues<ServiceDomain>());
        IsActive = true;
        Name = string.Empty;
        Update(name, registrationFee, monthlyPrice, billingPeriodMonths, diagnosticRequired);
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public AccountKind Segment { get; private set; }

    public Money RegistrationFee { get; private set; }

    public Money MonthlyPrice { get; private set; }

    /// <summary>1 for households (monthly), 3 for businesses (quarterly, after diagnostic).</summary>
    public int BillingPeriodMonths { get; private set; }

    /// <summary>A validated technical diagnostic is required before each new billing period.</summary>
    public bool DiagnosticRequired { get; private set; }

    /// <summary>Number of preventive visits included per period; null = unlimited.</summary>
    public int? IncludedVisitsPerPeriod { get; private set; }

    /// <summary>Dispatch priority, higher first (Premium &gt; Pro &gt; Essentiel).</summary>
    public int Priority { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<PlanSlaPolicy> SlaPolicies => _slaPolicies.AsReadOnly();

    public IReadOnlyCollection<ServiceDomain> IncludedDomains => _includedDomains.AsReadOnly();

    public Money PeriodPrice => MonthlyPrice * BillingPeriodMonths;

    public void Update(string name, Money registrationFee, Money monthlyPrice, int billingPeriodMonths, bool diagnosticRequired)
    {
        if (billingPeriodMonths is < 1 or > 12)
        {
            throw new DomainException("plan.period", "The billing period must be between 1 and 12 months.");
        }

        Name = Guard.Required(name, "plan.name", NameMaxLength);
        RegistrationFee = registrationFee;
        MonthlyPrice = monthlyPrice;
        BillingPeriodMonths = billingPeriodMonths;
        DiagnosticRequired = diagnosticRequired;
    }

    public void SetServiceLevel(int priority, int? includedVisitsPerPeriod)
    {
        if (includedVisitsPerPeriod < 0)
        {
            throw new DomainException("plan.visits", "Included visits cannot be negative.");
        }

        Priority = priority;
        IncludedVisitsPerPeriod = includedVisitsPerPeriod;
    }

    public void SetSla(Urgency urgency, TimeSpan maxDelay)
    {
        if (urgency == Urgency.Planned)
        {
            throw new DomainException("plan.sla_planned", "Planned work has no response delay.");
        }

        if (maxDelay <= TimeSpan.Zero)
        {
            throw new DomainException("plan.sla_delay", "The delay must be positive.");
        }

        _slaPolicies.RemoveAll(p => p.Urgency == urgency);
        _slaPolicies.Add(new PlanSlaPolicy(urgency, maxDelay));
    }

    /// <summary>Restores the specification delays for urgencies that have none (data repair, new urgencies).</summary>
    public void EnsureDefaultSla()
    {
        foreach (var policy in DefaultSlaPolicies.Where(d => !_slaPolicies.Exists(p => p.Urgency == d.Urgency)))
        {
            _slaPolicies.Add(policy with { });
        }
    }

    /// <summary>Max response delay for this urgency, or null when there is no SLA (planned work).</summary>
    public TimeSpan? GetMaxDelay(Urgency urgency) =>
        urgency == Urgency.Planned ? null : _slaPolicies.Find(p => p.Urgency == urgency)?.MaxDelay;

    public void SetIncludedDomains(IEnumerable<ServiceDomain> domains)
    {
        var list = domains.Distinct().ToList();
        if (list.Count == 0)
        {
            throw new DomainException("plan.domains", "A plan covers at least one domain.");
        }

        _includedDomains.Clear();
        _includedDomains.AddRange(list);
    }

    public bool Covers(ServiceDomain domain) => _includedDomains.Contains(domain);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
