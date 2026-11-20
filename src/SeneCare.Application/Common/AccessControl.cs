using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Accounts;

namespace SeneCare.Application.Common;

/// <summary>
/// Which customer data a user may see. Every client use case goes through here: a customer never
/// reaches another customer's properties, requests or files (unknown ids look like « not found »).
/// One instance per request: the answers are kept for the request, so a page made of several blocks (home,
/// requests, subscription, neighborhood) asks the database once instead of once per block. Changes of access
/// (new home, invitation, removal) end with a redirect, i.e. a new request: nothing stale is ever read.
/// </summary>
public sealed class AccessControl(IApplicationDbContext db)
{
    private readonly Dictionary<Guid, IReadOnlyList<CustomerAccount>> _accounts = [];
    private readonly Dictionary<Guid, IReadOnlyList<Guid>> _propertyIds = [];

    public async Task<IReadOnlyList<CustomerAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!_accounts.TryGetValue(userId, out var accounts))
        {
            accounts = await db.CustomerAccounts.Where(a => a.Members.Any(m => m.UserId == userId)).ToListAsync(cancellationToken);
            _accounts[userId] = accounts;
        }

        return accounts;
    }

    /// <summary>Properties the user can see: all of the account for owners/managers, their scope otherwise.</summary>
    public async Task<IReadOnlyList<Guid>> PropertyIdsOfAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_propertyIds.TryGetValue(userId, out var known))
        {
            return known;
        }

        var accounts = await AccountsOfAsync(userId, cancellationToken);
        var fullAccessAccounts = accounts.Where(a => a.FindMember(userId)!.HasFullAccess).Select(a => a.Id).ToList();
        var scoped = accounts.Where(a => !a.FindMember(userId)!.HasFullAccess).SelectMany(a => a.FindMember(userId)!.PropertyScope).ToList();

        var owned = await db.Properties
            .Where(p => fullAccessAccounts.Contains(p.AccountId))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        IReadOnlyList<Guid> ids = [.. owned.Concat(scoped).Distinct()];
        _propertyIds[userId] = ids;
        return ids;
    }

    /// <summary>The property if the user may access it, otherwise null.</summary>
    public async Task<Property?> PropertyAsync(Guid userId, Guid propertyId, CancellationToken cancellationToken = default)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Id == property.AccountId, cancellationToken);
        return account?.FindMember(userId)?.CanAccess(property.Id) == true ? property : null;
    }

    public async Task<bool> CanAccessAccountAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default) =>
        await db.CustomerAccounts.AnyAsync(a => a.Id == accountId && a.Members.Any(m => m.UserId == userId), cancellationToken);
}
