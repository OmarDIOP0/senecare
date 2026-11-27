using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Payments;

/// <summary>A payment request sent to Orange Money: our own reference (the payment id), the amount, a label.</summary>
/// <summary>Request for a payment; <paramref name="ReturnPath"/> is where Max it sends the person back (our payment page).</summary>
public sealed record OrangeMoneyCheckoutRequest(string Reference, Money Amount, string Label, string? ReturnPath = null);

/// <summary>What Orange Money gives back to pay: a QR code (PNG, Base64) and the Max it / Orange Money links.</summary>
public sealed record OrangeMoneyCheckout(string? QrCodePng, string? MaxItLink, string? OrangeMoneyLink, string? ShortLink, DateTimeOffset ExpiresAt);

/// <summary>A merchant payment as Orange Money reports it (the authoritative state, never the callback body).</summary>
public sealed record OrangeMoneyTransaction(string TransactionId, string Reference, string Status, long Amount, string? Currency, string? MerchantCode, string? Type)
{
    public bool IsSuccess => Status == "SUCCESS";

    public bool IsFinalFailure => Status is "FAILED" or "CANCELLED" or "REJECTED";
}

/// <summary>Orange Money could not be reached or refused the request (network, credentials, rate limit…).</summary>
public sealed class OrangeMoneyException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Orange Money merchant payment API (Sonatel): QR code / deep links, transaction search, callback registration.</summary>
public interface IOrangeMoneyGateway
{
    /// <summary>Credentials and merchant code are set (OrangeMoney:* settings, never in code).</summary>
    bool IsConfigured { get; }

    /// <summary>Production (real money); false in Sandbox, where only SENECARE staff are offered Orange Money (tests).</summary>
    bool IsLive { get; }

    string? MerchantCode { get; }

    Task<OrangeMoneyCheckout> CreateCheckoutAsync(OrangeMoneyCheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>The merchant payment carrying our reference, if Orange Money knows it (null: not paid yet).</summary>
    Task<OrangeMoneyTransaction?> FindPaymentAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Registers our HTTPS notification address and shared key for the merchant code (persistent).</summary>
    Task RegisterCallbackAsync(CancellationToken cancellationToken = default);

    /// <summary>The callback carries the shared key we registered (« Authorization: Basic {key} », compared in constant time).</summary>
    bool IsTrustedCallback(string? authorizationHeader);

    /// <summary>Last refusal or failure of Orange Money since the site started (status and Orange's code, never a key).</summary>
    OrangeMoneyProblem? LastProblem { get; }

    /// <summary>Asks Orange for a new access token with the configured keys (Admin › Paiements « Tester la connexion »).</summary>
    Task TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed record OrangeMoneyProblem(DateTimeOffset At, string Detail);

public enum OrangeMoneyState
{
    Pending,
    Paid,
    Failed,
}

public sealed record OrangeMoneyStart(Guid PaymentId, string InvoiceNumber, Money Amount, OrangeMoneyCheckout Checkout);

/// <summary>
/// Paying an invoice with Orange Money through Sonatel's API (user decision of 24/11/2026: it replaces the manual
/// merchant code, and a payment confirmed by Orange activates the subscription at once, Finance being informed).
/// A payment is credited only after Orange Money's own transaction state says SUCCESS for our reference, the right
/// amount and our merchant code: the callback is only a signal, its body is never trusted.
/// </summary>
public sealed partial class OrangeMoneyPayments(
    IApplicationDbContext db,
    AccessControl access,
    IOrangeMoneyGateway gateway,
    PaymentSettlement settlement,
    Users.Notifier notifier,
    TimeProvider clock,
    ILogger<OrangeMoneyPayments> logger)
{
    // One confirmation at a time per payment (callbacks come at least once, sometimes together): the state is read
    // again inside the lock, so a payment is credited once. The site runs as one process (IIS, SmarterASP); several
    // instances would need a database concurrency token on payments instead.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public bool IsAvailable => gateway.IsConfigured;

    /// <summary>
    /// Orange Money is offered to this person: configured, and live — in Sandbox (test keys, no real money) only SENECARE
    /// staff see it, so customers are never sent to a test payment.
    /// </summary>
    public async Task<bool> IsOfferedToAsync(Guid userId, CancellationToken cancellationToken = default) =>
        gateway.IsConfigured
        && (gateway.IsLive || await db.Users.AnyAsync(u => u.Id == userId && u.Roles.Any(r => r.Role != Domain.Users.PlatformRole.Customer), cancellationToken));

    /// <summary>A new Orange Money payment for what the invoice still owes, with its QR code and links.</summary>
    public async Task<Result<OrangeMoneyStart>> StartAsync(Guid userId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        if (!await IsOfferedToAsync(userId, cancellationToken))
        {
            return Result<OrangeMoneyStart>.Failure("payment.channel_invalid");
        }

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null || !await access.CanAccessAccountAsync(userId, invoice.AccountId, cancellationToken))
        {
            return Result<OrangeMoneyStart>.Failure("invoice.not_found");
        }

        // An earlier attempt may have been paid meanwhile (callback lost): settle it before asking for money again.
        var earlier = await db.Payments
            .Where(p => p.InvoiceId == invoice.Id && p.Channel == PaymentChannel.OrangeMoney && p.DeclaredByUserId == null && p.Status == PaymentStatus.Pending)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in earlier)
        {
            await ConfirmAsync(Reference(id), cancellationToken);
        }

        if (invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.Overdue))
        {
            return Result<OrangeMoneyStart>.Failure("invoice.not_payable");
        }

        var now = clock.GetUtcNow();
        var payment = Payment.StartMobileMoney(invoice.Id, invoice.Balance, PaymentChannel.OrangeMoney, now);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var checkout = await gateway.CreateCheckoutAsync(
                new OrangeMoneyCheckoutRequest(Reference(payment.Id), payment.Amount, invoice.Number, PayPagePath(payment.Id)), cancellationToken);
            payment.AttachCheckout(checkout.MaxItLink, checkout.OrangeMoneyLink, checkout.QrCodePng, checkout.ExpiresAt);
            await db.SaveChangesAsync(cancellationToken);
            return Result<OrangeMoneyStart>.Success(new OrangeMoneyStart(payment.Id, invoice.Number, payment.Amount, checkout));
        }
        catch (OrangeMoneyException ex)
        {
            LogStartFailed(logger, invoice.Number, ex);
            payment.Fail(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return Result<OrangeMoneyStart>.Failure("payment.provider_unavailable");
        }
    }

    /// <summary>« Vérifier mon paiement »: asks Orange Money now (the customer's own payments only).</summary>
    public async Task<Result<OrangeMoneyState>> CheckAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await OwnsAsync(userId, paymentId, cancellationToken))
        {
            return Result<OrangeMoneyState>.Failure("payment.not_found");
        }

        try
        {
            return Result<OrangeMoneyState>.Success(await ConfirmAsync(Reference(paymentId), cancellationToken));
        }
        catch (OrangeMoneyException ex)
        {
            LogCheckFailed(logger, ex);
            return Result<OrangeMoneyState>.Failure("payment.provider_unavailable");
        }
    }

    /// <summary>The state as recorded here (no call to Orange Money): polled by the payment page.</summary>
    /// <summary>Our payment page: Max it sends the person back there, and it follows the payment until confirmed.</summary>
    public static string PayPagePath(Guid paymentId) => $"/abonnement/orange-money/{paymentId}";

    /// <summary>The payment request (links, QR code) as given by Orange Money, for the customer's own payments.</summary>
    public async Task<OrangeMoneyStart?> CheckoutAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await OwnsAsync(userId, paymentId, cancellationToken))
        {
            return null;
        }

        var row = await (
                from p in db.Payments
                join i in db.Invoices on p.InvoiceId equals i.Id
                where p.Id == paymentId
                select new { p.Amount, i.Number, p.CheckoutLink, p.CheckoutAltLink, p.CheckoutQrCode, p.CheckoutExpiresAt })
            .AsNoTracking()
            .FirstAsync(cancellationToken);
        return row.CheckoutExpiresAt is { } expires && expires > clock.GetUtcNow() && (row.CheckoutLink is not null || row.CheckoutQrCode is not null)
            ? new OrangeMoneyStart(paymentId, row.Number, row.Amount, new OrangeMoneyCheckout(row.CheckoutQrCode, row.CheckoutLink, row.CheckoutAltLink, null, expires))
            : null;
    }

    public async Task<OrangeMoneyState?> StateAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await OwnsAsync(userId, paymentId, cancellationToken))
        {
            return null;
        }

        var status = await db.Payments.Where(p => p.Id == paymentId).Select(p => p.Status).FirstAsync(cancellationToken);
        return State(status);
    }

    /// <summary>
    /// Callback or check: asks Orange Money for the payment carrying <paramref name="reference"/> and credits it once,
    /// only when SUCCESS with the expected amount and merchant code. Idempotent (callbacks come at least once).
    /// </summary>
    public async Task<OrangeMoneyState> ConfirmAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(reference, "N", out var paymentId))
        {
            return OrangeMoneyState.Failed;
        }

        var gate = Locks.GetOrAdd(paymentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ConfirmLockedAsync(paymentId, reference, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<OrangeMoneyState> ConfirmLockedAsync(Guid paymentId, string reference, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId && p.Channel == PaymentChannel.OrangeMoney, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            return payment is null ? OrangeMoneyState.Failed : State(payment.Status);
        }

        var transaction = await gateway.FindPaymentAsync(reference, cancellationToken);
        if (transaction is null)
        {
            return OrangeMoneyState.Pending;
        }

        var now = clock.GetUtcNow();
        if (transaction.IsFinalFailure)
        {
            payment.Fail(now);
            await db.SaveChangesAsync(cancellationToken);
            return OrangeMoneyState.Failed;
        }

        if (!transaction.IsSuccess)
        {
            return OrangeMoneyState.Pending;
        }

        // Fail closed: our merchant code, the exact amount in XOF, a merchant payment — anything missing or different
        // is never credited automatically.
        var expectedMerchant = gateway.MerchantCode;
        if (transaction.Amount != payment.Amount.Amount || transaction.Currency != "XOF" || transaction.Type != "MERCHANT_PAYMENT"
            || expectedMerchant is null || transaction.MerchantCode != expectedMerchant)
        {
            // Never credited automatically: Finance looks at it.
            LogMismatch(logger, reference, transaction.Amount, payment.Amount.Amount);
            await notifier.ToFinanceAsync(Users.NotificationTemplates.PaymentMismatch, new { amount = transaction.Amount, channel = PaymentChannel.OrangeMoney.ToString(), reason = transaction.TransactionId }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return OrangeMoneyState.Pending;
        }

        var invoice = await db.Invoices.FirstAsync(i => i.Id == payment.InvoiceId, cancellationToken);
        payment.Succeed(transaction.TransactionId, now);
        var details = $"{invoice.Number} · {payment.Amount.Amount.ToString(CultureInfo.InvariantCulture)} XOF · {transaction.TransactionId}";
        if (invoice.Status is InvoiceStatus.Issued or InvoiceStatus.Overdue)
        {
            await settlement.SettleAsync(invoice, payment, now, cancellationToken);
            await settlement.NotifyPaidAsync(invoice, payment, cancellationToken);
            await notifier.ToFinanceAsync(Users.NotificationTemplates.PaymentReceived, new { invoice = invoice.Number, amount = payment.Amount.Amount, channel = PaymentChannel.OrangeMoney.ToString() }, cancellationToken);
            Audit("payment.orange_money_confirmed", payment, details, now);
        }
        else
        {
            // Paid twice (two QR codes for one invoice): the money is there, Finance refunds or carries it over.
            await notifier.ToFinanceAsync(Users.NotificationTemplates.PaymentDuplicate, new { invoice = invoice.Number, amount = payment.Amount.Amount, channel = PaymentChannel.OrangeMoney.ToString() }, cancellationToken);
            Audit("payment.orange_money_duplicate", payment, details, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return OrangeMoneyState.Paid;
    }

    /// <summary>Our reference at Orange Money: the payment id, 32 hex characters (≤ 50 accepted).</summary>
    public static string Reference(Guid paymentId) => paymentId.ToString("N", CultureInfo.InvariantCulture);

    private async Task<bool> OwnsAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken)
    {
        var accountId = await (
                from p in db.Payments
                join i in db.Invoices on p.InvoiceId equals i.Id
                where p.Id == paymentId && p.Channel == PaymentChannel.OrangeMoney
                select (Guid?)i.AccountId)
            .FirstOrDefaultAsync(cancellationToken);
        return accountId is { } id && await access.CanAccessAccountAsync(userId, id, cancellationToken);
    }

    private static OrangeMoneyState State(PaymentStatus status) => status switch
    {
        PaymentStatus.Succeeded => OrangeMoneyState.Paid,
        PaymentStatus.Pending => OrangeMoneyState.Pending,
        _ => OrangeMoneyState.Failed,
    };

    private void Audit(string action, Payment payment, string details, DateTimeOffset now) =>
        db.AuditLogs.Add(new AuditLog(Guid.Empty, action, nameof(Payment), payment.Id.ToString(), false, null, details, now));

    [LoggerMessage(Level = LogLevel.Error, Message = "Orange Money payment could not be started for invoice {Invoice}")]
    private static partial void LogStartFailed(ILogger logger, string invoice, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orange Money could not be reached to check a payment")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Orange Money payment {Reference}: amount {Paid} XOF instead of {Expected} XOF or another merchant, not credited")]
    private static partial void LogMismatch(ILogger logger, string reference, long paid, long expected);
}
