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
public sealed partial class AdminBootstrapper(SeneCareDbContext db, IConfiguration configuration, TimeProvider clock, ILogger<AdminBootstrapper> logger)
{
    public const string ConfigurationKey = "Bootstrap:SuperAdminPhones";

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
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SuperAdmin role granted to {Phone} (Bootstrap:SuperAdminPhones)")]
    private static partial void LogGranted(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Administrator number (Bootstrap:SuperAdminPhones): {Phone}")]
    private static partial void LogAlreadyAdmin(ILogger logger, string phone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bootstrap:SuperAdminPhones has no valid Senegalese mobile number: « {Value} »")]
    private static partial void LogNoValidPhone(ILogger logger, string value);
}
