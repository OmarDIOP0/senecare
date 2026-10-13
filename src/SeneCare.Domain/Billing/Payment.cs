using SeneCare.Domain.Common;

namespace SeneCare.Domain.Billing;

public enum PaymentChannel
{
    Wave = 1,
    OrangeMoney = 2,
    FreeMoney = 3,
    Cash = 10,
    BankTransfer = 11,
}

public enum PaymentStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

public sealed record PaymentSucceeded(Guid PaymentId, Guid InvoiceId, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// A payment attempt for an invoice. Mobile-money payments are confirmed only by the provider's signed
/// webhook (never by the browser return); cash and transfers are recorded by the Finance role.
/// </summary>
public sealed class Payment : AggregateRoot
{
    public const int ReferenceMaxLength = 128;
    public const int NoteMaxLength = 500;

    private Payment()
    {
    }

    private Payment(Guid invoiceId, Money amount, PaymentChannel channel, DateTimeOffset now)
    {
        if (amount == Money.Zero)
        {
            throw new DomainException("payment.zero", "A payment amount must be positive.");
        }

        InvoiceId = invoiceId;
        Amount = amount;
        Channel = channel;
        Status = PaymentStatus.Pending;
        CreatedAt = now;
    }

    public Guid InvoiceId { get; private set; }

    public Money Amount { get; private set; }

    public PaymentChannel Channel { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Transaction id at the aggregator (PayDunya/PayTech/CinetPay), used for idempotent webhooks.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Finance user who recorded a manual (cash / transfer) payment.</summary>
    public Guid? RecordedByUserId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsManual => Channel is PaymentChannel.Cash or PaymentChannel.BankTransfer;

    public static Payment StartMobileMoney(Guid invoiceId, Money amount, PaymentChannel channel, DateTimeOffset now)
    {
        if (channel is PaymentChannel.Cash or PaymentChannel.BankTransfer)
        {
            throw new DomainException("payment.channel", "Use RecordManual for cash or bank transfer.");
        }

        return new Payment(invoiceId, amount, channel, now);
    }

    /// <summary>
    /// The customer says they paid through a Wave (or other) payment link. Nothing is credited until a
    /// Finance admin checks the transfer and calls <see cref="Verify"/>.
    /// </summary>
    public static Payment Declare(
        Guid invoiceId, Money amount, PaymentChannel channel, Guid declaredByUserId, string? customerReference, DateTimeOffset now)
    {
        if (channel is PaymentChannel.Cash or PaymentChannel.BankTransfer)
        {
            throw new DomainException("payment.channel", "Only mobile money payments can be declared by the customer.");
        }

        return new Payment(invoiceId, amount, channel, now)
        {
            DeclaredByUserId = declaredByUserId,
            CustomerReference = Guard.Optional(customerReference, "payment.reference", ReferenceMaxLength),
        };
    }

    /// <summary>Customer who declared the payment (payment link), if any.</summary>
    public Guid? DeclaredByUserId { get; private set; }

    /// <summary>Transaction id the customer copied from their Wave receipt, to help the check.</summary>
    public string? CustomerReference { get; private set; }

    public bool IsAwaitingVerification => Status == PaymentStatus.Pending && DeclaredByUserId is not null;

    /// <summary>Finance admin found the transfer: the payment counts.</summary>
    public void Verify(Guid adminUserId, DateTimeOffset now)
    {
        EnsureAwaitingVerification();
        RecordedByUserId = adminUserId;
        Succeed(null, now);
    }

    /// <summary>Transfer not found or wrong amount: the customer sees the reason and can declare again.</summary>
    public void Reject(Guid adminUserId, string reason, DateTimeOffset now)
    {
        EnsureAwaitingVerification();
        RecordedByUserId = adminUserId;
        Note = Guard.Required(reason, "payment.rejection_reason", NoteMaxLength);
        Fail(now);
    }

    private void EnsureAwaitingVerification()
    {
        if (!IsAwaitingVerification)
        {
            throw new DomainException("payment.not_awaiting_verification", "This payment is not waiting for verification.");
        }
    }

    public static Payment RecordManual(
        Guid invoiceId, Money amount, PaymentChannel channel, Guid recordedByUserId, string? note, DateTimeOffset now)
    {
        if (channel is not (PaymentChannel.Cash or PaymentChannel.BankTransfer))
        {
            throw new DomainException("payment.channel", "Only cash or bank transfer can be recorded manually.");
        }

        var payment = new Payment(invoiceId, amount, channel, now)
        {
            RecordedByUserId = recordedByUserId,
            Note = Guard.Optional(note, "payment.note", NoteMaxLength),
        };
        payment.Succeed(null, now);
        return payment;
    }

    public void AttachProviderReference(string reference)
    {
        ProviderReference = Guard.Required(reference, "payment.reference", ReferenceMaxLength);
    }

    /// <summary>Confirmation (webhook or manual). Idempotent.</summary>
    public void Succeed(string? providerReference, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Succeeded)
        {
            return;
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new DomainException("payment.not_pending", "Only a pending payment can succeed.");
        }

        if (providerReference is not null)
        {
            AttachProviderReference(providerReference);
        }

        Status = PaymentStatus.Succeeded;
        CompletedAt = now;
        Raise(new PaymentSucceeded(Id, InvoiceId, Amount, now));
    }

    public void Fail(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Succeeded)
        {
            throw new DomainException("payment.already_succeeded", "A successful payment cannot fail.");
        }

        Status = PaymentStatus.Failed;
        CompletedAt = now;
    }
}
