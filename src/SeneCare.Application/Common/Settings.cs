using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Common;

/// <summary>Reads admin-editable business parameters (with the defaults of <see cref="SettingKeys"/>).</summary>
public sealed class Settings(IApplicationDbContext db)
{
    public async Task<string> StringAsync(string key, CancellationToken cancellationToken = default) =>
        await db.AppSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken)
        ?? SettingKeys.Defaults[key];

    public async Task<int> IntAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await db.AppSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken)
            ?? SettingKeys.Defaults[key];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.Parse(SettingKeys.Defaults[key], CultureInfo.InvariantCulture);
    }
}

/// <summary>Africa/Dakar calendar (UTC+0 all year) for « today » in business rules.</summary>
public static class DakarCalendar
{
    public static DateOnly Today(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    }
}
