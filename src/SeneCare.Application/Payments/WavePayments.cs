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
    PaymentDeclarationState? Declaration,
    string? OrangeMoneyUrl = null,
    string? OrangeMoneyCode = null)
{
    /// <summary>Mobile money ways to pay that the admin opened (Wave, Orange Money), in display order.</summary>
    public IReadOnlyList<PaymentChannel> Channels =>
        [.. new[] { (PaymentChannel.Wave, WaveUrl is not null), (PaymentChannel.OrangeMoney, OrangeMoneyUrl is not null || OrangeMoneyCode is not null) }
            .Where(c => c.Item2).Select(c => c.Item1)];
}

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
/// Orange Money: a merchant code / number (paid from #144# or the app) and/or a payment link set by the admin.
/// « {montant} » in the link is replaced by the amount to pay.
/// </summary>
public static class OrangeMoney
{
    public static bool IsValidLink(string? link) =>
        Uri.TryCreate(link?.Replace(WaveLink.AmountPlaceholder, "1", StringComparison.Ordinal), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>Digits (spaces allowed between groups), 4 to 20 characters: a merchant code or a phone number.</summary>
    public static bool IsValidCode(string? code) =>
        code is { Length: >= 4 and <= 20 } && code.All(c => char.IsAsciiDigit(c) || c == ' ');

    public static string? LinkFor(string? link, Money amount) =>
        string.IsNullOrWhiteSpace(link) || !IsValidLink(link)
            ? null
            : link.Replace(WaveLink.AmountPlaceholder, amount.Amount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
                // A new offer chosen for the next period takes effect when that period is invoiced (not once its
                // invoice is out: then it waits for the following one).
                if (subscription.NextPlanId is { } nextPlanId
                    && !await db.Invoices.AnyAsync(
                        i => i.SubscriptionId == subscription.Id && i.Period != null && i.Period.Start == due
                            && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue),
                        cancellationToken))
                {
                    var next = await db.Plans.FirstOrDefaultAsync(p => p.Id == nextPlanId && p.IsActive, cancellationToken);
                    if (next is null)
                    {
                        subscription.CancelScheduledChange();
                    }
                    else
                    {
                        subscription.ApplyScheduledChange(next);
                    }
                }

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
        var orangeLink = await settings.StringAsync(SettingKeys.OrangeMoneyLink, cancellationToken);
        var orangeCode = (await settings.StringAsync(SettingKeys.OrangeMoneyCode, cancellationToken)).Trim();
        return new InvoiceDue(
            subscription.Id, invoice.Id, invoice.Number, invoice.Balance, isRegistration, period,
            WaveLink.For(waveLink, invoice.Balance), declaration,
            OrangeMoney.LinkFor(orangeLink, invoice.Balance),
            OrangeMoney.IsValidCode(orangeCode) ? orangeCode : null);
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
            subscription.FreeMonthsPerPeriod > 0
                ? $"Abonnement annuel {plan.Name} ({subscription.BillingPeriodMonths} mois dont {subscription.FreeMonthsPerPeriod} offerts)"
                : $"Abonnement {plan.Name}",
            subscription.ChargedMonths,
            subscription.MonthlyPrice));
        invoice.Issue(clock.GetUtcNow());
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }
}

/// <summary>« J'ai payé »: the customer declares a Wave or Orange Money payment; a Finance admin will check it.</summary>
public sealed class DeclarePaymentHandler(IApplicationDbContext db, AccessControl access, Users.Notifier notifier, TimeProvider clock)
{
    public async Task<Result<Guid>> HandleAsync(
        Guid userId, Guid invoiceId, string? customerReference, PaymentChannel channel = PaymentChannel.Wave, CancellationToken cancellationToken = default)
    {
        // Only the mobile money ways a customer pays by themselves; cash and transfers are recorded by Finance.
        if (channel is not (PaymentChannel.Wave or PaymentChannel.OrangeMoney))
        {
            return Result<Guid>.Failure("payment.channel_invalid");
        }

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
            payment = Payment.Declare(invoice.Id, invoice.Balance, channel, userId, customerReference, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.Payments.Add(payment);
        // Finance gets it in the bell: a payment to check against that merchant account (Wave or Orange Money).
        await notifier.ToFinanceAsync(Users.NotificationTemplates.PaymentDeclared, new { invoice = invoice.Number, amount = invoice.Balance.Amount, channel = channel.ToString() }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(payment.Id);
    }
}
