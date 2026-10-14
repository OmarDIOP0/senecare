using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;
using static SeneCare.Domain.Tests.TestData;

namespace SeneCare.Domain.Tests;

public class ServiceRequestTests
{
    private static readonly Guid Dispatcher = Guid.CreateVersion7();
    private static readonly Guid Tech = Guid.CreateVersion7();

    [Theory]
    [InlineData(Urgency.Critical, 4)]
    [InlineData(Urgency.High, 24)]
    [InlineData(Urgency.Normal, 72)]
    public void Sla_deadline_follows_the_plan_delay_for_the_urgency(Urgency urgency, int hours)
    {
        var request = Request(urgency);

        Assert.Equal(Now.AddHours(hours), request.SlaDeadline);
    }

    [Fact]
    public void Planned_work_has_no_sla()
    {
        var request = Request(Urgency.Planned);

        Assert.Null(request.SlaDeadline);
        Assert.False(request.IsSlaBreached(Now.AddYears(1)));
    }

    [Fact]
    public void Sla_is_breached_when_nobody_started_before_the_deadline()
    {
        var request = Request(Urgency.Critical);

        Assert.False(request.IsSlaBreached(Now.AddHours(3)));
        Assert.True(request.IsSlaBreached(Now.AddHours(5)));
    }

    [Fact]
    public void Sla_is_met_when_work_started_in_time_even_if_checked_later()
    {
        var request = Request(Urgency.Critical);
        request.StartReview(Dispatcher, Now);
        request.DispatchImmediately(Slot(1), Dispatcher, Now);
        request.MarkOnTheWay(Tech, Now.AddMinutes(30));
        request.StartWork(Tech, Now.AddHours(2));

        Assert.False(request.IsSlaBreached(Now.AddDays(2)));
    }

    [Fact]
    public void Arrival_on_site_is_what_stops_the_sla_clock()
    {
        var request = Request(Urgency.Critical);
        request.StartReview(Dispatcher, Now);
        request.DispatchImmediately(Slot(1), Dispatcher, Now);
        request.MarkOnTheWay(Tech, Now.AddMinutes(30));
        request.MarkArrived(Tech, Now.AddHours(3));
        request.StartWork(Tech, Now.AddHours(5));

        Assert.Equal(RequestStatus.InProgress, request.Status);
        Assert.Equal(Now.AddHours(3), request.RespondedAt);
        Assert.False(request.IsSlaBreached(Now.AddDays(1)));
    }

    [Fact]
    public void Requalifying_restarts_the_sla_from_now()
    {
        var request = Request(Urgency.Normal);
        request.StartReview(Dispatcher, Now);

        request.Requalify(Urgency.Critical, TimeSpan.FromHours(4), Dispatcher, Now.AddHours(1));

        Assert.Equal(Urgency.Critical, request.Urgency);
        Assert.Equal(Now.AddHours(5), request.SlaDeadline);
    }

    [Fact]
    public void A_review_belongs_to_whoever_validated_and_consented_not_to_the_reporter()
    {
        var manager = Guid.NewGuid();
        var request = Request();
        request.StartReview(Dispatcher, Now);
        request.ProposeAppointment([Slot(24)], Dispatcher, Now);
        request.ConfirmAppointment(Slot(24), UserId, Now.AddHours(1));
        request.MarkOnTheWay(Tech, Now.AddHours(23));
        request.StartWork(Tech, Now.AddHours(24));
        request.Complete(Tech, Now.AddHours(25));

        // The employee (UserId) reported; the manager validates and agrees to publish.
        request.ValidateByCustomer(manager, new Rating(5, "Équipe sérieuse", Now.AddHours(26), publicationConsent: true), Now.AddHours(26));

        Assert.Equal(manager, request.Rating!.AuthorUserId);
        Assert.NotEqual(request.ReportedByUserId, request.Rating.AuthorUserId);
        Assert.True(request.Rating.IsPublic);

        // Erasure of the manager only: the review goes, the employee's request stays.
        request.RedactRating();
        Assert.False(request.Rating.IsPublic);
        Assert.Null(request.Rating.Comment);
        Assert.Equal(5, request.Rating.Score);
        Assert.NotEqual("[supprimé]", request.Description);
    }

    [Fact]
    public void Full_happy_path_is_recorded_in_history()
    {
        var request = Request();
        request.StartReview(Dispatcher, Now);
        var appointment = request.ProposeAppointment([Slot(24), Slot(48)], Dispatcher, Now);
        request.ConfirmAppointment(Slot(48), UserId, Now.AddHours(1));
        request.MarkOnTheWay(Tech, Now.AddHours(47));
        request.StartWork(Tech, Now.AddHours(48));
        request.Complete(Tech, Now.AddHours(50));
        request.ValidateByCustomer(UserId, new Rating(5, "Travail propre, merci", Now.AddHours(52)), Now.AddHours(52));

        Assert.Equal(RequestStatus.ValidatedByCustomer, request.Status);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(Slot(48), appointment.ChosenSlot);
        Assert.Equal(
            [
                RequestStatus.Submitted, RequestStatus.UnderReview, RequestStatus.AppointmentProposed,
                RequestStatus.AppointmentConfirmed, RequestStatus.OnTheWay, RequestStatus.InProgress,
                RequestStatus.Completed, RequestStatus.ValidatedByCustomer,
            ],
            request.History.Select(h => h.To));
        Assert.Equal(5, request.Rating!.Score);
    }

    [Fact]
    public void Invalid_transitions_are_refused()
    {
        var request = Request();

        var ex = Assert.Throws<DomainException>(() => request.Complete(Tech, Now));
        Assert.Equal("request.invalid_transition", ex.Code);
        Assert.Throws<DomainException>(() => request.MarkOnTheWay(Tech, Now));
        Assert.Equal(RequestStatus.Submitted, request.Status);
    }

    [Fact]
    public void Customer_must_choose_one_of_the_proposed_slots()
    {
        var request = Request();
        request.StartReview(Dispatcher, Now);
        request.ProposeAppointment([Slot(24)], Dispatcher, Now);

        Assert.Throws<DomainException>(() => request.ConfirmAppointment(Slot(30), UserId, Now));
    }

    [Fact]
    public void Proposed_slots_cannot_be_in_the_past()
    {
        var request = Request();
        request.StartReview(Dispatcher, Now);

        Assert.Throws<DomainException>(() => request.ProposeAppointment([Slot(-2)], Dispatcher, Now));
        Assert.Equal(RequestStatus.UnderReview, request.Status);
    }

    [Fact]
    public void Rescheduling_cancels_the_previous_appointment()
    {
        var request = Request();
        request.StartReview(Dispatcher, Now);
        var first = request.ProposeAppointment([Slot(24)], Dispatcher, Now);
        request.ConfirmAppointment(Slot(24), UserId, Now);

        var second = request.ProposeAppointment([Slot(72)], Dispatcher, Now);

        Assert.Equal(AppointmentStatus.Cancelled, first.Status);
        Assert.Same(second, request.CurrentAppointment);
    }

    [Fact]
    public void Approval_workflow_starts_the_sla_only_once_approved()
    {
        var request = Request(Urgency.High, requiresApproval: true);
        Assert.Equal(RequestStatus.AwaitingApproval, request.Status);
        Assert.Null(request.SlaDeadline);

        request.Approve(Dispatcher, TimeSpan.FromHours(24), Now.AddHours(2));

        Assert.Equal(RequestStatus.Submitted, request.Status);
        Assert.Equal(Now.AddHours(26), request.SlaDeadline);
    }

    [Fact]
    public void Dispute_requires_a_reason_and_can_be_reopened()
    {
        var request = CompletedRequest();

        Assert.Throws<DomainException>(() => request.Dispute(UserId, " ", Now));
        request.Dispute(UserId, "La fuite est revenue le soir même", Now);
        request.Reopen(Dispatcher, Now);

        Assert.Equal(RequestStatus.UnderReview, request.Status);
    }

    [Fact]
    public void Completed_request_is_auto_validated_after_the_configured_delay()
    {
        var request = CompletedRequest();

        Assert.False(request.TryAutoValidate(3, Now.AddDays(1)));
        Assert.True(request.TryAutoValidate(3, Now.AddDays(4)));
        Assert.Equal(RequestStatus.ValidatedByCustomer, request.Status);
    }

    [Fact]
    public void Status_changes_raise_domain_events()
    {
        var request = Request();
        request.StartReview(Dispatcher, Now);

        var change = Assert.Single(request.DomainEvents.OfType<ServiceRequestStatusChanged>());
        Assert.Equal(RequestStatus.UnderReview, change.To);
    }

    [Fact]
    public void Final_statuses_have_no_successor()
    {
        Assert.True(RequestStateMachine.IsFinal(RequestStatus.ValidatedByCustomer));
        Assert.True(RequestStateMachine.IsFinal(RequestStatus.Cancelled));
        Assert.All(
            Enum.GetValues<RequestStatus>().Where(s => !RequestStateMachine.IsFinal(s)),
            s => Assert.NotEmpty(RequestStateMachine.NextStatuses(s)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_must_be_between_1_and_5(int score) =>
        Assert.Throws<DomainException>(() => new Rating(score, null, Now));

    private static ServiceRequest CompletedRequest()
    {
        var request = Request();
        request.StartReview(Dispatcher, Now);
        request.DispatchImmediately(Slot(1), Dispatcher, Now);
        request.MarkOnTheWay(Tech, Now);
        request.StartWork(Tech, Now);
        request.Complete(Tech, Now);
        return request;
    }
}
