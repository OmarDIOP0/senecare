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

public sealed record PhotoUploaderModel(string Name, string Label, int MaxPhotos = 6, string? Id = null);

public enum EmptyIllustration
{
    House = 1,
    Tools = 2,
    Tree = 3,
    Phone = 4,
}

public sealed record EmptyStateModel(string Title, string Text, EmptyIllustration Illustration, string? ActionLabel = null, string? ActionUrl = null);

/// <summary>Bottom tab bar (mobile) / side rail (desktop), with the central action (« Signaler »).</summary>
/// <summary>Everything the app shell shows: navigation, who is signed in, organisation, unread notifications.</summary>
public sealed record ShellModel(
    SeneCare.Web.Infrastructure.AppNav Nav,
    string ActiveKey,
    string? PageTitle,
    SeneCare.Application.Users.UserProfileDto? Profile,
    int UnreadNotifications)
{
    public string DisplayName => Profile?.FullName ?? (Profile is null ? string.Empty : "+221 " + Profile.Phone);

    /// <summary>« Awa Ndiaye » → « AN ».</summary>
    public string Initials
    {
        get
        {
            var words = (Profile?.FullName ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    public SeneCare.Web.Infrastructure.NavItem? ActiveItem =>
        Nav.Items.FirstOrDefault(i => i.Key == ActiveKey) ?? (ActiveKey == SeneCare.Web.Infrastructure.AppNavigation.Settings.Key ? SeneCare.Web.Infrastructure.AppNavigation.Settings : null);
}

/// <summary>Technician form fields: empty for a new technician, filled when editing.</summary>
public sealed record TechnicianFieldsModel(SeneCare.Application.Admin.TechnicianRow? Row);

/// <summary>Preventive advice form (new when <see cref="Id"/> is null).</summary>
public sealed record AdviceFormModel(Guid? Id, string? Title, string? Body, IReadOnlyCollection<int> Months, SeneCare.Domain.Common.ServiceDomain? Domain);

/// <summary>Page title + grey description + main action on the right.</summary>
public sealed record PageHeaderModel(string Title, string? Description = null, string? ActionLabel = null, string? ActionUrl = null, string? ActionIcon = null);

public sealed record ChecklistStep(string Label, string Url, bool Done);

/// <summary>Start-up checklist (« 1/4 ») shown instead of figures on a new account.</summary>
public sealed record ChecklistModel(string Title, IReadOnlyList<ChecklistStep> Steps)
{
    public int Done => Steps.Count(s => s.Done);
}

/// <summary>Photo of the public site; falls back to a named placeholder until the file is added.</summary>
public sealed record PhotoModel(string Path, string Alt, string Class, int Width = 900, int Height = 675, string? Icon = null, bool Eager = false);

/// <summary>A customer review card (published review or sample content).</summary>
public sealed record ReviewCardModel(string Author, string Place, DateTimeOffset Date, int Rating, string Trade, string Text);

/// <summary>Header of a public page: breadcrumb, title, introduction, optional photo.</summary>
public sealed record SiteHeroModel(string Title, string Text, string? Crumb = null, PhotoModel? Photo = null, string? ActionLabel = null, string? ActionUrl = null);

/// <summary>Progress bars and « ÉTAPE 2/5 — VOTRE NUMÉRO » title of the sign-up steps.</summary>
public sealed record WizardHeadingModel(int Step, int Total, string Label, string Title, string? Text = null);

/// <summary>Trade illustration (icon on a blue tile) used where a trade photo would go.</summary>
public sealed record TradeArtModel(SeneCare.Domain.Common.ServiceDomain Domain, string Label, string Class, bool Large = false);
