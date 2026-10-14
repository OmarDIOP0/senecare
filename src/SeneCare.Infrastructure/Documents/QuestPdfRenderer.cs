using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Business;

namespace SeneCare.Infrastructure.Documents;

/// <summary>
/// PDF layout with the SENECARE palette (latérite, atlantique, sable). Light documents: vector text only,
/// no images, so a quarterly report stays a few dozen KB on a phone.
/// </summary>
public sealed class QuestPdfRenderer : IPdfRenderer
{
    private const string Laterite = "#B4472B";
    private const string Atlantique = "#1C3D5A";
    private const string Baobab = "#2F5D3A";
    private const string Sable = "#F6EFE4";
    private const string Ligne = "#DDD2C2";
    private const string Charbon = "#1F1B16";
    private const string CharbonDoux = "#5C5348";

    static QuestPdfRenderer()
    {
        // Free Community licence (company revenue under 1 M$). Review before going beyond.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] FieldReport(FieldReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Document.Create(document => document.Page(page =>
        {
            Setup(page, report.Footer);
            page.Header().Element(h => Header(h, report.Title, report.AccountName));
            page.Content().PaddingVertical(14).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(c => Facts(c, report.Facts));

                if (!string.IsNullOrWhiteSpace(report.Recommendations))
                {
                    column.Item().Background(Sable).BorderLeft(3).BorderColor(Laterite).Padding(10).Column(r =>
                    {
                        r.Item().Text(report.RecommendationsTitle).Bold().FontColor(Laterite);
                        r.Item().PaddingTop(4).Text(report.Recommendations);
                    });
                }

                foreach (var section in report.Sections)
                {
                    column.Item().PaddingTop(6).Text(section.Title).FontSize(13).Bold().FontColor(Atlantique);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(4);
                            c.RelativeColumn(2);
                        });
                        foreach (var line in section.Lines)
                        {
                            table.Cell().Element(Cell).Text(line.Domain).FontColor(CharbonDoux);
                            table.Cell().Element(Cell).Column(c =>
                            {
                                c.Item().Text(line.Label);
                                if (!string.IsNullOrWhiteSpace(line.Comment))
                                {
                                    c.Item().Text(line.Comment).FontSize(9).FontColor(CharbonDoux);
                                }
                            });
                            table.Cell().Element(Cell).AlignRight().Text(line.Result).Bold().FontColor(line.IsAnomaly ? Laterite : Baobab);
                        }
                    });
                }
            });
        })).GeneratePdf();
    }

    public byte[] RequestsReport(RequestsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Document.Create(document => document.Page(page =>
        {
            Setup(page, report.Footer);
            page.Size(PageSizes.A4.Landscape());
            page.Header().Element(h => Header(h, report.Title, $"{report.AccountName} · {report.Subtitle}"));
            page.Content().PaddingVertical(14).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(c => Facts(c, report.Summary));
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        foreach (var _ in report.Columns)
                        {
                            c.RelativeColumn();
                        }
                    });
                    table.Header(header =>
                    {
                        foreach (var name in report.Columns)
                        {
                            header.Cell().Background(Atlantique).Padding(4).Text(name).FontColor(Sable).Bold().FontSize(9);
                        }
                    });
                    foreach (var row in report.Rows)
                    {
                        foreach (var value in row)
                        {
                            table.Cell().Element(Cell).Text(value).FontSize(9);
                        }
                    }
                });
            });
        })).GeneratePdf();
    }

    public byte[] Invoice(SeneCare.Application.Payments.InvoicePdf invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return Document.Create(document => document.Page(page =>
        {
            Setup(page, invoice.Footer);
            page.Header().Element(h => Header(h, invoice.Title, invoice.AccountName));
            page.Content().PaddingVertical(14).Column(column =>
            {
                column.Spacing(14);
                column.Item().Element(c => Facts(c, invoice.Facts));
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(header =>
                    {
                        foreach (var (index, name) in invoice.Columns.Index())
                        {
                            var cell = header.Cell().Background(Atlantique).Padding(5);
                            (index == 0 ? cell : cell.AlignRight()).Text(name).FontColor(Sable).Bold().FontSize(9);
                        }
                    });
                    foreach (var line in invoice.Lines)
                    {
                        table.Cell().Element(Cell).Text(line.Description);
                        table.Cell().Element(Cell).AlignRight().Text(line.Quantity);
                        table.Cell().Element(Cell).AlignRight().Text(line.UnitPrice);
                        table.Cell().Element(Cell).AlignRight().Text(line.Total);
                    }
                });
                column.Item().AlignRight().Background(Sable).Padding(10).Row(row =>
                {
                    row.AutoItem().Text(invoice.TotalLabel + "  ").Bold();
                    row.AutoItem().Text(invoice.Total).Bold().FontSize(13).FontColor(Laterite);
                });
                if (invoice.PaidStamp is { } stamp)
                {
                    column.Item().AlignRight().Border(2).BorderColor(Baobab).PaddingVertical(4).PaddingHorizontal(10)
                        .Text(stamp).Bold().FontColor(Baobab);
                }
            });
        })).GeneratePdf();
    }

    private static void Setup(PageDescriptor page, string footer)
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(s => s.FontSize(10).FontColor(Charbon));
        page.Footer().AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(s => s.FontSize(8).FontColor(CharbonDoux));
            text.Span(footer + " · ");
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }

    private static void Header(IContainer container, string title, string subtitle) =>
        container.BorderBottom(2).BorderColor(Laterite).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(title).FontSize(18).Bold().FontColor(Atlantique);
                c.Item().Text(subtitle).FontColor(CharbonDoux);
            });
            row.ConstantItem(110).AlignRight().AlignMiddle().Text(t =>
            {
                t.Span("Sene").Bold().FontSize(14).FontColor(Atlantique);
                t.Span("Care").Bold().FontSize(14).FontColor(Laterite);
            });
        });

    private static void Facts(IContainer container, IReadOnlyList<ReportFact> facts) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(150);
                c.RelativeColumn();
            });
            foreach (var fact in facts)
            {
                table.Cell().PaddingVertical(2).Text(fact.Label).FontColor(CharbonDoux);
                table.Cell().PaddingVertical(2).Text(fact.Value).Bold();
            }
        });

    private static IContainer Cell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Ligne).PaddingVertical(4).PaddingHorizontal(3);
}
