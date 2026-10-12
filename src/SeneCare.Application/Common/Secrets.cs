using System.Globalization;
using System.Security.Cryptography;

namespace SeneCare.Application.Common;

/// <summary>Cryptographically secure random values for codes and links.</summary>
public static class Secrets
{
    /// <summary>Numeric code with leading zeros, e.g. « 048213 ».</summary>
    public static string NumericCode(int length)
    {
        var max = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(0, max).ToString("D" + length.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>URL-safe token of <paramref name="bytes"/> random bytes.</summary>
    public static string UrlToken(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
