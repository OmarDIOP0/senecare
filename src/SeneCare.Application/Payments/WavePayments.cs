using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Payments;

public sealed record InvoiceDue(
    Guid SubscriptionId,
    Guid InvoiceId,
    string Number,
    Money Amount,
    bool IsRegistration,
    DateRange? Period,
    string? WaveUrl,
    PaymentDeclarationState? Declaration);

/// <summary>Last payment declared by the customer for this invoice.</summary>
public sealed record PaymentDeclarationState(PaymentStatus Status, DateTimeOffset DeclaredAt, string? RejectionReason);

/// <summary>Builds the Wave link: « {montant} » in the configured link is replaced by the amount to pay.</summary>
public static class WaveLink
{
    public const string AmountPlaceholder = "{montant}";

    public static bool IsValid(string? link) =>
        Uri.TryCreate(link?.Replace(AmountPlaceholder, "1", StringComparison.Ordinal), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host == "pay.wave.com" || uri.Host.EndsWith(".wave.com", StringComparison.Ordinal));

    public static string? For(string? link, Money amount) =>
        string.IsNullOrWhiteSpace(link) || !IsValid(link)
            ? null
            : link.Replace(AmountPlaceholder, amount.Amount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

/// <summary>
/// What the customer has to pay now, as an issued invoice: registration fee + first period for a new
/// subscription, or the next period within 7 days of the due date. The invoice number is the reference
/// to write in the Wave note.
/// </summary>
public sealed class PrepareInvoiceHandler(
    IApplicationDbContext db,
    AccessControl access,
    IDocumentNumberGenerator numbers,
    Settings settings,
    DiagnosticPlanner diagnostics,
    TimeProvider clock)
{
    public const int RenewalWindowDays = 7;

    public async Task<InvoiceDue?> HandleAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
        if (subscription is null || !await access.CanAccessAccountAsync(userId, subscription.AccountId, cancellationToken))
        {
            return null;
        }

        var today = DakarCalendar.Today(clock);
        var isRegistration = subscription.Status == SubscriptionStatus.PendingRegistration;
        DateRange? period = null;
        if (!isRegistration)
        {
            if (subscription.Status == SubscriptionStatus.Cancelled || subscription.NextDueDate is not { } due || due > today.AddDays(RenewalWindowDays))
            {
                return null;
            }

            try
            {
                // Business plans need the validated quarterly diagnostic: until then nothing is due.
                period = subscription.PrepareRenewal(await diagnostics.ValidatedForCurrentPeriodAsync(subscription, cancellationToken));
            }
            catch (DomainException)
            {
                return null;
            }
        }

        var invoice = await db.Invoices
            .Where(i => i.SubscriptionId == subscription.Id && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue))
            .OrderByDescending(i => i.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice is null || invoice.Period != period)
        {
            invoice = await CreateInvoiceAsync(subscription, period, today, cancellationToken);
        }

        var declaration = await db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoice.Id && p.DeclaredByUserId != null)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PaymentDeclarationState(p.Status, p.CreatedAt, p.Note))
            .FirstOrDefaultAsync(cancellationToken);

        var waveLink = await settings.StringAsync(SettingKeys.WaveLink, cancellationToken);
        return new InvoiceDue(
            subscription.Id, invoice.Id, invoice.Number, invoice.Balance, isRegistration, period,
            WaveLink.For(waveLink, invoice.Balance), declaration);
    }

    private async Task<Invoice> CreateInvoiceAsync(Subscription subscription, DateRange? period, DateOnly today, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == subscription.PlanId, cancellationToken);
        var invoice = new Invoice(
            await numbers.NextInvoiceNumberAsync(cancellationToken),
            subscription.AccountId,
            subscription.Id,
            period,
            period?.Start ?? today);

        if (period is null)
        {
            invoice.AddLine(new InvoiceLine(InvoiceLineKind.RegistrationFee, $"Inscription {plan.Name}", 1, subscription.RegistrationFee));
        }

        invoice.AddLine(new InvoiceLine(
            InvoiceLineKind.SubscriptionPeriod,
            $"Abonnement {plan.Name}",
            subscription.BillingPeriodMonths,
            subscription.MonthlyPrice));
        invoice.Issue(clock.GetUtcNow());
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }
}

/// <summary>« J'ai payé »: the customer declares a Wave payment; a Finance admin will check it.</summary>
public sealed class DeclarePaymentHandler(IApplicationDbContext db, AccessControl access, TimeProvider clock)
{
    public async Task<Result<Guid>> HandleAsync(Guid userId, Guid invoiceId, string? customerReference, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null || !await access.CanAccessAccountAsync(userId, invoice.AccountId, cancellationToken))
        {
            return Result<Guid>.Failure("invoice.not_found");
        }

        if (invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.Overdue))
        {
            return Result<Guid>.Failure("invoice.not_payable");
        }

        var alreadyPending = await db.Payments
            .AnyAsync(p => p.InvoiceId == invoice.Id && p.DeclaredByUserId != null && p.Status == PaymentStatus.Pending, cancellationToken);
        if (alreadyPending)
        {
            return Result<Guid>.Failure("payment.already_declared");
        }

        Payment payment;
        try
        {
            payment = Payment.Declare(invoice.Id, invoice.Balance, PaymentChannel.Wave, userId, customerReference, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(payment.Id);
    }
}
