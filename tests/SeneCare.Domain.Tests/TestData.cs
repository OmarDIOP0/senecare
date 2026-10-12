using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Requests;

namespace SeneCare.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    public static readonly Guid UserId = Guid.CreateVersion7();

    public static Address Address() => new(Guid.CreateVersion7(), new GeoPoint(14.7167, -17.4677), "Derrière la mosquée, portail vert", null);

    public static Plan HomePlan() =>
        new("HOME", "Maisons & Villas", AccountKind.Household, Money.FromFcfa(10_000), Money.FromFcfa(8_500), 1, diagnosticRequired: false);

    public static Plan BusinessPlan() =>
        new("ESSENTIEL", "Essentiel", AccountKind.Business, Money.FromFcfa(75_000), Money.FromFcfa(1_500_000), 3, diagnosticRequired: true);

    public static CustomerAccount Household() => CustomerAccount.CreateHousehold("Famille Diop", UserId, Now);

    public static CustomerAccount Business() => CustomerAccount.CreateBusiness("Teranga Services SARL", "123456789", UserId, Now);

    public static ServiceRequest Request(Urgency urgency = Urgency.Normal, bool requiresApproval = false) =>
        ServiceRequest.Create(
            "SC-2026-000001",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            UserId,
            ServiceDomain.Plumbing,
            "Fuite sous l'évier de la cuisine",
            urgency,
            HomePlan().GetMaxDelay(urgency),
            requiresApproval,
            [],
            Now);

    public static TimeSlot Slot(int hoursFromNow, int durationHours = 2) =>
        new(Now.AddHours(hoursFromNow), Now.AddHours(hoursFromNow + durationHours));
}
