using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Web.ViewModels;

/// <summary>Senegalese phone field with fixed +221 prefix and « 77 123 45 67 » formatting.</summary>
public sealed record PhoneInputModel(string Name, string? Value, string Label, string? Error = null, string? Hint = null);

/// <summary>Searchable neighborhood picker (HTMX search, hidden input carries the id).</summary>
public sealed record NeighborhoodPickerModel(string Name, Guid? SelectedId = null, string? SelectedLabel = null, string? Error = null);

public sealed record InterventionCardModel(
    string Number,
    ServiceDomain Domain,
    string Title,
    RequestStatus Status,
    Urgency Urgency,
    string Url,
    DateTimeOffset? AppointmentAt = null,
    string? TechnicianName = null,
    string? TechnicianPhoneE164 = null);

public enum TimelineState
{
    Done = 1,
    Current = 2,
    Upcoming = 3,
}

public sealed record TimelineStep(string Label, TimelineState State, DateTimeOffset? At = null, string? Note = null);

public sealed record PhotoUploaderModel(string Name, string Label, int MaxPhotos = 6);

public enum EmptyIllustration
{
    House = 1,
    Tools = 2,
    Tree = 3,
    Phone = 4,
}

public sealed record EmptyStateModel(string Title, string Text, EmptyIllustration Illustration, string? ActionLabel = null, string? ActionUrl = null);

/// <summary>Bottom tab bar (mobile) / side rail (desktop), with the central action (« Signaler »).</summary>
public sealed record TabBarModel(SeneCare.Web.Infrastructure.AppNav Nav, string ActiveKey, bool Demo = false);
