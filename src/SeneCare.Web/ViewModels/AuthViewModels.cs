using SeneCare.Application.Accounts;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;

namespace SeneCare.Web.ViewModels;

public sealed class SignInViewModel
{
    /// <summary>National number as typed (« 77 123 45 67 »); +221 is shown as a fixed prefix.</summary>
    public string? Phone { get; set; }

    /// <summary>WhatsApp, the channel everybody uses; the server falls back to SMS when it fails.</summary>
    public OtpChannel Channel { get; set; } = OtpChannel.WhatsApp;

    /// <summary>Step 2 of the sign-up (« Créer mon compte »): the number comes after the profile choice.</summary>
    public bool Signup { get; set; }

    public AccountKind? Profile { get; set; }
}

public sealed class CodeViewModel
{
    public string? Code { get; set; }

    public string Phone { get; set; } = string.Empty;

    public OtpChannel Channel { get; set; }

    /// <summary>Shown as step 2 of the sign-up instead of a plain sign-in.</summary>
    public bool Signup { get; set; }

    /// <summary>Development only (Otp:ShowCodeOnPage): the code, to type it without a phone.</summary>
    public string? DevelopmentCode { get; set; }

    public string FormattedPhone => PhoneNumber.TryParse(Phone, out var p) ? p.Formatted : Phone;
}

public sealed class RegistrationViewModel
{
    public string? FullName { get; set; }

    public AccountKind? Profile { get; set; }

    public string? CompanyName { get; set; }

    public bool AcceptsPrivacyPolicy { get; set; }

    public bool AcceptsWhatsApp { get; set; }

    /// <summary>Set when the user was invited: the profile choice is skipped.</summary>
    public string? JoiningAccountName { get; set; }

    /// <summary>SENECARE staff (admins, technicians) have no customer profile to choose.</summary>
    public bool IsStaff { get; set; }

    /// <summary>Offer chosen during sign-up (code of a plan) and, for monthly plans, yearly payment.</summary>
    public string? PlanCode { get; set; }

    public bool Annual { get; set; }

    /// <summary>Offers shown at the « Votre formule » step (prices from the database).</summary>
    public IReadOnlyList<SeneCare.Application.Public.PublicPlan> Plans { get; set; } = [];

    /// <summary>The verified phone number, shown read-only.</summary>
    public string? Phone { get; set; }

    /// <summary>Step shown first (0-based): 2 once the profile and number are done, 0 otherwise.</summary>
    public int StartStep { get; set; } = 2;

    /// <summary>The home / first site, entered during the sign-up (step 3); more can be added later.</summary>
    public PropertyFormModel Property { get; set; } = new();

    /// <summary>True when the person typed something at the « Votre logement » step.</summary>
    public bool HasProperty => !string.IsNullOrWhiteSpace(Property.Name) || Property.NeighborhoodId is not null;
}

public sealed class InvitationViewModel
{
    public string Token { get; set; } = string.Empty;

    public InvitationDetails? Invitation { get; set; }
}

public sealed class InviteMemberViewModel
{
    public string? Phone { get; set; }

    public AccountRole Role { get; set; } = AccountRole.Reporter;

    /// <summary>Sites the site manager / employee works on (ignored for a manager).</summary>
    public List<Guid> Sites { get; set; } = [];
}

public sealed class TeamViewModel
{
    public string AccountName { get; set; } = string.Empty;

    public IReadOnlyList<TeamMemberDto> Members { get; set; } = [];

    public InviteMemberViewModel Invite { get; set; } = new();

    public IReadOnlyList<TeamSite> Sites { get; set; } = [];

    public bool RequiresApproval { get; set; }

    public Guid CurrentUserId { get; set; }
}
