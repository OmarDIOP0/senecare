using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using static SeneCare.Domain.Tests.TestData;

namespace SeneCare.Domain.Tests;

public class SubscriptionTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    [Fact]
    public void Household_subscription_covers_exactly_one_home()
    {
        var account = Household();

        Assert.Throws<DomainException>(() => Subscription.Create(account, HomePlan(), [], Now));
        Assert.Throws<DomainException>(() =>
            Subscription.Create(account, HomePlan(), [Guid.CreateVersion7(), Guid.CreateVersion7()], Now));
        Assert.NotNull(Subscription.Create(account, HomePlan(), [Guid.CreateVersion7()], Now));
    }

    [Fact]
    public void Plan_must_match_the_account_type() =>
        Assert.Throws<DomainException>(() => Subscription.Create(Household(), BusinessPlan(), [Guid.CreateVersion7()], Now));

    [Fact]
    public void Price_is_snapshotted_at_creation()
    {
        var plan = HomePlan();
        var subscription = Subscription.Create(Household(), plan, [Guid.CreateVersion7()], Now);

        plan.Update("Maisons & Villas", Money.FromFcfa(10_000), Money.FromFcfa(9_500), 1, false);

        Assert.Equal(8_500, subscription.MonthlyPrice.Amount);
    }

    [Fact]
    public void Business_subscription_is_billed_per_quarter()
    {
        var subscription = ActiveBusinessSubscription();

        Assert.Equal(4_500_000, subscription.PeriodPrice.Amount);
        Assert.Equal(new DateOnly(2026, 3, 31), subscription.CurrentPeriod!.End);
        Assert.Equal(new DateOnly(2026, 4, 1), subscription.NextDueDate);
    }

    [Fact]
    public void Business_renewal_is_refused_without_validated_diagnostic()
    {
        var subscription = ActiveBusinessSubscription();
        var diagnostic = new Diagnostic(subscription.AccountId, subscription.Id, subscription.CurrentPeriod!, Now);

        var ex = Assert.Throws<DomainException>(() => subscription.PrepareRenewal(null));
        Assert.Equal("subscription.diagnostic_required", ex.Code);
        Assert.Throws<DomainException>(() => subscription.PrepareRenewal(diagnostic)); // planned, not validated
    }

    [Fact]
    public void Business_renewal_is_allowed_with_validated_diagnostic_of_the_current_period()
    {
        var subscription = ActiveBusinessSubscription();
        var diagnostic = ValidatedDiagnostic(subscription.Id, subscription.AccountId, subscription.CurrentPeriod!);

        var next = subscription.PrepareRenewal(diagnostic);

        Assert.Equal(new DateRange(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)), next);
    }

    [Fact]
    public void Diagnostic_of_another_period_does_not_unlock_renewal()
    {
        var subscription = ActiveBusinessSubscription();
        var oldPeriod = DateRange.OfMonths(new DateOnly(2025, 10, 1), 3);
        var diagnostic = ValidatedDiagnostic(subscription.Id, subscription.AccountId, oldPeriod);

        Assert.Throws<DomainException>(() => subscription.PrepareRenewal(diagnostic));
    }

    [Fact]
    public void Household_renewal_needs_no_diagnostic()
    {
        var subscription = Subscription.Create(Household(), HomePlan(), [Guid.CreateVersion7()], Now);
        subscription.Activate(Jan1, Now);

        Assert.Equal(new DateOnly(2026, 2, 1), subscription.PrepareRenewal(null).Start);
    }

    [Fact]
    public void Unpaid_subscription_goes_past_due_then_suspended_after_grace_period()
    {
        var subscription = Subscription.Create(Household(), HomePlan(), [Guid.CreateVersion7()], Now);
        subscription.Activate(Jan1, Now); // due 2026-02-01

        subscription.ApplyPaymentDeadlines(new DateOnly(2026, 2, 1), 7, Now);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        subscription.ApplyPaymentDeadlines(new DateOnly(2026, 2, 2), 7, Now);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.True(subscription.AllowsRequests);

        subscription.ApplyPaymentDeadlines(new DateOnly(2026, 2, 9), 7, Now);
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        Assert.False(subscription.AllowsRequests);
        Assert.Contains(subscription.DomainEvents, e => e is SubscriptionSuspended);
    }

    [Fact]
    public void Payment_of_next_period_reactivates_a_suspended_subscription()
    {
        var subscription = Subscription.Create(Household(), HomePlan(), [Guid.CreateVersion7()], Now);
        subscription.Activate(Jan1, Now);
        subscription.ApplyPaymentDeadlines(new DateOnly(2026, 3, 1), 7, Now);
        var next = subscription.PrepareRenewal(null);

        subscription.RecordPeriodPaid(next, Now);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(next, subscription.CurrentPeriod);
    }

    private static Subscription ActiveBusinessSubscription()
    {
        var subscription = Subscription.Create(Business(), BusinessPlan(), [Guid.CreateVersion7(), Guid.CreateVersion7()], Now);
        subscription.Activate(Jan1, Now);
        return subscription;
    }

    private static Diagnostic ValidatedDiagnostic(Guid subscriptionId, Guid accountId, DateRange period)
    {
        var diagnostic = new Diagnostic(accountId, subscriptionId, period, Now);
        diagnostic.Start(Guid.CreateVersion7(), Now);
        diagnostic.RecordControlPoint(null, ServiceDomain.Electricity, "Tableau électrique", ControlPointResult.Ok, null);
        diagnostic.Submit("RAS", null, Now);
        diagnostic.Validate(Guid.CreateVersion7(), Now);
        return diagnostic;
    }
}

public class InvoiceTests
{
    [Fact]
    public void Registration_plus_first_month_totals_18_500_fcfa()
    {
        var invoice = IssuedInvoice();

        Assert.Equal(18_500, invoice.Total.Amount);
    }

    [Fact]
    public void Invoice_is_paid_when_payments_cover_the_total()
    {
        var invoice = IssuedInvoice();
        invoice.ApplyPayment(Guid.CreateVersion7(), Money.FromFcfa(10_000), Now);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(8_500, invoice.Balance.Amount);

        invoice.ApplyPayment(Guid.CreateVersion7(), Money.FromFcfa(8_500), Now);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Contains(invoice.DomainEvents, e => e is InvoicePaid);
    }

    [Fact]
    public void Same_payment_is_never_counted_twice()
    {
        var invoice = IssuedInvoice();
        var paymentId = Guid.CreateVersion7();

        invoice.ApplyPayment(paymentId, Money.FromFcfa(10_000), Now);
        invoice.ApplyPayment(paymentId, Money.FromFcfa(10_000), Now);

        Assert.Equal(10_000, invoice.AmountPaid.Amount);
    }

    [Fact]
    public void Issued_invoice_cannot_be_modified() =>
        Assert.Throws<DomainException>(() =>
            IssuedInvoice().AddLine(new InvoiceLine(InvoiceLineKind.Other, "Extra", 1, Money.FromFcfa(1))));

    [Fact]
    public void Empty_invoice_cannot_be_issued() =>
        Assert.Throws<DomainException>(() =>
            new Invoice("FAC-2026-000002", Guid.CreateVersion7(), null, null, new DateOnly(2026, 1, 1)).Issue(Now));

    [Fact]
    public void Late_invoice_becomes_overdue()
    {
        var invoice = IssuedInvoice();

        invoice.MarkOverdueIfLate(new DateOnly(2026, 10, 20));

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
    }

    [Fact]
    public void Mobile_money_payment_succeeds_once_and_only_from_pending()
    {
        var payment = Payment.StartMobileMoney(Guid.CreateVersion7(), Money.FromFcfa(18_500), PaymentChannel.Wave, Now);

        payment.Succeed("WAVE-123", Now);
        payment.Succeed("WAVE-123", Now); // duplicate webhook

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Single(payment.DomainEvents);
        Assert.Throws<DomainException>(() => payment.Fail(Now));
    }

    [Fact]
    public void Manual_payment_is_only_for_cash_or_transfer_and_records_who()
    {
        var finance = Guid.CreateVersion7();
        var payment = Payment.RecordManual(Guid.CreateVersion7(), Money.FromFcfa(8_500), PaymentChannel.Cash, finance, "Reçu n°12", Now);

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(finance, payment.RecordedByUserId);
        Assert.Throws<DomainException>(() =>
            Payment.RecordManual(Guid.CreateVersion7(), Money.FromFcfa(1), PaymentChannel.Wave, finance, null, Now));
    }

    private static Invoice IssuedInvoice()
    {
        var invoice = new Invoice("FAC-2026-000001", Guid.CreateVersion7(), Guid.CreateVersion7(), null, new DateOnly(2026, 10, 15));
        invoice.AddLine(new InvoiceLine(InvoiceLineKind.RegistrationFee, "Inscription", 1, Money.FromFcfa(10_000)));
        invoice.AddLine(new InvoiceLine(InvoiceLineKind.SubscriptionPeriod, "Abonnement octobre", 1, Money.FromFcfa(8_500)));
        invoice.Issue(Now);
        return invoice;
    }
}
