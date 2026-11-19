using Microsoft.AspNetCore.Identity;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Auth;
using Microsoft.EntityFrameworkCore;
using SeneCare.Domain.Users;
using SeneCare.Infrastructure.Persistence;

namespace SeneCare.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager, SeneCareDbContext db) : IIdentityService
{
    public async Task ForgetAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identityUser = await userManager.FindByIdAsync(user.Id.ToString());
        if (identityUser is not null)
        {
            identityUser.PhoneNumber = user.PhoneNumber.E164;
            identityUser.UserName = user.PhoneNumber.E164;
            identityUser.MustChangePassword = false;
            await userManager.UpdateAsync(identityUser);
            if (await userManager.HasPasswordAsync(identityUser))
            {
                await userManager.RemovePasswordAsync(identityUser);
            }

            // New security stamp: cookies issued before are rejected at their next validation.
            await userManager.UpdateSecurityStampAsync(identityUser);
        }

        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    public async Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await userManager.FindByIdAsync(userId.ToString()) is { } user && await userManager.HasPasswordAsync(user);

    public async Task<bool> MustChangePasswordAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await userManager.FindByIdAsync(userId.ToString()) is { MustChangePassword: true };

    public async Task<PasswordCheck> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !await userManager.HasPasswordAsync(user))
        {
            return PasswordCheck.NoPassword;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return PasswordCheck.LockedOut;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user) ? PasswordCheck.LockedOut : PasswordCheck.Failed;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return user.MustChangePassword ? PasswordCheck.SuccessMustChange : PasswordCheck.Success;
    }

    public async Task<string?> SetPasswordAsync(Guid userId, string password, bool mustChange, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return "user.not_found";
        }

        if (await userManager.HasPasswordAsync(user))
        {
            await userManager.RemovePasswordAsync(user);
        }

        var added = await userManager.AddPasswordAsync(user, password);
        if (!added.Succeeded)
        {
            return "password.too_short";
        }

        user.MustChangePassword = mustChange;
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        // New security stamp: other web sessions signed in with the old password are closed…
        await userManager.UpdateSecurityStampAsync(user);
        // …and so are the mobile app sessions (refresh tokens), like for an erasure.
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
        return null;
    }

    public async Task EnsureIdentityUserAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var existing = await userManager.FindByIdAsync(user.Id.ToString());
        if (existing is not null)
        {
            if (existing.PhoneNumber != user.PhoneNumber.E164)
            {
                existing.PhoneNumber = user.PhoneNumber.E164;
                existing.UserName = user.PhoneNumber.E164;
                await userManager.UpdateAsync(existing);
            }

            return;
        }

        var identityUser = new ApplicationUser
        {
            Id = user.Id,
            UserName = user.PhoneNumber.E164,
            PhoneNumber = user.PhoneNumber.E164,
            PhoneNumberConfirmed = true,
        };
        var result = await userManager.CreateAsync(identityUser);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Identity user creation failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}
