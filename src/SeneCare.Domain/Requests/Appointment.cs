using SeneCare.Domain.Common;

namespace SeneCare.Domain.Requests;

public enum AppointmentStatus
{
    Proposed = 1,
    Confirmed = 2,
    Cancelled = 3,
}

/// <summary>Slots proposed by the dispatcher for a request, and the one chosen by the customer.</summary>
public sealed class Appointment : Entity
{
    public const int MaxProposedSlots = 5;

    private readonly List<TimeSlot> _proposedSlots = [];

    private Appointment()
    {
    }

    public AppointmentStatus Status { get; private set; }

    public IReadOnlyCollection<TimeSlot> ProposedSlots => _proposedSlots.AsReadOnly();

    public TimeSlot? ChosenSlot { get; private set; }

    public Guid ProposedByUserId { get; private set; }

    public DateTimeOffset ProposedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    internal static Appointment Propose(IEnumerable<TimeSlot> slots, Guid proposedByUserId, DateTimeOffset now)
    {
        var list = slots.Distinct().OrderBy(s => s.Start).Select(s => s with { }).ToList();
        if (list.Count is 0 or > MaxProposedSlots)
        {
            throw new DomainException("appointment.slots", "Propose between 1 and 5 time slots.");
        }

        if (list.Exists(s => s.Start < now))
        {
            throw new DomainException("appointment.past_slot", "A proposed slot cannot be in the past.");
        }

        var appointment = new Appointment
        {
            Status = AppointmentStatus.Proposed,
            ProposedByUserId = proposedByUserId,
            ProposedAt = now,
        };
        appointment._proposedSlots.AddRange(list);
        return appointment;
    }

    internal void Confirm(TimeSlot chosenSlot, DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Proposed)
        {
            throw new DomainException("appointment.not_proposed", "This appointment is no longer open.");
        }

        if (!_proposedSlots.Contains(chosenSlot))
        {
            throw new DomainException("appointment.unknown_slot", "Choose one of the proposed slots.");
        }

        ChosenSlot = chosenSlot with { }; // own copy: value objects are never shared between owners (EF Core)
        ConfirmedAt = now;
        Status = AppointmentStatus.Confirmed;
    }

    internal void Cancel() => Status = AppointmentStatus.Cancelled;
}
