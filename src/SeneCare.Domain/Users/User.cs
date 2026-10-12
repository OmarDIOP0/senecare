using SeneCare.Domain.Common;

namespace SeneCare.Domain.Users;

/// <summary>Platform roles. Customer-account roles (owner, site manager…) live on <c>AccountMember</c>.</summary>
public enum PlatformRole
{
    Customer = 1,
    Technician = 2,
    SuperAdmin = 10,
    Dispatcher = 11,
    Finance = 12,
    Support = 13,
}

public enum ConsentType
{
    /// <summary>Processing of personal data (law 2008-12) — required to use the service.</summary>
    PersonalData = 1,
    WhatsAppMessages = 2,
    Marketing = 3,
}

public sealed record UserRole(PlatformRole Role, DateTimeOffset GrantedAt);

public sealed record UserConsent(ConsentType Type, string PolicyVersion, DateTimeOffset GivenAt, DateTimeOffset? WithdrawnAt = null)
{
    public bool IsActive => WithdrawnAt is null;
}

/// <summary>A person using SENECARE, identified by their phone number. Shares its Id with the Identity user.</summary>
public sealed class User : AggregateRoot
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 256;

    private readonly List<UserRole> _roles = [];
    private readonly List<UserConsent> _consents = [];

    private User()
    {
        PhoneNumber = null!;
    }

    private User(Guid id, PhoneNumber phoneNumber, DateTimeOffset now)
        : base(id)
    {
        PhoneNumber = phoneNumber;
        CreatedAt = now;
        IsActive = true;
        Language = Language.French;
    }

    public PhoneNumber PhoneNumber { get; private set; }

    public string? FullName { get; private set; }

    public string? Email { get; private set; }

    public Language Language { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public IReadOnlyCollection<UserConsent> Consents => _consents.AsReadOnly();

    public static User Register(PhoneNumber phoneNumber, DateTimeOffset now, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(phoneNumber);
        return new User(id ?? Guid.CreateVersion7(), phoneNumber, now);
    }

    public void UpdateProfile(string fullName, string? email, Language language)
    {
        FullName = Guard.Required(fullName, "user.name", NameMaxLength);
        Email = Guard.Optional(email, "user.email", EmailMaxLength);
        Language = language;
    }

    public void ChangePhoneNumber(PhoneNumber phoneNumber)
    {
        ArgumentNullException.ThrowIfNull(phoneNumber);
        PhoneNumber = phoneNumber;
    }

    public bool HasRole(PlatformRole role) => _roles.Exists(r => r.Role == role);

    public void GrantRole(PlatformRole role, DateTimeOffset now)
    {
        if (!HasRole(role))
        {
            _roles.Add(new UserRole(role, now));
        }
    }

    public void RevokeRole(PlatformRole role) => _roles.RemoveAll(r => r.Role == role);

    public bool HasActiveConsent(ConsentType type) => _consents.Exists(c => c.Type == type && c.IsActive);

    public void GiveConsent(ConsentType type, string policyVersion, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        WithdrawConsent(type, now);
        _consents.Add(new UserConsent(type, policyVersion, now));
    }

    public void WithdrawConsent(ConsentType type, DateTimeOffset now)
    {
        for (var i = 0; i < _consents.Count; i++)
        {
            if (_consents[i].Type == type && _consents[i].IsActive)
            {
                _consents[i] = _consents[i] with { WithdrawnAt = now };
            }
        }
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
