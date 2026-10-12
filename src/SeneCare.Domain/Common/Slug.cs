using System.Globalization;
using System.Text;

namespace SeneCare.Domain.Common;

public static class Slug
{
    /// <summary>« Mermoz-Sacré-Cœur » → « mermoz-sacre-coeur », « Patte d'Oie » → « patte-d-oie ».</summary>
    public static string From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text
            .Replace("œ", "oe", StringComparison.Ordinal)
            .Replace("Œ", "Oe", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(normalized.Length);
        var pendingDash = false;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.ToString();
    }
}
