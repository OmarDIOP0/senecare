using System.Globalization;
using System.Resources;
using SeneCare.Domain.Common;

namespace SeneCare.Application.Resources;

/// <summary>Localized texts produced by the back end: SMS / WhatsApp messages, field checklists, reports.</summary>
public static class Messages
{
    private static readonly ResourceManager ResourceManager = new("SeneCare.Application.Resources.Messages", typeof(Messages).Assembly);

    public static string Otp(string code, Language language) => Format("Otp", language, code);

    public static string Invitation(string companyName, string link, Language language) => Format("Invitation", language, companyName, link);

    /// <summary>Default field checklist for a trade (editable on site).</summary>
    public static IReadOnlyList<string> Checklist(ServiceDomain domain, Language language) =>
        Format("Checklist_" + domain, language).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string PaymentVerified(string invoice, Language language) => Format("Payment_Verified", language, invoice);

    public static string PaymentRejected(string invoice, string reason, Language language) => Format("Payment_Rejected", language, invoice, reason);

    public static string RequestSlotsProposed(string number, Language language) => Format("Request_SlotsProposed", language, number);

    public static string RequestOnTheWay(string number, string technician, Language language) => Format("Request_OnTheWay", language, number, technician);

    public static string RequestCompleted(string number, Language language) => Format("Request_Completed", language, number);

    public static string MissionAssigned(string number, DateTimeOffset start, Language language) => Format("Mission_Assigned", language, number, When(start, language));

    public static string ReportDone(string steps, Language language) => Format("Report_Done", language, steps);

    public static string ReportParts(string parts, Language language) => Format("Report_Parts", language, parts);

    public static string ReportDuration(int minutes, Language language) => Format("Report_Duration", language, minutes);

    /// <summary>« lun. 12 oct. à 9h30 », Dakar time (UTC+0, no daylight saving).</summary>
    private static string When(DateTimeOffset at, Language language) =>
        at.ToUniversalTime().ToString(language == Language.English ? "ddd d MMM 'at' H':'mm" : "'le' ddd d MMM 'à' H'h'mm", CultureFor(language));

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
