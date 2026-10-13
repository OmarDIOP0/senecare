using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Requests;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;

namespace SeneCare.Application.Business;

/// <summary>
/// Quarterly diagnostic planning: as soon as a business subscription starts (or a new quarter is paid),
/// the diagnostic closing that quarter is scheduled two weeks before its end. The dispatcher can move it.
/// </summary>
public sealed class DiagnosticPlanner(IApplicationDbContext db, TimeProvider clock)
{
    public const int DaysBeforePeriodEnd = 14;

    /// <summary>Adds the diagnostic of the current period if missing. The caller saves.</summary>
    public async Task<Diagnostic?> EnsurePlannedAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (!subscription.DiagnosticRequired || subscription.CurrentPeriod is not { } period
            || subscription.Status is SubscriptionStatus.PendingRegistration or SubscriptionStatus.Cancelled)
        {
            return null;
        }

        var exists = await db.Diagnostics.AnyAsync(
            d => d.SubscriptionId == subscription.Id && d.Status != DiagnosticStatus.Cancelled
                && d.PeriodCovered.Start == period.Start && d.PeriodCovered.End == period.End,
            cancellationToken);
        if (exists)
        {
            return null;
        }

        var today = DakarCalendar.Today(clock);
        var day = period.End.AddDays(-DaysBeforePeriodEnd);
        if (day < today)
        {
            day = today;
        }

        // 09:00 in Dakar (UTC+0 all year).
        var diagnostic = new Diagnostic(subscription.AccountId, subscription.Id, period, new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero));
        db.Diagnostics.Add(diagnostic);
        return diagnostic;
    }

    /// <summary>Validated diagnostic closing the current period: what unlocks the next invoice.</summary>
    public Task<Diagnostic?> ValidatedForCurrentPeriodAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (subscription.CurrentPeriod is not { } period)
        {
            return Task.FromResult<Diagnostic?>(null);
        }

        return db.Diagnostics.AsNoTracking().FirstOrDefaultAsync(
            d => d.SubscriptionId == subscription.Id && d.Status == DiagnosticStatus.Validated
                && d.PeriodCovered.Start == period.Start && d.PeriodCovered.End == period.End,
            cancellationToken);
    }
}

public sealed record ControlPointView(
    Guid Id,
    ServiceDomain Domain,
    string Label,
    ControlPointResult Result,
    string? Comment,
    IReadOnlyList<Guid> PhotoIds,
    Guid? ConvertedRequestId);

public sealed record DiagnosticSite(Guid? PropertyId, string? Name, IReadOnlyList<ControlPointView> Points);

public sealed record DiagnosticDetail(
    DiagnosticSlot Slot,
    string AccountName,
    string? TechnicianName,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ValidatedAt,
    string? Recommendations,
    IReadOnlyList<DiagnosticSite> Sites,
    bool CanConvert)
{
    public bool IsReportAvailable => Slot.Status is DiagnosticStatus.Submitted or DiagnosticStatus.Validated;
}

public sealed class DiagnosticQueries(IApplicationDbContext db, BusinessAccess access)
{
    public async Task<IReadOnlyList<DiagnosticSlot>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        if (member is null)
        {
            return [];
        }

        var list = await db.Diagnostics.AsNoTracking()
            .Where(d => d.AccountId == member.Account.Id && d.Status != DiagnosticStatus.Cancelled)
            .OrderByDescending(d => d.ScheduledFor)
            .ToListAsync(cancellationToken);
        return [.. list.Select(d => new DiagnosticSlot(d.Id, d.ScheduledFor, d.Status, d.PeriodCovered, d.AnomalyCount))];
    }

    /// <summary>Report of a diagnostic; site managers and employees only see the points of their sites.</summary>
    public async Task<DiagnosticDetail?> DetailAsync(Guid userId, Guid diagnosticId, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        var diagnostic = await db.Diagnostics.AsNoTracking().FirstOrDefaultAsync(d => d.Id == diagnosticId, cancellationToken);
        if (member is null || diagnostic is null || diagnostic.AccountId != member.Account.Id || diagnostic.Status == DiagnosticStatus.Cancelled)
        {
            return null;
        }

        var visible = diagnostic.ControlPoints
            .Where(c => c.PropertyId is not { } site || member.CanAccess(site))
            .ToList();
        var siteIds = visible.Where(c => c.PropertyId is not null).Select(c => c.PropertyId!.Value).Distinct().ToList();
        var names = await db.Properties.AsNoTracking().Where(p => siteIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        string? technician = null;
        if (diagnostic.TechnicianId is { } technicianId)
        {
            technician = await (
                    from t in db.Technicians
                    join u in db.Users on t.UserId equals u.Id
                    where t.Id == technicianId
                    select u.FullName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var sites = visible
            .GroupBy(c => c.PropertyId)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key is { } id ? names.GetValueOrDefault(id) : null, StringComparer.CurrentCulture)
            .Select(g => new DiagnosticSite(
                g.Key,
                g.Key is { } id ? names.GetValueOrDefault(id) : null,
                [.. g.OrderBy(c => c.Result == ControlPointResult.Anomaly ? 0 : 1).ThenBy(c => c.Domain)
                    .Select(c => new ControlPointView(c.Id, c.Domain, c.Label, c.Result, c.Comment, [.. c.Photos.Select(p => p.MediaFileId)], c.ConvertedRequestId))]))
            .ToList();

        return new DiagnosticDetail(
            new DiagnosticSlot(diagnostic.Id, diagnostic.ScheduledFor, diagnostic.Status, diagnostic.PeriodCovered, visible.Count(c => c.Result == ControlPointResult.Anomaly)),
            member.Account.DisplayName,
            technician,
            diagnostic.SubmittedAt,
            diagnostic.ValidatedAt,
            diagnostic.Recommendations,
            sites,
            member.CanApprove && diagnostic.Status is DiagnosticStatus.Submitted or DiagnosticStatus.Validated);
    }
}

/// <summary>« Convertir en demande en un clic »: an anomaly becomes a normal-urgency request on its site.</summary>
public sealed class ConvertAnomalyHandler(IApplicationDbContext db, BusinessAccess access, CreateRequestHandler createRequest)
{
    public async Task<Result<Guid>> HandleAsync(Guid userId, Guid diagnosticId, Guid controlPointId, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        var diagnostic = await db.Diagnostics.FirstOrDefaultAsync(d => d.Id == diagnosticId, cancellationToken);
        var point = diagnostic?.ControlPoints.FirstOrDefault(c => c.Id == controlPointId);
        if (member is null || diagnostic is null || point is null || diagnostic.AccountId != member.Account.Id)
        {
            return Result<Guid>.Failure("diagnostic.not_found");
        }

        if (!member.CanApprove)
        {
            return Result<Guid>.Failure("business.approver_only");
        }

        if (point.ConvertedRequestId is { } existing)
        {
            return Result<Guid>.Success(existing);
        }

        if (point.Result != ControlPointResult.Anomaly || diagnostic.Status is not (DiagnosticStatus.Submitted or DiagnosticStatus.Validated))
        {
            return Result<Guid>.Failure("diagnostic.not_anomaly");
        }

        var siteId = point.PropertyId ?? (await access.SiteIdsAsync(member, cancellationToken)) switch { [var first, ..] => first, _ => Guid.Empty };
        if (siteId == Guid.Empty || !member.CanAccess(siteId))
        {
            return Result<Guid>.Failure("diagnostic.not_found");
        }

        var description = string.IsNullOrWhiteSpace(point.Comment) ? point.Label : $"{point.Label} — {point.Comment}";
        var created = await createRequest.HandleAsync(
            new CreateRequestCommand(userId, siteId, point.Domain, description, Urgency.Normal, [], [], null, $"diag-{point.Id:N}"),
            cancellationToken);
        if (!created.IsSuccess)
        {
            return Result<Guid>.Failure(created.Error);
        }

        diagnostic.MarkAnomalyConverted(point.Id, created.Value.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(created.Value.Id);
    }
}
