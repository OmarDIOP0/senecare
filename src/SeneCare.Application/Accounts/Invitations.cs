using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Resources;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Accounts;

/// <param name="InvitationUrlTemplate">Absolute link with « {0} » replaced by the token, e.g. « https://senecare.sn/invitation/{0} ».</param>
public sealed record InviteMemberCommand(
    Guid InviterUserId,
    Guid AccountId,
    string? Phone,
    AccountRole Role,
    IReadOnlyCollection<Guid> PropertyScope,
    string InvitationUrlTemplate);

public sealed record InvitationSent(Guid InvitationId, PhoneNumber Phone, DateTimeOffset ExpiresAt);

/// <summary>A business manager invites an employee by phone; the link arrives by WhatsApp or SMS.</summary>
public sealed class InviteMemberHandler(
    IApplicationDbContext db,
    ISecretHasher hasher,
    ISmsSender sms,
    IWhatsAppSender whatsApp,
    TimeProvider clock)
{
    public async Task<Result<InvitationSent>> HandleAsync(InviteMemberCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!PhoneNumber.TryParse(command.Phone, out var phone))
        {
            return Result<InvitationSent>.Failure("phone.invalid");
        }

        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<InvitationSent>.Failure("account.not_found");
        }

        var alreadyMember = await db.Users.AnyAsync(u => u.PhoneNumber == phone && account.Members.Select(m => m.UserId).Contains(u.Id), cancellationToken);
        if (alreadyMember)
        {
            return Result<InvitationSent>.Failure("account.member_exists");
        }

        var now = clock.GetUtcNow();
        var token = Secrets.UrlToken();
        AccountInvitation invitation;
        try
        {
            invitation = AccountInvitation.Create(account, command.InviterUserId, phone, command.Role, command.PropertyScope, hasher.Hash(token), now);
        }
        catch (DomainException ex)
        {
            return Result<InvitationSent>.Failure(ex.Code);
        }

        // A new invitation replaces pending ones for the same number.
        var pending = await db.AccountInvitations
            .Where(i => i.AccountId == account.Id && i.PhoneNumber == phone && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken);
        pending.ForEach(i => i.Revoke());

        db.AccountInvitations.Add(invitation);
        db.AuditLogs.Add(new AuditLog(command.InviterUserId, "account.invite", nameof(CustomerAccount), account.Id.ToString(), false, null, phone.E164, now));
        await db.SaveChangesAsync(cancellationToken);

        var link = string.Format(System.Globalization.CultureInfo.InvariantCulture, command.InvitationUrlTemplate, token);
        var message = Messages.Invitation(account.DisplayName, link, Language.French);
        try
        {
            await whatsApp.SendAsync(phone, message, cancellationToken);
        }
#pragma warning disable CA1031 // Any WhatsApp failure must fall back to SMS.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            await sms.SendAsync(phone, message, cancellationToken);
        }

        return Result<InvitationSent>.Success(new InvitationSent(invitation.Id, phone, invitation.ExpiresAt));
    }
}

public sealed record InvitationDetails(Guid InvitationId, string CompanyName, AccountRole Role, PhoneNumber Phone, bool IsUsable);

/// <summary>Reads an invitation from its link token (public page before sign-in).</summary>
public sealed class GetInvitationHandler(IApplicationDbContext db, ISecretHasher hasher, TimeProvider clock)
{
    public async Task<InvitationDetails?> HandleAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return null;
        }

        var hash = hasher.Hash(token);
        var invitation = await db.AccountInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invitation is null)
        {
            return null;
        }

        var companyName = await db.CustomerAccounts.Where(a => a.Id == invitation.AccountId).Select(a => a.DisplayName).FirstAsync(cancellationToken);
        return new InvitationDetails(invitation.Id, companyName, invitation.Role, invitation.PhoneNumber, invitation.IsUsable(clock.GetUtcNow()));
    }
}

public sealed record AcceptInvitationCommand(Guid InvitationId, Guid UserId);

/// <summary>After the OTP on the invited number: joins the business account.</summary>
public sealed class AcceptInvitationHandler(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<Result<Guid>> HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invitation = await db.AccountInvitations.FirstOrDefaultAsync(i => i.Id == command.InvitationId, cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (invitation is null || user is null)
        {
            return Result<Guid>.Failure("invitation.expired");
        }

        var account = await db.CustomerAccounts.FirstAsync(a => a.Id == invitation.AccountId, cancellationToken);
        try
        {
            invitation.Accept(account, user, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(account.Id);
    }
}
