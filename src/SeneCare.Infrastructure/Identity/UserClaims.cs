using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SeneCare.Application.Auth;
using SeneCare.Infrastructure.Persistence;

namespace SeneCare.Infrastructure.Identity;

/// <summary>Builds the business claims of a user: name, platform roles, account memberships, registration state.</summary>
internal sealed class UserClaimsBuilder(SeneCareDbContext db)
{
    public async Task<IReadOnlyList<Claim>> BuildAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var memberships = await db.CustomerAccounts.AsNoTracking()
            .Where(a => a.Members.Any(m => m.UserId == userId))
            .Select(a => new { a.Id, a.Kind, a.Members })
            .ToListAsync(cancellationToken);

        var claims = new List<Claim>
        {
            new(SeneCareClaimTypes.Phone, user.PhoneNumber.E164),
            new(SeneCareClaimTypes.Registered, RegistrationStatus.IsComplete(user, memberships.Count > 0) ? "true" : "false"),
        };
        if (user.FullName is not null)
        {
            claims.Add(new Claim(ClaimTypes.GivenName, user.FullName));
        }

        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role.ToString())));
        claims.AddRange(memberships.Select(a => new Claim(
            SeneCareClaimTypes.Membership,
            new MembershipClaim(a.Id, a.Kind, a.Members.First(m => m.UserId == userId).Role).ToString())));
        return claims;
    }
}

/// <summary>Cookie principal = Identity claims (id, security stamp) + SENECARE business claims.</summary>
internal sealed class SeneCareUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> options,
    UserClaimsBuilder claimsBuilder)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaims(await claimsBuilder.BuildAsync(user.Id));
        return identity;
    }
}
