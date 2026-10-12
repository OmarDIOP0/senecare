using SeneCare.Domain.Common;

namespace SeneCare.Domain.Accounts;

public enum AccountKind
{
    /// <summary>« Ma maison » — individual or family (also diaspora).</summary>
    Household = 1,

    /// <summary>« Mon entreprise » — company with one or several sites.</summary>
    Business = 2,
}

public enum AccountRole
{
    /// <summary>Holder of the account (household) — full rights.</summary>
    Owner = 1,

    /// <summary>« Gestionnaire » of a business account — full rights.</summary>
    Manager = 2,

    /// <summary>« Responsable de site » — manages and approves requests on their sites.</summary>
    SiteManager = 3,

    /// <summary>« Employé déclarant » — reports issues on their sites.</summary>
    Reporter = 4,
}

/// <summary>Member of a customer account. Site managers and reporters are limited to <see cref="PropertyScope"/>.</summary>
public sealed class AccountMember : Entity
{
    private readonly List<Guid> _propertyScope = [];

    private AccountMember()
    {
    }

    internal AccountMember(Guid userId, AccountRole role, DateTimeOffset now)
    {
        UserId = userId;
        Role = role;
        JoinedAt = now;
    }

    public Guid UserId { get; private set; }

    public AccountRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public IReadOnlyCollection<Guid> PropertyScope => _propertyScope.AsReadOnly();

    public bool HasFullAccess => Role is AccountRole.Owner or AccountRole.Manager;

    public bool CanAccess(Guid propertyId) => HasFullAccess || _propertyScope.Contains(propertyId);

    internal void ChangeRole(AccountRole role) => Role = role;

    internal void SetScope(IEnumerable<Guid> propertyIds)
    {
        _propertyScope.Clear();
        _propertyScope.AddRange(propertyIds.Distinct());
    }
}

/// <summary>Customer account (household or business). Owns properties/sites, subscriptions and invoices.</summary>
public sealed class CustomerAccount : AggregateRoot
{
    public const int NameMaxLength = 160;

    private readonly List<AccountMember> _members = [];

    private CustomerAccount()
    {
        DisplayName = string.Empty;
    }

    private CustomerAccount(AccountKind kind, string displayName, DateTimeOffset now)
    {
        Kind = kind;
        DisplayName = Guard.Required(displayName, "account.name", NameMaxLength);
        CreatedAt = now;
    }

    public AccountKind Kind { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Senegalese company identifier (NINEA), businesses only.</summary>
    public string? Ninea { get; private set; }

    /// <summary>When on, reporters' requests wait for a site manager's approval (Phase 7).</summary>
    public bool RequiresRequestApproval { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<AccountMember> Members => _members.AsReadOnly();

    public static CustomerAccount CreateHousehold(string displayName, Guid ownerUserId, DateTimeOffset now)
    {
        var account = new CustomerAccount(AccountKind.Household, displayName, now);
        account._members.Add(new AccountMember(ownerUserId, AccountRole.Owner, now));
        return account;
    }

    public static CustomerAccount CreateBusiness(string companyName, string? ninea, Guid managerUserId, DateTimeOffset now)
    {
        var account = new CustomerAccount(AccountKind.Business, companyName, now)
        {
            Ninea = Guard.Optional(ninea, "account.ninea", 20),
        };
        account._members.Add(new AccountMember(managerUserId, AccountRole.Manager, now));
        return account;
    }

    public AccountMember? FindMember(Guid userId) => _members.Find(m => m.UserId == userId);

    public bool IsMember(Guid userId) => FindMember(userId) is not null;

    public AccountMember AddMember(Guid userId, AccountRole role, IEnumerable<Guid> propertyScope, DateTimeOffset now)
    {
        if (IsMember(userId))
        {
            throw new DomainException("account.member_exists", "This person is already a member of the account.");
        }

        EnsureRoleAllowed(role);
        var member = new AccountMember(userId, role, now);
        member.SetScope(propertyScope);
        _members.Add(member);
        return member;
    }

    public void ChangeMemberRole(Guid userId, AccountRole role, IEnumerable<Guid> propertyScope)
    {
        var member = FindMember(userId) ?? throw new DomainException("account.member_not_found", "Member not found.");
        EnsureRoleAllowed(role);
        if (member.HasFullAccess && role is not (AccountRole.Owner or AccountRole.Manager))
        {
            EnsureAnotherAdminRemains(userId);
        }

        member.ChangeRole(role);
        member.SetScope(propertyScope);
    }

    public void RemoveMember(Guid userId)
    {
        var member = FindMember(userId) ?? throw new DomainException("account.member_not_found", "Member not found.");
        if (member.HasFullAccess)
        {
            EnsureAnotherAdminRemains(userId);
        }

        _members.Remove(member);
    }

    public void Rename(string displayName) => DisplayName = Guard.Required(displayName, "account.name", NameMaxLength);

    public void SetRequestApproval(bool required)
    {
        if (required && Kind != AccountKind.Business)
        {
            throw new DomainException("account.approval_business_only", "Approval workflow is for business accounts.");
        }

        RequiresRequestApproval = required;
    }

    private void EnsureRoleAllowed(AccountRole role)
    {
        var allowed = Kind == AccountKind.Household
            ? role is AccountRole.Owner
            : role is not AccountRole.Owner;
        if (!allowed)
        {
            throw new DomainException("account.role_not_allowed", "This role is not available for this type of account.");
        }
    }

    private void EnsureAnotherAdminRemains(Guid leavingUserId)
    {
        if (!_members.Exists(m => m.UserId != leavingUserId && m.HasFullAccess))
        {
            throw new DomainException("account.last_admin", "An account must keep at least one administrator.");
        }
    }
}
