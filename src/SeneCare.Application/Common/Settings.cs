using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Common;

/// <summary>
/// Reads admin-editable business parameters (with the defaults of <see cref="SettingKeys"/>). One instance per
/// request: the few dozen settings are read in one query the first time, not one query per key.
/// </summary>
public sealed class Settings(IApplicationDbContext db)
{
    private Dictionary<string, string>? _stored;

    public async Task<string> StringAsync(string key, CancellationToken cancellationToken = default) =>
        (await StoredAsync(cancellationToken)).GetValueOrDefault(key) ?? SettingKeys.Defaults[key];

    public async Task<int> IntAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await StringAsync(key, cancellationToken);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.Parse(SettingKeys.Defaults[key], CultureInfo.InvariantCulture);
    }

    private async Task<Dictionary<string, string>> StoredAsync(CancellationToken cancellationToken) =>
        _stored ??= await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);
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
