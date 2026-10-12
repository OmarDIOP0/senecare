using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Auth;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Users;

public sealed record MembershipDto(Guid AccountId, string AccountName, AccountKind Kind, AccountRole Role);

public sealed record UserProfileDto(
    Guid UserId,
    string Phone,
    string? FullName,
    IReadOnlyList<PlatformRole> Roles,
    IReadOnlyList<MembershipDto> Memberships,
    bool IsRegistrationComplete);

public sealed class GetUserProfileHandler(IApplicationDbContext db)
{
    public async Task<UserProfileDto?> HandleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var accounts = await db.CustomerAccounts.AsNoTracking()
            .Where(a => a.Members.Any(m => m.UserId == userId))
            .ToListAsync(cancellationToken);
        var memberships = accounts
            .Select(a => new MembershipDto(a.Id, a.DisplayName, a.Kind, a.FindMember(userId)!.Role))
            .ToList();

        return new UserProfileDto(
            user.Id,
            user.PhoneNumber.Formatted,
            user.FullName,
            [.. user.Roles.Select(r => r.Role)],
            memberships,
            RegistrationStatus.IsComplete(user, memberships.Count > 0));
    }
}

public sealed record TeamMemberDto(Guid UserId, string? FullName, string Phone, AccountRole Role);

public sealed record TeamDto(string AccountName, IReadOnlyList<TeamMemberDto> Members);

/// <summary>Members of a business account (team page).</summary>
public sealed class GetTeamHandler(IApplicationDbContext db)
{
    public async Task<TeamDto?> HandleAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var userIds = account.Members.Select(m => m.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
        return new TeamDto(account.DisplayName, [.. account.Members
            .Where(m => users.ContainsKey(m.UserId))
            .Select(m => new TeamMemberDto(m.UserId, users[m.UserId].FullName, users[m.UserId].PhoneNumber.Formatted, m.Role))
            .OrderBy(m => m.Role)
            .ThenBy(m => m.FullName, StringComparer.CurrentCulture)]);
    }
}
