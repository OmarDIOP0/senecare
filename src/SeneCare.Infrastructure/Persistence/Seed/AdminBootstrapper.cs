using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Persistence.Seed;

/// <summary>
/// First SENECARE administrators: phone numbers listed in « Bootstrap:SuperAdminPhones »
/// (comma-separated, user-secrets or environment variable — never in the repository) get the
/// SuperAdmin role at startup. They then sign in with their phone + OTP like everyone else.
/// </summary>
public sealed partial class AdminBootstrapper(
    SeneCareDbContext db, IConfiguration configuration, TimeProvider clock, ILogger<AdminBootstrapper> logger, SeneCare.Application.Abstractions.IIdentityService identity)
{
    public const string ConfigurationKey = "Bootstrap:SuperAdminPhones";

    /// <summary>Initial password of these admins (environment variable only), asked to be changed at first sign-in.</summary>
    public const string PasswordKey = "Bootstrap:SuperAdminPassword";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var phones = (configuration[ConfigurationKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => PhoneNumber.TryParse(p, out var phone) ? phone : null)
            .OfType<PhoneNumber>()
            .ToList();

        if (phones.Count == 0)
        {
            LogNoValidPhone(logger, configuration[ConfigurationKey] ?? string.Empty);
        }

        foreach (var phone in phones)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
            if (user is null)
            {
                user = User.Register(phone, clock.GetUtcNow());
                db.Users.Add(user);
            }

            if (!user.HasRole(PlatformRole.SuperAdmin))
            {
                user.GrantRole(PlatformRole.SuperAdmin, clock.GetUtcNow());
                LogGranted(logger, phone.Formatted);
            }
            else
            {
                LogAlreadyAdmin(logger, phone.Formatted);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Password sign-in: an admin without a password gets the configured one (never replaces a chosen one).
        var password = configuration[PasswordKey];
        foreach (var phone in phones)
        {
            var user = await db.Users.FirstAsync(u => u.PhoneNumber == phone, cancellationToken);
            await identity.EnsureIdentityUserAsync(user, cancellationToken);
            if (await identity.HasPasswordAsync(user.Id, cancellationToken))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(password) || SeneCare.Application.Auth.PasswordRules.Check(password, phone) is not null)
            {
                LogNoPassword(logger, phone.Formatted);
                continue;
            }

            await identity.SetPasswordAsync(user.Id, password, mustChange: true, cancellationToken);
            LogPasswordSet(logger, phone.Formatted);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Administrator {Phone} has no password: set Bootstrap__SuperAdminPassword (8 characters or more) to sign in.")]
    private static partial void LogNoPassword(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initial password set for administrator {Phone} (to change at first sign-in).")]
    private static partial void LogPasswordSet(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SuperAdmin role granted to {Phone} (Bootstrap:SuperAdminPhones)")]
    private static partial void LogGranted(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Administrator number (Bootstrap:SuperAdminPhones): {Phone}")]
    private static partial void LogAlreadyAdmin(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bootstrap:SuperAdminPhones has no valid Senegalese mobile number: « {Value} »")]
    private static partial void LogNoValidPhone(ILogger logger, string value);
}
