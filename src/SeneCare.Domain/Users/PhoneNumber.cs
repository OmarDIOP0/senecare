using System.Diagnostics.CodeAnalysis;

namespace SeneCare.Domain.Users;

/// <summary>
/// Senegalese mobile number. Accepts « 77 123 45 67 », « +221771234567 », « 00221 77… » and stores
/// the E.164 form (+221XXXXXXXXX). Only mobile prefixes able to receive an SMS OTP are allowed.
/// </summary>
public sealed record PhoneNumber
{
    public const string CountryCode = "+221";

    private static readonly string[] MobilePrefixes = ["70", "75", "76", "77", "78"];

    /// <summary>
    /// Placeholder of an erased person: a fixed-line prefix, so it can never receive an OTP, never be typed
    /// at sign-in, and never collide with a real customer's mobile number.
    /// </summary>
    private const string AnonymizedPrefix = "330";

    private PhoneNumber(string nationalNumber) => NationalNumber = nationalNumber;

    /// <summary>The 9 national digits, e.g. « 771234567 ».</summary>
    public string NationalNumber { get; }

    /// <summary>E.164 form stored in database, e.g. « +221771234567 ».</summary>
    public string E164 => CountryCode + NationalNumber;

    /// <summary>Display form « 77 123 45 67 ».</summary>
    public string Formatted =>
        $"{NationalNumber[..2]} {NationalNumber[2..5]} {NationalNumber[5..7]} {NationalNumber[7..]}";

    public static PhoneNumber Parse(string? input) =>
        TryParse(input, out var phone)
            ? phone
            : throw new Common.DomainException("phone.invalid", "Invalid Senegalese mobile number.");

    public static bool TryParse(string? input, [NotNullWhen(true)] out PhoneNumber? phone)
    {
        phone = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        var hasPlus = input.TrimStart().StartsWith('+');

        if (digits.Length == 14 && digits.StartsWith("00221", StringComparison.Ordinal))
        {
            digits = digits[5..];
        }
        else if (digits.Length == 12 && digits.StartsWith("221", StringComparison.Ordinal))
        {
            digits = digits[3..];
        }
        else if (hasPlus)
        {
            return false;
        }

        if (digits.Length != 9 || !MobilePrefixes.Contains(digits[..2], StringComparer.Ordinal))
        {
            return false;
        }

        phone = new PhoneNumber(digits);
        return true;
    }

    /// <summary>True for the placeholder number of an erased person.</summary>
    public bool IsAnonymized => NationalNumber.StartsWith(AnonymizedPrefix, StringComparison.Ordinal);

    /// <summary>Placeholder number for an erased person (<paramref name="serial"/>: 0–999 999, unique).</summary>
    public static PhoneNumber Anonymized(int serial) =>
        serial is < 0 or > 999_999
            ? throw new ArgumentOutOfRangeException(nameof(serial))
            : new PhoneNumber(AnonymizedPrefix + serial.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Reads a stored E.164 value: a mobile number, or the placeholder of an erased person.</summary>
    public static PhoneNumber FromStorage(string e164)
    {
        ArgumentNullException.ThrowIfNull(e164);
        if (e164.StartsWith(CountryCode + AnonymizedPrefix, StringComparison.Ordinal) && e164.Length == CountryCode.Length + 9)
        {
            return new PhoneNumber(e164[CountryCode.Length..]);
        }

        return Parse(e164);
    }

    public override string ToString() => E164;
}
