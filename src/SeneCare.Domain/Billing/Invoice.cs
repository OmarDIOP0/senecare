using SeneCare.Domain.Common;

namespace SeneCare.Domain.Billing;

public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    Paid = 3,
    Overdue = 4,
    Cancelled = 5,
}

public enum InvoiceLineKind
{
    RegistrationFee = 1,
    SubscriptionPeriod = 2,
    Other = 3,
}

public sealed record InvoicePaid(Guid InvoiceId, Guid AccountId, Guid? SubscriptionId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record InvoiceLine
{
    public const int DescriptionMaxLength = 200;

    public InvoiceLine(InvoiceLineKind kind, string description, int quantity, Money unitPrice)
    {
        if (quantity <= 0)
        {
            throw new DomainException("invoice.quantity", "Quantity must be positive.");
        }

        Kind = kind;
        Description = Guard.Required(description, "invoice.line", DescriptionMaxLength);
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public InvoiceLineKind Kind { get; private init; }

    public string Description { get; private init; }

    public int Quantity { get; private init; }

    public Money UnitPrice { get; private init; }

    public Money Total => UnitPrice * Quantity;
}

/// <summary>Invoice of a customer account. Paid when confirmed payments cover the total.</summary>
public sealed class Invoice : AggregateRoot
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<Guid> _appliedPaymentIds = [];

    private Invoice()
    {
        Number = string.Empty;
    }

    public Invoice(string number, Guid accountId, Guid? subscriptionId, DateRange? period, DateOnly dueDate)
    {
        Number = Guard.Required(number, "invoice.number", 32);
        AccountId = accountId;
        SubscriptionId = subscriptionId;
        Period = period is null ? null : period with { };
        DueDate = dueDate;
        Status = InvoiceStatus.Draft;
    }

    /// <summary>Human-readable number, e.g. « FAC-2026-000123 » (sequence managed by infrastructure).</summary>
    public string Number { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? SubscriptionId { get; private set; }

    /// <summary>Subscription period billed, when relevant.</summary>
    public DateRange? Period { get; private set; }

    public DateOnly DueDate { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public Money AmountPaid { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    public IReadOnlyCollection<Guid> AppliedPaymentIds => _appliedPaymentIds.AsReadOnly();

    public Money Total => _lines.Aggregate(Money.Zero, (sum, line) => sum + line.Total);

    public Money Balance => AmountPaid >= Total ? Money.Zero : Total - AmountPaid;

    public void AddLine(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != InvoiceStatus.Draft)
        {
            throw new DomainException("invoice.not_draft", "An issued invoice cannot be changed.");
        }

        _lines.Add(line);
    }

    public void Issue(DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new DomainException("invoice.not_draft", "This invoice has already been issued.");
        }

        if (_lines.Count == 0 || Total == Money.Zero)
        {
            throw new DomainException("invoice.empty", "An invoice needs at least one non-zero line.");
        }

        Status = InvoiceStatus.Issued;
        IssuedAt = now;
    }

    /// <summary>Applies a confirmed payment. Idempotent: the same payment is never counted twice.</summary>
    public void ApplyPayment(Guid paymentId, Money amount, DateTimeOffset now)
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.Overdue))
        {
            throw new DomainException("invoice.not_payable", "This invoice cannot receive payments.");
        }

        if (_appliedPaymentIds.Contains(paymentId))
        {
            return;
        }

        _appliedPaymentIds.Add(paymentId);
        AmountPaid += amount;

        if (AmountPaid >= Total)
        {
            Status = InvoiceStatus.Paid;
            PaidAt = now;
            Raise(new InvoicePaid(Id, AccountId, SubscriptionId, now));
        }
    }

    public void MarkOverdueIfLate(DateOnly today)
    {
        if (Status == InvoiceStatus.Issued && today > DueDate)
        {
            Status = InvoiceStatus.Overdue;
        }
    }

    public void Cancel()
    {
        if (AmountPaid > Money.Zero || Status == InvoiceStatus.Paid)
        {
            throw new DomainException("invoice.has_payments", "An invoice with payments cannot be cancelled.");
        }

        Status = InvoiceStatus.Cancelled;
    }
}
