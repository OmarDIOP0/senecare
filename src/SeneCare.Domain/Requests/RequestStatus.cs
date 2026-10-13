namespace SeneCare.Domain.Requests;

public enum RequestStatus
{
    /// <summary>Reported by an employee, waiting for the site manager (business approval workflow).</summary>
    AwaitingApproval = 1,
    Submitted = 2,
    UnderReview = 3,
    AppointmentProposed = 4,
    AppointmentConfirmed = 5,
    OnTheWay = 6,
    InProgress = 7,
    Completed = 8,
    ValidatedByCustomer = 9,
    Disputed = 10,
    Cancelled = 11,

    /// <summary>Technician arrived (« Sur place »): the response time (SLA) stops here.</summary>
    OnSite = 12,
}

/// <summary>Allowed transitions of a service request. Anything not listed here is refused.</summary>
public static class RequestStateMachine
{
    private static readonly Dictionary<RequestStatus, RequestStatus[]> Transitions = new()
    {
        [RequestStatus.AwaitingApproval] = [RequestStatus.Submitted, RequestStatus.Cancelled],
        [RequestStatus.Submitted] = [RequestStatus.UnderReview, RequestStatus.Cancelled],
        [RequestStatus.UnderReview] =
        [
            RequestStatus.AppointmentProposed,
            RequestStatus.AppointmentConfirmed, // critical urgency: immediate dispatch
            RequestStatus.Cancelled,
        ],
        [RequestStatus.AppointmentProposed] =
        [
            RequestStatus.AppointmentProposed, // new slots
            RequestStatus.AppointmentConfirmed,
            RequestStatus.Cancelled,
        ],
        [RequestStatus.AppointmentConfirmed] =
        [
            RequestStatus.AppointmentProposed, // rescheduling
            RequestStatus.OnTheWay,
            RequestStatus.Cancelled,
        ],
        [RequestStatus.OnTheWay] = [RequestStatus.OnSite, RequestStatus.InProgress],
        [RequestStatus.OnSite] = [RequestStatus.InProgress],
        [RequestStatus.InProgress] = [RequestStatus.Completed],
        [RequestStatus.Completed] = [RequestStatus.ValidatedByCustomer, RequestStatus.Disputed],
        [RequestStatus.Disputed] = [RequestStatus.UnderReview],
        [RequestStatus.ValidatedByCustomer] = [],
        [RequestStatus.Cancelled] = [],
    };

    public static bool CanTransition(RequestStatus from, RequestStatus to) =>
        Transitions.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    public static IReadOnlyList<RequestStatus> NextStatuses(RequestStatus from) =>
        Transitions.TryGetValue(from, out var targets) ? targets : [];

    public static bool IsFinal(RequestStatus status) => NextStatuses(status).Count == 0;
}
