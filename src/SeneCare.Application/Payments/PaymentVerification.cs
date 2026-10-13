using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Payments;

public sealed record UnpaidInvoice(Guid InvoiceId, string Number, string AccountName, Money Due, DateOnly DueDate, InvoiceStatus Status);

public sealed record PendingPayment(
    Guid PaymentId,
    string InvoiceNumber,
    Money Amount,
    PaymentChannel Channel,
    string? CustomerReference,
    DateTimeOffset DeclaredAt,
    string CustomerName,
    string CustomerPhone,
    string AccountName);

/// <summary>
/// Finance back-office for payment-link payments: list what customers declared, then verify (credit the
/// invoice and activate / extend the subscription) or reject with a reason. Every decision is audited.
/// </summary>
public sealed class PaymentVerificationHandler(IApplicationDbContext db, Settings settings, DiagnosticPlanner diagnostics, TimeProvider clock)
{
    public async Task<IReadOnlyList<PendingPayment>> PendingAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from p in db.Payments
                join i in db.Invoices on p.InvoiceId equals i.Id
                join a in db.CustomerAccounts on i.AccountId equals a.Id
                join u in db.Users on p.DeclaredByUserId equals u.Id
                where p.Status == PaymentStatus.Pending && p.DeclaredByUserId != null
                orderby p.CreatedAt
                select new { p, i.Number, Account = a.DisplayName, u.FullName, u.PhoneNumber })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new PendingPayment(
            r.p.Id, r.Number, r.p.Amount, r.p.Channel, r.p.CustomerReference, r.p.CreatedAt,
            r.FullName ?? string.Empty, r.PhoneNumber.Formatted, r.Account))];
    }

    public async Task<Result<Guid>> VerifyAsync(Guid adminUserId, Guid paymentId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result<Guid>.Failure("payment.not_found");
        }

        var invoice = await db.Invoices.FirstAsync(i => i.Id == payment.InvoiceId, cancellationToken);
        var now = clock.GetUtcNow();
        try
        {
            payment.Verify(adminUserId, now);
            await SettleAsync(invoice, payment, now, cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        Audit(adminUserId, "payment.verify", payment, ipAddress, $"{invoice.Number} · {payment.Amount.Amount.ToString(CultureInfo.InvariantCulture)} XOF · {payment.CustomerReference}", now);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(payment.Id);
    }

    public async Task<Result<Guid>> RejectAsync(Guid adminUserId, Guid paymentId, string? reason, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result<Guid>.Failure("payment.not_found");
        }

        var now = clock.GetUtcNow();
        try
        {
            payment.Reject(adminUserId, reason ?? string.Empty, now);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        Audit(adminUserId, "payment.reject", payment, ipAddress, reason, now);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(payment.Id);
    }

    /// <summary>Invoices waiting for money (for cash / bank transfer recording).</summary>
    public async Task<IReadOnlyList<UnpaidInvoice>> UnpaidAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from i in db.Invoices
                join a in db.CustomerAccounts on i.AccountId equals a.Id
                where i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue
                orderby i.DueDate
                select new { Invoice = i, Account = a.DisplayName })
            .AsNoTracking()
            .Take(200)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new UnpaidInvoice(r.Invoice.Id, r.Invoice.Number, r.Account, r.Invoice.Total - r.Invoice.AmountPaid, r.Invoice.DueDate, r.Invoice.Status))];
    }

    /// <summary>
    /// Cash or bank transfer received by SENECARE (large company invoices often go this way): recorded by
    /// Finance with a note (receipt number, bank reference), then settled like a verified Wave payment.
    /// </summary>
    public async Task<Result<Guid>> RecordManualAsync(
        Guid adminUserId, Guid invoiceId, PaymentChannel channel, string? note, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result<Guid>.Failure("invoice.not_found");
        }

        if (invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.Overdue))
        {
            return Result<Guid>.Failure("invoice.not_payable");
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            return Result<Guid>.Failure("payment.note_required");
        }

        var now = clock.GetUtcNow();
        Payment payment;
        try
        {
            payment = Payment.RecordManual(invoice.Id, invoice.Total - invoice.AmountPaid, channel, adminUserId, note, now);
            db.Payments.Add(payment);
            await SettleAsync(invoice, payment, now, cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        Audit(adminUserId, "payment.record_manual", payment, ipAddress, $"{invoice.Number} · {channel} · {note}", now);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(payment.Id);
    }

    /// <summary>Credits the invoice; once paid, starts or extends the subscription and plans its diagnostic.</summary>
    private async Task SettleAsync(Invoice invoice, Payment payment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        invoice.ApplyPayment(payment.Id, payment.Amount, now);
        if (invoice.Status == InvoiceStatus.Paid && invoice.SubscriptionId is { } subscriptionId)
        {
            var subscription = await db.Subscriptions.FirstAsync(s => s.Id == subscriptionId, cancellationToken);
            if (subscription.Status == SubscriptionStatus.PendingRegistration)
            {
                subscription.Activate(DakarCalendar.Today(clock), now);
            }
            else if (invoice.Period is { } period)
            {
                subscription.RecordPeriodPaid(period, now);
            }

            await diagnostics.EnsurePlannedAsync(subscription, cancellationToken);
        }
    }

    public Task<string> WaveLinkAsync(CancellationToken cancellationToken = default) =>
        settings.StringAsync(SettingKeys.WaveLink, cancellationToken);

    /// <summary>The Wave merchant link is a setting (never in code): only pay.wave.com HTTPS links are accepted.</summary>
    public async Task<Result<string>> SetWaveLinkAsync(Guid adminUserId, string? link, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var value = link?.Trim() ?? string.Empty;
        if (value.Length > 0 && !WaveLink.IsValid(value))
        {
            return Result<string>.Failure("payment.wave_link_invalid");
        }

        var now = clock.GetUtcNow();
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.WaveLink, cancellationToken);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting(SettingKeys.WaveLink, value, now));
        }
        else
        {
            setting.Change(value, now);
        }

        db.AuditLogs.Add(new AuditLog(adminUserId, "settings.wave_link", nameof(AppSetting), SettingKeys.WaveLink, false, ipAddress, value, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result<string>.Success(value);
    }

    private void Audit(Guid adminUserId, string action, Payment payment, string? ipAddress, string? details, DateTimeOffset now) =>
        db.AuditLogs.Add(new AuditLog(adminUserId, action, nameof(Payment), payment.Id.ToString(), false, ipAddress, details, now));
}
