using Microsoft.AspNetCore.Identity;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
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
