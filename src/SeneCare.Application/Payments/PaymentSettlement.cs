using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Admin;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;

namespace SeneCare.Application.Payments;

/// <summary>
/// What a confirmed payment does, whoever confirmed it (Finance for Wave / cash / transfer, Orange Money's API):
/// the invoice is credited; once paid, the subscription starts or moves to the next period, its diagnostic is
/// planned, and the customer is told (bell + WhatsApp / SMS).
/// </summary>
public sealed class PaymentSettlement(
    IApplicationDbContext db, DiagnosticPlanner diagnostics, NeighborhoodMembership neighborhoods, Users.Notifier notifier, TimeProvider clock)
{
    public async Task SettleAsync(Invoice invoice, Payment payment, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(payment);
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
            await neighborhoods.RefreshForPropertiesAsync([.. subscription.CoveredPropertyIds], cancellationToken);
        }
    }

    /// <summary>The customer learns that the money arrived and the subscription is running.</summary>
    public Task NotifyPaidAsync(Invoice invoice, Payment payment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(payment);
        return notifier.ToAccountAsync(
            invoice.AccountId,
            Users.NotificationTemplates.PaymentVerified,
            new { invoice = invoice.Number, amount = payment.Amount.Amount },
            invoice.Status == InvoiceStatus.Paid ? Resources.Messages.PaymentVerified(invoice.Number, Language.French) : null,
            cancellationToken);
    }
}
