using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Public;

public sealed record SocialLink(string Name, string Url);

/// <summary>SENECARE's public contact details, from Admin › Paramètres. A field left empty is not shown.</summary>
public sealed record PublicContact(
    string? PhoneDisplay,
    string? PhoneLink,
    string? WhatsAppUrl,
    string? Email,
    string? Address,
    string? Hours,
    string? Areas,
    IReadOnlyList<SocialLink> Social)
{
    public bool HasPhone => PhoneLink is not null;

    public bool HasWhatsApp => WhatsAppUrl is not null;

    /// <summary>« 762919145 », « +221 76 291 91 45 », « 00221… » → the 9 national digits, or null.</summary>
    public static string? NationalDigits(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00221", StringComparison.Ordinal))
        {
            digits = digits[5..];
        }
        else if (digits.Length == 12 && digits.StartsWith("221", StringComparison.Ordinal))
        {
            digits = digits[3..];
        }

        return digits.Length == 9 && digits[0] is '3' or '7' ? digits : null;
    }

    public static string Display(string digits) => $"+221 {digits[..2]} {digits[2..5]} {digits[5..7]} {digits[7..]}";
}

public sealed class GetContactHandler(IApplicationDbContext db)
{
    public async Task<PublicContact> HandleAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.AppSettings.AsNoTracking()
            .Where(s => SettingKeys.Contact.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        string? Value(string key)
        {
            var value = (stored.GetValueOrDefault(key) ?? SettingKeys.Defaults[key]).Trim();
            return value.Length == 0 ? null : value;
        }

        var phone = PublicContact.NationalDigits(Value(SettingKeys.ContactPhone));
        var whatsApp = PublicContact.NationalDigits(Value(SettingKeys.ContactWhatsApp));
        var social = new[] { ("Facebook", SettingKeys.ContactFacebook), ("Instagram", SettingKeys.ContactInstagram), ("LinkedIn", SettingKeys.ContactLinkedIn) }
            .Select(s => (s.Item1, Url: Value(s.Item2)))
            .Where(s => s.Url is not null)
            .Select(s => new SocialLink(s.Item1, s.Url!))
            .ToList();
        return new PublicContact(
            phone is null ? null : PublicContact.Display(phone),
            phone is null ? null : "+221" + phone,
            whatsApp is null ? null : "https://wa.me/221" + whatsApp,
            Value(SettingKeys.ContactEmail),
            Value(SettingKeys.ContactAddress),
            Value(SettingKeys.ContactHours),
            Value(SettingKeys.ContactAreas),
            social);
    }
}
