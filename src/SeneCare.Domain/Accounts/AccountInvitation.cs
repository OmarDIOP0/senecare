using SeneCare.Domain.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Domain.Accounts;

public enum InvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
}

/// <summary>
/// Invitation of an employee to a business account: sent by link (SMS/WhatsApp), accepted after an OTP
/// on the invited phone number. Only a hash of the link token is stored.
/// </summary>
public sealed class AccountInvitation : AggregateRoot
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly List<Guid> _propertyScope = [];

    private AccountInvitation()
    {
        PhoneNumber = null!;
        TokenHash = string.Empty;
    }

    private AccountInvitation(Guid accountId, PhoneNumber phoneNumber, AccountRole role, string tokenHash, Guid invitedByUserId, DateTimeOffset now)
    {
        AccountId = accountId;
        PhoneNumber = phoneNumber;
        Role = role;
        TokenHash = tokenHash;
        InvitedByUserId = invitedByUserId;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
        Status = InvitationStatus.Pending;
    }

    public Guid AccountId { get; private set; }

    public PhoneNumber PhoneNumber { get; private set; }

    public AccountRole Role { get; private set; }

    public string TokenHash { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public InvitationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public IReadOnlyCollection<Guid> PropertyScope => _propertyScope.AsReadOnly();

    /// <summary>Only managers of a business account can invite, and only to employee roles.</summary>
    public static AccountInvitation Create(
        CustomerAccount account,
        Guid invitedByUserId,
        PhoneNumber phoneNumber,
        AccountRole role,
        IEnumerable<Guid> propertyScope,
        string tokenHash,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (account.Kind != AccountKind.Business)
        {
            throw new DomainException("invitation.business_only", "Only business accounts can invite employees.");
        }

        if (account.FindMember(invitedByUserId) is not { Role: AccountRole.Manager })
        {
            throw new DomainException("invitation.not_manager", "Only a manager can invite employees.");
        }

        if (role == AccountRole.Owner)
        {
            throw new DomainException("account.role_not_allowed", "This role is not available for this type of account.");
        }

        var invitation = new AccountInvitation(account.Id, phoneNumber, role, tokenHash, invitedByUserId, now);
        invitation._propertyScope.AddRange(propertyScope.Distinct());
        return invitation;
    }

    public bool IsUsable(DateTimeOffset now) => Status == InvitationStatus.Pending && now <= ExpiresAt;

    /// <summary>
    /// Accepts the invitation for <paramref name="user"/> (whose phone was just verified by OTP)
    /// and adds them to <paramref name="account"/>.
    /// </summary>
    public void Accept(CustomerAccount account, User user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(user);

        if (account.Id != AccountId)
        {
            throw new DomainException("invitation.wrong_account", "This invitation belongs to another account.");
        }

        if (!IsUsable(now))
        {
            throw new DomainException("invitation.expired", "This invitation is no longer valid.");
        }

        if (user.PhoneNumber != PhoneNumber)
        {
            throw new DomainException("invitation.wrong_phone", "This invitation was sent to another phone number.");
        }

        if (!account.IsMember(user.Id))
        {
            account.AddMember(user.Id, Role, _propertyScope, now);
        }

        Status = InvitationStatus.Accepted;
        AcceptedByUserId = user.Id;
        AcceptedAt = now;
    }

    public void Revoke()
    {
        if (Status == InvitationStatus.Pending)
        {
            Status = InvitationStatus.Revoked;
        }
    }
}
