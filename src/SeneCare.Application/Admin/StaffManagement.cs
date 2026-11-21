using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Auth;
using SeneCare.Application.Common;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Admin;

/// <summary>A member of the SENECARE team: admin roles, and whether they are also a technician.</summary>
public sealed record StaffMember(
    Guid UserId,
    string? Name,
    string Phone,
    IReadOnlyList<PlatformRole> AdminRoles,
    Guid? TechnicianId,
    bool IsActive);

/// <summary>Result of adding a member: the temporary password when the person had none (shown once).</summary>
public sealed record StaffAdded(Guid UserId, string Phone, string? Name, string? TemporaryPassword);

/// <summary>
/// « Équipe SeneCare » (super-admins only): who runs the platform and with which rights. One account per person,
/// roles add up: an admin can also be a technician (their technician file — trades, zones, hours — is kept in
/// Admin › Techniciens). Rules: nobody removes their own rights, there is always a super-admin left, and a change
/// of rights ends the person's sessions at once (they sign in again with the new rights). Every change is audited.
/// Temporary passwords only in password sign-in (Auth:Mode = Password, the default); with codes there is none.
/// </summary>
public sealed class StaffManagement(IApplicationDbContext db, IIdentityService identity, IOptions<AuthOptions> auth, TimeProvider clock)
{
    private bool UsesPasswords => !auth.Value.UsesOtp;

    /// <summary>The roles given on this screen (technician and customer have their own screens).</summary>
    public static readonly IReadOnlyList<PlatformRole> AdminRoles =
        [PlatformRole.SuperAdmin, PlatformRole.Dispatcher, PlatformRole.Finance, PlatformRole.Support];

    public async Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.AnonymizedAt == null && u.Roles.Any(r => AdminRoles.Contains(r.Role)))
            .ToListAsync(cancellationToken);
        var ids = users.Select(u => u.Id).ToList();
        var technicians = await db.Technicians.AsNoTracking()
            .Where(t => ids.Contains(t.UserId))
            .Select(t => new { t.UserId, t.Id })
            .ToDictionaryAsync(t => t.UserId, t => t.Id, cancellationToken);

        return [.. users
            .Select(u => new StaffMember(
                u.Id,
                u.FullName,
                u.PhoneNumber.Formatted,
                [.. AdminRoles.Where(u.HasRole)],
                technicians.TryGetValue(u.Id, out var technicianId) ? technicianId : null,
                u.IsActive))
            .OrderByDescending(m => m.AdminRoles.Contains(PlatformRole.SuperAdmin))
            .ThenBy(m => m.Name ?? m.Phone)];
    }

    /// <summary>
    /// Adds a member (new or existing account, e.g. a technician or a customer of SENECARE). A person without a
    /// password gets a temporary one, to be changed at the first sign-in.
    /// </summary>
    public async Task<Result<StaffAdded>> AddAsync(Guid adminId, string? phone, string? fullName, IReadOnlyCollection<PlatformRole> roles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (!PhoneNumber.TryParse(phone, out var number))
        {
            return Result<StaffAdded>.Failure("phone.invalid");
        }

        var granted = roles.Where(AdminRoles.Contains).Distinct().ToList();
        if (granted.Count == 0)
        {
            return Result<StaffAdded>.Failure("staff.role_required");
        }

        var now = clock.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == number, cancellationToken);
        if (user is { AnonymizedAt: not null })
        {
            return Result<StaffAdded>.Failure("user.not_found");
        }

        if (user is null)
        {
            user = User.Register(number, now);
            db.Users.Add(user);
        }

        if (!string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(user.FullName))
        {
            user.UpdateProfile(fullName.Trim(), user.Email, user.Language);
        }

        granted.ForEach(role => user.GrantRole(role, now));
        db.AuditLogs.Add(new AuditLog(adminId, "staff.add", nameof(User), user.Id.ToString(), false, null, string.Join(',', granted), now));
        await db.SaveChangesAsync(cancellationToken);

        await identity.EnsureIdentityUserAsync(user, cancellationToken);
        string? temporary = null;
        if (UsesPasswords && !await identity.HasPasswordAsync(user.Id, cancellationToken))
        {
            temporary = PasswordRules.Temporary();
            if (await identity.SetPasswordAsync(user.Id, temporary, mustChange: true, cancellationToken) is { } error)
            {
                return Result<StaffAdded>.Failure(error);
            }
        }
        else
        {
            // Already signed in somewhere: the new rights apply at the next sign-in.
            await identity.EndSessionsAsync(user.Id, cancellationToken);
        }

        return Result<StaffAdded>.Success(new StaffAdded(user.Id, user.PhoneNumber.Formatted, user.FullName, temporary));
    }

    /// <summary>Replaces the admin roles of a member (technician / customer roles are not touched).</summary>
    public async Task<Result<Guid>> SetRolesAsync(Guid adminId, Guid userId, IReadOnlyCollection<PlatformRole> roles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var wanted = roles.Where(AdminRoles.Contains).Distinct().ToList();
        if (wanted.Count == 0)
        {
            // No admin role left: that is « Retirer de l'équipe », with its own confirmation.
            return Result<Guid>.Failure("staff.role_required");
        }

        return await ChangeAsync(adminId, userId, wanted, "staff.roles", cancellationToken);
    }

    /// <summary>« Retirer de l'équipe »: every admin role removed (a technician stays a technician).</summary>
    public Task<Result<Guid>> RemoveAsync(Guid adminId, Guid userId, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, userId, [], "staff.remove", cancellationToken);

    /// <summary>
    /// Temporary password for a team member or a technician (the customer screen refuses staff accounts). Not for
    /// oneself: « Changer mon mot de passe » is there for that. Logged as sensitive.
    /// </summary>
    public async Task<Result<StaffAdded>> ResetPasswordAsync(Guid adminId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == adminId)
        {
            return Result<StaffAdded>.Failure("staff.self");
        }

        if (!UsesPasswords)
        {
            return Result<StaffAdded>.Failure("staff.otp_mode");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsStaff || user.AnonymizedAt is not null)
        {
            return Result<StaffAdded>.Failure("user.not_found");
        }

        await identity.EnsureIdentityUserAsync(user, cancellationToken);
        var temporary = PasswordRules.Temporary();
        if (await identity.SetPasswordAsync(user.Id, temporary, mustChange: true, cancellationToken) is { } error)
        {
            return Result<StaffAdded>.Failure(error);
        }

        db.AuditLogs.Add(new AuditLog(adminId, "staff.password_reset", nameof(User), user.Id.ToString(), true, null, null, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<StaffAdded>.Success(new StaffAdded(user.Id, user.PhoneNumber.Formatted, user.FullName, temporary));
    }

    /// <summary>
    /// Technician file (Admin › Techniciens): a temporary password to give them, always (« Nouveau mot de passe
    /// provisoire ») or only when they have none yet (just created: otherwise they could never sign in).
    /// </summary>
    public async Task<Result<StaffAdded>> TechnicianPasswordAsync(Guid adminId, Guid technicianId, bool onlyIfNone, CancellationToken cancellationToken = default)
    {
        var userId = await db.Technicians.AsNoTracking().Where(t => t.Id == technicianId).Select(t => (Guid?)t.UserId).FirstOrDefaultAsync(cancellationToken);
        if (userId is not { } id)
        {
            return Result<StaffAdded>.Failure("user.not_found");
        }

        if (onlyIfNone && (!UsesPasswords || await identity.HasPasswordAsync(id, cancellationToken)))
        {
            return Result<StaffAdded>.Success(new StaffAdded(id, string.Empty, null, null));
        }

        return await ResetPasswordAsync(adminId, id, cancellationToken);
    }

    private async Task<Result<Guid>> ChangeAsync(Guid adminId, Guid userId, IReadOnlyList<PlatformRole> wanted, string action, CancellationToken cancellationToken)
    {
        if (userId == adminId)
        {
            return Result<Guid>.Failure("staff.self");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !AdminRoles.Any(user.HasRole))
        {
            return Result<Guid>.Failure("user.not_found");
        }

        // Never the last super-admin (the platform would have nobody to manage it).
        if (user.HasRole(PlatformRole.SuperAdmin) && !wanted.Contains(PlatformRole.SuperAdmin)
            && !await db.Users.AnyAsync(u => u.Id != userId && u.AnonymizedAt == null && u.IsActive && u.Roles.Any(r => r.Role == PlatformRole.SuperAdmin), cancellationToken))
        {
            return Result<Guid>.Failure("staff.last_super_admin");
        }

        var now = clock.GetUtcNow();
        foreach (var role in AdminRoles)
        {
            if (wanted.Contains(role))
            {
                user.GrantRole(role, now);
            }
            else
            {
                user.RevokeRole(role);
            }
        }

        db.AuditLogs.Add(new AuditLog(adminId, action, nameof(User), user.Id.ToString(), false, null, string.Join(',', wanted), now));
        await db.SaveChangesAsync(cancellationToken);
        // The rights are in the session cookie: end the sessions so they apply now, not at the next sign-in.
        await identity.EndSessionsAsync(user.Id, cancellationToken);
        return Result<Guid>.Success(user.Id);
    }
}
