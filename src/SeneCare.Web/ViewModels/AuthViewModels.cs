using SeneCare.Application.Accounts;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;

namespace SeneCare.Web.ViewModels;

public sealed class SignInViewModel
{
    /// <summary>National number as typed (« 77 123 45 67 »); +221 is shown as a fixed prefix.</summary>
    public string? Phone { get; set; }

    public OtpChannel Channel { get; set; } = OtpChannel.Sms;
}

public sealed class CodeViewModel
{
    public string? Code { get; set; }

    public string Phone { get; set; } = string.Empty;

    public OtpChannel Channel { get; set; }

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
}

public sealed class TeamViewModel
{
    public string AccountName { get; set; } = string.Empty;

    public IReadOnlyList<TeamMemberDto> Members { get; set; } = [];

    public InviteMemberViewModel Invite { get; set; } = new();
}
