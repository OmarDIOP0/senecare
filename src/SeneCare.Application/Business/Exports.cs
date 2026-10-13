using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Business;

// ---- PDF documents. The Web fills them with already-translated text: the renderer only lays out. ----

public sealed record ReportFact(string Label, string Value);

public sealed record ReportLine(string Domain, string Label, string Result, bool IsAnomaly, string? Comment);

public sealed record ReportSection(string Title, IReadOnlyList<ReportLine> Lines);

public sealed record DiagnosticReport(
    string Title,
    string AccountName,
    IReadOnlyList<ReportFact> Facts,
    string RecommendationsTitle,
    string? Recommendations,
    IReadOnlyList<ReportSection> Sections,
    string Footer);

public sealed record RequestsReport(
    string Title,
    string AccountName,
    string Subtitle,
    IReadOnlyList<ReportFact> Summary,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string Footer);

// ---- Export data (CSV / PDF), limited to what the member may see. ----

public sealed record RequestExportRow(
    string Number,
    DateTimeOffset CreatedAt,
    string Site,
    ServiceDomain Domain,
    Urgency Urgency,
    RequestStatus Status,
    string? ReportedBy,
    DateTimeOffset? SlaDeadline,
    DateTimeOffset? RespondedAt,
    DateTimeOffset? CompletedAt,
    string Description)
{
    /// <summary>True/false once measurable (reached, or deadline passed); null without SLA or still in time.</summary>
    public bool? SlaRespected(DateTimeOffset now) => SlaDeadline is not { } deadline
        ? null
        : RespondedAt is { } at ? at <= deadline : now > deadline ? false : null;
}

public sealed record InvoiceExportRow(string Number, DateOnly DueDate, DateRange? Period, Money Total, Money Paid, InvoiceStatus Status, DateTimeOffset? PaidAt);

public sealed class BusinessExports(IApplicationDbContext db, BusinessAccess access)
{
    public const int MaxRows = 5_000;

    public async Task<(string AccountName, IReadOnlyList<RequestExportRow> Rows)?> RequestsAsync(
        Guid userId, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var siteIds = await access.SiteIdsAsync(member, cancellationToken);
        var rows = await (
                from r in db.ServiceRequests
                join p in db.Properties on r.PropertyId equals p.Id
                join u in db.Users on r.ReportedByUserId equals u.Id into reporters
                from u in reporters.DefaultIfEmpty()
                where siteIds.Contains(r.PropertyId) && r.CreatedAt >= since && r.CreatedAt < until
                orderby r.CreatedAt
                select new RequestExportRow(
                    r.Number, r.CreatedAt, p.Name, r.Domain, r.Urgency, r.Status, u.FullName,
                    r.SlaDeadline, r.RespondedAt, r.CompletedAt, r.Description))
            .AsNoTracking()
            .Take(MaxRows)
            .ToListAsync(cancellationToken);
        return (member.Account.DisplayName, rows);
    }

    /// <summary>Invoices: manager only (money).</summary>
    public async Task<(string AccountName, IReadOnlyList<InvoiceExportRow> Rows)?> InvoicesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        if (member is not { IsManager: true })
        {
            return null;
        }

        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.AccountId == member.Account.Id && i.Status != InvoiceStatus.Draft)
            .OrderBy(i => i.DueDate)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);
        return (member.Account.DisplayName, [.. invoices.Select(i => new InvoiceExportRow(i.Number, i.DueDate, i.Period, i.Total, i.AmountPaid, i.Status, i.PaidAt))]);
    }
}
