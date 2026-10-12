using System.Globalization;

namespace SeneCare.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, string code, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException(code, "This value is required.");
        }

        return CheckLength(trimmed, code, maxLength);
    }

    public static string? Optional(string? value, string code, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : CheckLength(trimmed, code, maxLength);
    }

    private static string CheckLength(string value, string code, int maxLength) =>
        value.Length <= maxLength
            ? value
            : throw new DomainException(
                code + ".too_long",
                string.Create(CultureInfo.InvariantCulture, $"Maximum {maxLength} characters."));
}
