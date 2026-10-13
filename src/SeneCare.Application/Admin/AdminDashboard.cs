using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Admin;

public sealed record Count<T>(T Key, int Value);

public sealed record MonthlyRevenue(int Year, int Month, Money Amount);

public sealed record NeighborhoodCount(string Neighborhood, string Commune, int Members, int Threshold);

public sealed record AdminDashboard(
    int Open,
    IReadOnlyList<Count<RequestStatus>> ByStatus,
    IReadOnlyList<Count<Urgency>> ByUrgency,
    IReadOnlyList<Count<ServiceDomain>> ByDomain,
    IReadOnlyList<Count<string>> ByCommune,
    int SlaMeasured,
    int SlaRespected,
    int LateNow,
    IReadOnlyList<MonthlyRevenue> Revenue,
    Money Unpaid,
    int UnpaidInvoices,
    IReadOnlyList<NeighborhoodCount> Neighborhoods,
    double? AverageRating,
    int Ratings,
    int Disputes,
    int PendingPayments)
{
    public int? SlaRate => SlaMeasured == 0 ? null : (int)Math.Round(100.0 * SlaRespected / SlaMeasured);
}

/// <summary>
/// Back-office overview: the requests to watch now (open, late), the money (monthly revenue, unpaid),
/// the neighborhoods and customer satisfaction. Windows: SLA and satisfaction on 30 days, revenue on 6 months.
/// </summary>
public sealed class AdminDashboardQuery(IApplicationDbContext db, TimeProvider clock)
{
    private static readonly RequestStatus[] Closed = [RequestStatus.ValidatedByCustomer, RequestStatus.Cancelled];

    public async Task<AdminDashboard> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var open = await (
                from r in db.ServiceRequests
                join p in db.Properties on r.PropertyId equals p.Id
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where !Closed.Contains(r.Status)
                select new { r.Status, r.Urgency, r.Domain, Commune = c.Name, r.SlaDeadline, r.RespondedAt })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var since = now.AddDays(-30);
        var measured = await db.ServiceRequests.AsNoTracking()
            .Where(r => r.SlaDeadline != null && r.CreatedAt >= since && r.Status != RequestStatus.Cancelled
                && (r.RespondedAt != null || r.SlaDeadline < now))
            .Select(r => new { r.SlaDeadline, r.RespondedAt })
            .ToListAsync(cancellationToken);

        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-5);
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Succeeded && p.CompletedAt >= monthStart)
            .Select(p => new { p.CompletedAt, p.Amount })
            .ToListAsync(cancellationToken);
        var revenue = Enumerable.Range(0, 6)
            .Select(i => monthStart.AddMonths(i))
            .Select(m => new MonthlyRevenue(
                m.Year,
                m.Month,
                payments.Where(p => p.CompletedAt!.Value.Year == m.Year && p.CompletedAt.Value.Month == m.Month)
                    .Aggregate(Money.Zero, (sum, p) => sum + p.Amount)))
            .ToList();

        var unpaid = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue)
            .ToListAsync(cancellationToken);

        var neighborhoods = await (
                from p in db.NeighborhoodPrograms
                join n in db.Neighborhoods on p.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where p.MemberCount > 0
                orderby p.MemberCount descending
                select new NeighborhoodCount(n.Name, c.Name, p.MemberCount, p.Threshold))
            .AsNoTracking()
            .Take(8)
            .ToListAsync(cancellationToken);

        var ratings = await db.ServiceRequests.AsNoTracking()
            .Where(r => r.Rating != null && r.Rating.GivenAt >= since)
            .Select(r => r.Rating!.Score)
            .ToListAsync(cancellationToken);

        return new AdminDashboard(
            open.Count,
            [.. open.GroupBy(r => r.Status).Select(g => new Count<RequestStatus>(g.Key, g.Count())).OrderBy(c => c.Key)],
            [.. open.GroupBy(r => r.Urgency).Select(g => new Count<Urgency>(g.Key, g.Count())).OrderBy(c => c.Key)],
            [.. open.GroupBy(r => r.Domain).Select(g => new Count<ServiceDomain>(g.Key, g.Count())).OrderByDescending(c => c.Value)],
            [.. open.GroupBy(r => r.Commune).Select(g => new Count<string>(g.Key, g.Count())).OrderByDescending(c => c.Value).Take(8)],
            measured.Count,
            measured.Count(r => r.RespondedAt is { } at && at <= r.SlaDeadline),
            open.Count(r => r.RespondedAt is null && r.SlaDeadline < now),
            revenue,
            unpaid.Aggregate(Money.Zero, (sum, i) => sum + (i.Total - i.AmountPaid)),
            unpaid.Count,
            neighborhoods,
            ratings.Count == 0 ? null : ratings.Average(),
            ratings.Count,
            await db.ServiceRequests.CountAsync(r => r.Status == RequestStatus.Disputed, cancellationToken),
            await db.Payments.CountAsync(p => p.Status == PaymentStatus.Pending && p.DeclaredByUserId != null, cancellationToken));
    }
}
