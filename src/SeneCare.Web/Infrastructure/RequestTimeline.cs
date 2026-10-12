using Microsoft.Extensions.Localization;
using SeneCare.Application.Requests;
using SeneCare.Domain.Requests;
using SeneCare.Web.Localization;
using SeneCare.Web.ViewModels;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// Turns a request's status history into the 6 customer-facing steps:
/// received → analysis → appointment → on the way → intervention → validation.
/// </summary>
public static class RequestTimeline
{
    private static readonly (string LabelKey, RequestStatus[] Reached)[] Steps =
    [
        ("Timeline_Received", [RequestStatus.Submitted, RequestStatus.AwaitingApproval]),
        ("Timeline_Review", [RequestStatus.UnderReview]),
        ("Timeline_Appointment", [RequestStatus.AppointmentConfirmed]),
        ("Timeline_OnTheWay", [RequestStatus.OnTheWay]),
        ("Timeline_Work", [RequestStatus.Completed]),
        ("Timeline_Validated", [RequestStatus.ValidatedByCustomer]),
    ];

    public static IReadOnlyList<TimelineStep> Build(
        RequestStatus status, IReadOnlyList<StatusStep> history, DateTimeOffset? appointmentAt, IStringLocalizer<SharedResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(localizer);

        var current = status switch
        {
            RequestStatus.AwaitingApproval => 0,
            RequestStatus.Submitted or RequestStatus.UnderReview => 1,
            RequestStatus.AppointmentProposed or RequestStatus.AppointmentConfirmed => 2,
            RequestStatus.OnTheWay => 3,
            RequestStatus.InProgress => 4,
            RequestStatus.Completed or RequestStatus.Disputed => 5,
            RequestStatus.ValidatedByCustomer => Steps.Length,
            _ => -1,
        };

        var steps = new List<TimelineStep>(Steps.Length);
        for (var i = 0; i < Steps.Length; i++)
        {
            var (labelKey, reached) = Steps[i];
            var at = history.Where(h => reached.Contains(h.To)).Select(h => (DateTimeOffset?)h.At).FirstOrDefault();
            var state = i < current ? TimelineState.Done : i == current ? TimelineState.Current : TimelineState.Upcoming;
            var note = (i, status) switch
            {
                (0, RequestStatus.AwaitingApproval) => localizer["Timeline_Note_Approval"].Value,
                (2, RequestStatus.AppointmentProposed) => localizer["Timeline_Note_ChooseSlot"].Value,
                (2, RequestStatus.AppointmentConfirmed) when appointmentAt is { } date => localizer["Timeline_Note_AppointmentOn", Formatting.ShortDateTime(date)].Value,
                (4, RequestStatus.InProgress) => localizer["Timeline_Note_Working"].Value,
                (5, RequestStatus.Completed) => localizer["Timeline_Note_Validate"].Value,
                (5, RequestStatus.Disputed) => localizer["Timeline_Note_Disputed"].Value,
                _ => null,
            };

            // The appointment step shows the appointment date rather than when it was booked.
            if (i == 2 && appointmentAt is not null && state != TimelineState.Upcoming)
            {
                at = null;
            }

            steps.Add(new TimelineStep(localizer[labelKey], state, state == TimelineState.Upcoming ? null : at, note));
        }

        return steps;
    }
}
