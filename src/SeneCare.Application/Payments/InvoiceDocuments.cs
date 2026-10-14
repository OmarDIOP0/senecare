using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Domain.Billing;

namespace SeneCare.Application.Payments;

/// <summary>One line of the PDF, already formatted (« 8 500 FCFA »).</summary>
public sealed record InvoicePdfLine(string Description, string Quantity, string UnitPrice, string Total);

/// <summary>Invoice — or receipt once paid — as a light PDF (vector text only).</summary>
public sealed record InvoicePdf(
    string Title,
    string AccountName,
    IReadOnlyList<ReportFact> Facts,
    IReadOnlyList<string> Columns,
    IReadOnlyList<InvoicePdfLine> Lines,
    string TotalLabel,
    string Total,
    string? PaidStamp,
    string Footer);

public sealed record InvoiceForDocument(Invoice Invoice, string AccountName);

/// <summary>An invoice of one of the user's accounts, for its PDF; nothing for anyone else.</summary>
public sealed class InvoiceDocumentQuery(IApplicationDbContext db, AccessControl access)
{
    public async Task<InvoiceForDocument?> HandleAsync(Guid userId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId && i.Status != InvoiceStatus.Draft, cancellationToken);
        if (invoice is null || !await access.CanAccessAccountAsync(userId, invoice.AccountId, cancellationToken))
        {
            return null;
        }

        var account = await db.CustomerAccounts.AsNoTracking().Where(a => a.Id == invoice.AccountId).Select(a => a.DisplayName).FirstAsync(cancellationToken);
        return new InvoiceForDocument(invoice, account);
    }
}
