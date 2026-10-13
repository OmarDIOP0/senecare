using Microsoft.AspNetCore.Identity;
using SeneCare.Application.Abstractions;
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
            await userManager.UpdateAsync(identityUser);
            // New security stamp: cookies issued before are rejected at their next validation.
            await userManager.UpdateSecurityStampAsync(identityUser);
        }

        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
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
