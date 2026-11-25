using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Admin;

/// <summary>
/// Suspend / reactivate a customer account (super-admin, support; user decision of 25/11/2026). Suspended: its members
/// can no longer sign in (their open sessions close), nothing is deleted (homes, requests, invoices), and the account
/// leaves the customer list (filter « Archivés »). A member who is also SENECARE staff, or who still belongs to another
/// active account, keeps signing in but no longer reaches this one (<see cref="AccessControl"/>, BusinessAccess); its
/// pending invitations are withdrawn and no new one can be sent or accepted. Both actions are audited.
/// </summary>
public sealed class CustomerSuspension(IApplicationDbContext db, IIdentityService identity, TimeProvider clock)
{
    public async Task<Result<Guid>> SuspendAsync(Guid adminId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return Result<Guid>.Failure("account.not_found");
        }

        if (account.IsMember(adminId))
        {
            return Result<Guid>.Failure("account.suspend_self");
        }

        var now = clock.GetUtcNow();
        account.Suspend(now);
        var blocked = new List<Guid>();
        foreach (var user in await MembersAsync(account, cancellationToken))
        {
            if (user.IsActive && !user.IsStaff && !await ElsewhereAsync(user.Id, account.Id, cancellationToken))
            {
                user.Deactivate();
                blocked.Add(user.Id);
            }
        }

        // Pending invitations of the account are withdrawn (a manager invites again after the reactivation).
        (await db.AccountInvitations.Where(i => i.AccountId == account.Id && i.Status == InvitationStatus.Pending).ToListAsync(cancellationToken))
            .ForEach(i => i.Revoke());

        db.AuditLogs.Add(new AuditLog(adminId, "customer.suspend", nameof(CustomerAccount), account.Id.ToString(), false, null, account.DisplayName, now));
        await db.SaveChangesAsync(cancellationToken);
        foreach (var id in blocked)
        {
            await identity.EndSessionsAsync(id, cancellationToken);
        }

        return Result<Guid>.Success(account.Id);
    }

    public async Task<Result<Guid>> ReactivateAsync(Guid adminId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return Result<Guid>.Failure("account.not_found");
        }

        account.Reactivate();
        foreach (var user in await MembersAsync(account, cancellationToken))
        {
            // Erased people (law 2008-12) stay erased.
            if (!user.IsActive && user.AnonymizedAt is null)
            {
                user.Reactivate();
            }
        }

        db.AuditLogs.Add(new AuditLog(adminId, "customer.reactivate", nameof(CustomerAccount), account.Id.ToString(), false, null, account.DisplayName, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(account.Id);
    }

    private async Task<List<Domain.Users.User>> MembersAsync(CustomerAccount account, CancellationToken cancellationToken)
    {
        var ids = account.Members.Select(m => m.UserId).ToList();
        return await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken);
    }

    private Task<bool> ElsewhereAsync(Guid userId, Guid accountId, CancellationToken cancellationToken) =>
        db.CustomerAccounts.AnyAsync(a => a.Id != accountId && a.SuspendedAt == null && a.Members.Any(m => m.UserId == userId), cancellationToken);
}
