using System.Globalization;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Web.Infrastructure;

/// <summary>Display formats for Senegal: « 8 500 FCFA », « lun. 12 oct. », Africa/Dakar time.</summary>
public static class Formatting
{
    private static readonly TimeZoneInfo Dakar = FindDakar();

    /// <summary>« 8 500 FCFA » — French digit grouping (narrow no-break space) and a no-break space before FCFA.</summary>
    public static string Fcfa(long amount) => amount.ToString("N0", CultureInfo.CurrentCulture) + " FCFA";

    public static string Fcfa(Money amount) => Fcfa(amount.Amount);

    /// <summary>« lun. 12 oct. »</summary>
    public static string ShortDate(DateTimeOffset value) => ToDakar(value).ToString("ddd d MMM", CultureInfo.CurrentCulture);

    /// <summary>« 12 oct. 2026 »: when the year matters (yearly periods, receipts).</summary>
    public static string DateWithYear(DateOnly value) => value.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>« lun. 12 oct. · 09:30 »</summary>
    public static string ShortDateTime(DateTimeOffset value) =>
        ShortDate(value) + " · " + ToDakar(value).ToString("HH:mm", CultureInfo.CurrentCulture);

    public static string Time(DateTimeOffset value) => ToDakar(value).ToString("HH:mm", CultureInfo.CurrentCulture);

    public static DateTimeOffset ToDakar(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Dakar);

    /// <summary>Lucide icon for a trade.</summary>
    public static string Icon(ServiceDomain domain) => domain switch
    {
        ServiceDomain.Electricity => "plug",
        ServiceDomain.Plumbing => "pipe-wrench",
        ServiceDomain.Waterproofing => "umbrella",
        ServiceDomain.Masonry => "brick-wall",
        ServiceDomain.Carpentry => "door-open",
        ServiceDomain.Security => "cctv",
        ServiceDomain.Outdoor => "plant",
        _ => "wrench",
    };

    /// <summary>Discreet tinted badges: blue = with SENECARE, amber = waiting / on the way, green = done, red = problem.</summary>
    public static string BadgeClass(RequestStatus status) => status switch
    {
        RequestStatus.AwaitingApproval or RequestStatus.AppointmentProposed => "bg-soleil-clair text-soleil",
        RequestStatus.Submitted or RequestStatus.UnderReview => "bg-atlantique-clair text-atlantique",
        RequestStatus.AppointmentConfirmed => "bg-indigo-clair text-indigo",
        RequestStatus.OnTheWay or RequestStatus.OnSite or RequestStatus.InProgress => "bg-soleil-clair text-soleil",
        RequestStatus.Completed or RequestStatus.ValidatedByCustomer => "bg-baobab-clair text-baobab",
        RequestStatus.Disputed => "bg-rouge-clair text-rouge",
        _ => "bg-sourdine text-charbon-doux",
    };

    public static string BadgeClass(Urgency urgency) => urgency switch
    {
        Urgency.Critical => "bg-rouge-clair text-rouge",
        Urgency.High => "bg-soleil-clair text-soleil",
        _ => "bg-sourdine text-charbon-doux",
    };

    public static string Icon(Urgency urgency) => urgency switch
    {
        Urgency.Critical => "siren",
        Urgency.High => "triangle-alert",
        Urgency.Normal => "clock",
        _ => "calendar",
    };

    private static TimeZoneInfo FindDakar()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Dakar");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc; // Dakar is UTC+0 all year, no daylight saving.
        }
    }
}
