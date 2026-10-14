using Microsoft.Extensions.Localization;
using SeneCare.Application.Business;
using SeneCare.Application.Payments;
using SeneCare.Domain.Billing;
using SeneCare.Web.Localization;

namespace SeneCare.Web.Infrastructure;

/// <summary>Invoice / receipt PDF content, in the user's language.</summary>
public static class InvoiceDocuments
{
    public static InvoicePdf Build(InvoiceForDocument found, IStringLocalizer<SharedResource> l)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(l);
        var invoice = found.Invoice;
        var paid = invoice.Status == InvoiceStatus.Paid;
        var facts = new List<ReportFact>
        {
            new(l["Pdf_Invoice_Number"], invoice.Number),
            new(l["Pdf_Invoice_Issued"], invoice.IssuedAt is { } issued ? Formatting.ShortDate(issued) : "—"),
            new(l["Pdf_Invoice_Due"], Formatting.ShortDate(new DateTimeOffset(invoice.DueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))),
            new(l["Pdf_Invoice_Status"], l["InvoiceStatus_" + invoice.Status]),
        };
        if (invoice.Period is { } period)
        {
            facts.Add(new(l["Subscription_Period"], $"{Formatting.ShortDate(new DateTimeOffset(period.Start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))} – {Formatting.ShortDate(new DateTimeOffset(period.End.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))}"));
        }

        if (paid && invoice.PaidAt is { } paidAt)
        {
            facts.Add(new(l["Pdf_Invoice_PaidOn"], Formatting.ShortDate(paidAt)));
        }

        return new InvoicePdf(
            paid ? l["Pdf_Receipt_Title", invoice.Number] : l["Pdf_Invoice_Title", invoice.Number],
            found.AccountName,
            facts,
            [l["Pdf_Col_Description"], l["Pdf_Col_Quantity"], l["Pdf_Col_Unit"], l["Pdf_Col_Total"]],
            [.. invoice.Lines.Select(line => new InvoicePdfLine(line.Description, line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), Formatting.Fcfa(line.UnitPrice), Formatting.Fcfa(line.Total)))],
            l["Pdf_Col_TotalDue"],
            Formatting.Fcfa(invoice.Total),
            paid ? l["Pdf_PaidStamp"].Value : null,
            l["Pdf_Footer"]);
    }
}
