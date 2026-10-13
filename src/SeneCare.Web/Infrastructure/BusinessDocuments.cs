using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using SeneCare.Application.Business;
using SeneCare.Domain.Diagnostics;
using SeneCare.Web.Localization;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// Turns business data into translated documents: CSV for Excel (« ; » separator, UTF-8 BOM, French
/// dates) and the content of the PDF reports. The PDF renderer itself only lays out the given text.
/// </summary>
public static class BusinessDocuments
{
    private const string CsvDate = "yyyy-MM-dd HH:mm";

    public static FieldReport Diagnostic(DiagnosticDetail detail, IStringLocalizer<SharedResource> l)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(l);
        var facts = new List<ReportFact>
        {
            new(l["Diagnostic_Period"], $"{Day(detail.Slot.Period.Start)} – {Day(detail.Slot.Period.End)}"),
            new(l["Diagnostic_Date"], Formatting.ShortDateTime(detail.Slot.ScheduledFor)),
            new(l["Diagnostic_Status"], l["DiagnosticStatus_" + detail.Slot.Status]),
            new(l["Diagnostic_Anomalies"], detail.Slot.Anomalies.ToString(CultureInfo.CurrentCulture)),
        };
        if (detail.TechnicianName is { } technician)
        {
            facts.Add(new(l["Diagnostic_Technician"], technician));
        }

        if (detail.ValidatedAt is { } validated)
        {
            facts.Add(new(l["Diagnostic_ValidatedOn"], Formatting.ShortDate(validated)));
        }

        return new FieldReport(
            l["Diagnostic_ReportTitle", Quarter(detail.Slot.Period.Start), detail.Slot.Period.Start.Year],
            detail.AccountName,
            facts,
            l["Diagnostic_Recommendations"],
            detail.Recommendations,
            [.. detail.Sites.Select(site => new ReportSection(
                site.Name ?? l["Diagnostic_General"].Value,
                [.. site.Points.Select(p => new ReportLine(
                    l["Domain_" + p.Domain], p.Label, l["ControlPoint_" + p.Result], p.Result == ControlPointResult.Anomaly, p.Comment))]))],
            l["Pdf_Footer", Formatting.ShortDate(DateTimeOffset.UtcNow)]);
    }

    public static RequestsReport Requests(
        string accountName, IReadOnlyList<RequestExportRow> rows, DateTimeOffset since, DateTimeOffset until, IStringLocalizer<SharedResource> l, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(l);
        var measured = rows.Select(r => r.SlaRespected(now)).OfType<bool>().ToList();
        var summary = new List<ReportFact>
        {
            new(l["Export_Total"], rows.Count.ToString(CultureInfo.CurrentCulture)),
            new(l["Export_Critical"], rows.Count(r => r.Urgency == Domain.Common.Urgency.Critical).ToString(CultureInfo.CurrentCulture)),
            new(l["Export_SlaRate"], measured.Count == 0 ? "—" : $"{Math.Round(100.0 * measured.Count(m => m) / measured.Count)} %"),
        };

        return new RequestsReport(
            l["Export_RequestsTitle"],
            accountName,
            $"{Formatting.ShortDate(since)} – {Formatting.ShortDate(until.AddDays(-1))}",
            summary,
            [l["Export_Col_Number"], l["Export_Col_Date"], l["Export_Col_Site"], l["Export_Col_Domain"], l["Export_Col_Urgency"], l["Export_Col_Status"], l["Export_Col_Sla"]],
            [.. rows.Select(r => (IReadOnlyList<string>)[
                r.Number,
                Formatting.ShortDate(r.CreatedAt),
                r.Site,
                l["Domain_" + r.Domain],
                l["Urgency_" + r.Urgency],
                l["Status_" + r.Status],
                Sla(r.SlaRespected(now), l)])],
            l["Pdf_Footer", Formatting.ShortDate(now)]);
    }

    public static byte[] RequestsCsv(IReadOnlyList<RequestExportRow> rows, IStringLocalizer<SharedResource> l, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(l);
        var csv = new Csv();
        csv.Row(l["Export_Col_Number"], l["Export_Col_Date"], l["Export_Col_Site"], l["Export_Col_Domain"], l["Export_Col_Urgency"],
            l["Export_Col_Status"], l["Export_Col_ReportedBy"], l["Export_Col_SlaDeadline"], l["Export_Col_Responded"], l["Export_Col_Completed"],
            l["Export_Col_Sla"], l["Export_Col_Description"]);
        foreach (var r in rows)
        {
            csv.Row(r.Number, Date(r.CreatedAt), r.Site, l["Domain_" + r.Domain], l["Urgency_" + r.Urgency], l["Status_" + r.Status],
                r.ReportedBy ?? string.Empty, Date(r.SlaDeadline), Date(r.RespondedAt), Date(r.CompletedAt), Sla(r.SlaRespected(now), l), r.Description);
        }

        return csv.ToBytes();
    }

    public static byte[] InvoicesCsv(IReadOnlyList<InvoiceExportRow> rows, IStringLocalizer<SharedResource> l)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(l);
        var csv = new Csv();
        csv.Row(l["Export_Col_Invoice"], l["Export_Col_DueDate"], l["Export_Col_Period"], l["Export_Col_Amount"], l["Export_Col_Paid"], l["Export_Col_Status"], l["Export_Col_PaidOn"]);
        foreach (var i in rows)
        {
            csv.Row(
                i.Number,
                i.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                i.Period is { } p ? $"{p.Start:yyyy-MM-dd} → {p.End:yyyy-MM-dd}" : l["Export_Registration"],
                i.Total.Amount.ToString(CultureInfo.InvariantCulture),
                i.Paid.Amount.ToString(CultureInfo.InvariantCulture),
                l["InvoiceStatus_" + i.Status],
                Date(i.PaidAt));
        }

        return csv.ToBytes();
    }

    private static string Sla(bool? respected, IStringLocalizer<SharedResource> l) => respected switch
    {
        true => l["Export_Sla_Yes"],
        false => l["Export_Sla_No"],
        null => "—",
    };

    private static string Date(DateTimeOffset? value) =>
        value is { } v ? Formatting.ToDakar(v).ToString(CsvDate, CultureInfo.InvariantCulture) : string.Empty;

    private static string Day(DateOnly day) => Formatting.ShortDate(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    /// <summary>Quarter number (« T2 2026 »): what business customers talk about.</summary>
    public static int Quarter(DateOnly start) => ((start.Month - 1) / 3) + 1;

    /// <summary>Minimal RFC 4180 writer with protection against formula injection in spreadsheets.</summary>
    private sealed class Csv
    {
        private readonly StringBuilder _text = new();

        public void Row(params string[] values) =>
            _text.Append(string.Join(';', values.Select(Escape))).Append("\r\n");

        public byte[] ToBytes() => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(_text.ToString())];

        private static string Escape(string value)
        {
            // A cell starting with = + - @ would be run as a formula by Excel: neutralise it.
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                value = "'" + value;
            }

            return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
        }
    }
}
