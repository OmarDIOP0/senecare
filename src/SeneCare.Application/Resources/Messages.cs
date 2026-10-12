using System.Globalization;
using System.Resources;
using SeneCare.Domain.Common;

namespace SeneCare.Application.Resources;

/// <summary>Localized texts of outgoing SMS / WhatsApp messages.</summary>
public static class Messages
{
    private static readonly ResourceManager ResourceManager = new("SeneCare.Application.Resources.Messages", typeof(Messages).Assembly);

    public static string Otp(string code, Language language) => Format("Otp", language, code);

    public static string Invitation(string companyName, string link, Language language) => Format("Invitation", language, companyName, link);

    private static string Format(string key, Language language, params object[] args)
    {
        var culture = CultureFor(language);
        var template = ResourceManager.GetString(key, culture)
            ?? throw new InvalidOperationException($"Missing message resource « {key} ».");
        return string.Format(culture, template, args);
    }

    private static CultureInfo CultureFor(Language language) => language switch
    {
        Language.Wolof => CultureInfo.GetCultureInfo("wo"),
        Language.English => CultureInfo.GetCultureInfo("en"),
        _ => CultureInfo.GetCultureInfo("fr"),
    };
}
