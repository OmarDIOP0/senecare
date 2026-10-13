using Microsoft.Extensions.Localization;
using SeneCare.Application.Business;
using SeneCare.Application.Requests;
using SeneCare.Web.Localization;

namespace SeneCare.Web.Infrastructure;

/// <summary>Content of the intervention report PDF (translated here; the renderer only lays it out).</summary>
public static class InterventionDocuments
{
    public static FieldReport Report(RequestDetail detail, IStringLocalizer<SharedResource> l)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(l);
        var s = detail.Summary;
        var facts = new List<ReportFact>
        {
            new(l["Export_Col_Number"], s.Number),
            new(l["Export_Col_Site"], s.PropertyName),
            new(l["Export_Col_Domain"], l["Domain_" + s.Domain]),
            new(l["Export_Col_Date"], Formatting.ShortDateTime(s.CreatedAt)),
        };
        if (detail.CompletedAt is { } completed)
        {
            facts.Add(new(l["Export_Col_Completed"], Formatting.ShortDateTime(completed)));
        }

        if (s.Technician is { } technician)
        {
            facts.Add(new(l["Diagnostic_Technician"], technician.Name));
        }

        if (detail.SignedByName is { } signer)
        {
            facts.Add(new(l["Tech_SignedByName"], signer));
        }

        if (detail.WarrantyEndsOn is { } warranty)
        {
            facts.Add(new(l["Report_Warranty"], Formatting.ShortDate(new DateTimeOffset(warranty.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))));
        }

        return new FieldReport(
            l["Report_Title"],
            s.Title,
            facts,
            l["Request_Report"],
            detail.Report,
            [new ReportSection(l["Tech_Request"], [new ReportLine(l["Domain_" + s.Domain], detail.Description, l["Status_" + s.Status], false, null)])],
            l["Pdf_Footer", Formatting.ShortDate(DateTimeOffset.UtcNow)]);
    }
}
