using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Technicians;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Admin;

public sealed record SupervisedDiagnostic(
    Guid Id,
    string AccountName,
    DiagnosticStatus Status,
    DateTimeOffset ScheduledFor,
    DateRange Period,
    string? TechnicianName,
    int Points,
    int Anomalies,
    int Sites,
    Guid? FirstSiteCommuneId);

/// <summary>
/// Quarterly diagnostics seen by the supervisor: give the planned ones to a technician (or move them),
/// then validate the reports handed in — which unlocks the next quarter's invoice — or send them back.
/// </summary>
public sealed class DiagnosticSupervision(IApplicationDbContext db, AssignmentHandler assignments, TimeProvider clock)
{
    public async Task<IReadOnlyList<SupervisedDiagnostic>> ListAsync(CancellationToken cancellationToken = default)
    {
        var diagnostics = await db.Diagnostics.AsNoTracking()
            .Where(d => d.Status != DiagnosticStatus.Cancelled && d.Status != DiagnosticStatus.Validated)
            .OrderBy(d => d.Status == DiagnosticStatus.Submitted ? 0 : 1)
            .ThenBy(d => d.ScheduledFor)
            .Take(200)
            .ToListAsync(cancellationToken);
        var accountIds = diagnostics.Select(d => d.AccountId).Distinct().ToList();
        var accounts = await db.CustomerAccounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.DisplayName, cancellationToken);
        var subscriptionIds = diagnostics.Select(d => d.SubscriptionId).Distinct().ToList();
        var sites = await db.Subscriptions.AsNoTracking().Where(s => subscriptionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.CoveredPropertyIds.ToList(), cancellationToken);
        var firstSites = sites.Values.Where(v => v.Count > 0).Select(v => v[0]).ToList();
        var communes = await (
                from p in db.Properties
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                where firstSites.Contains(p.Id)
                select new { p.Id, n.CommuneId })
            .ToDictionaryAsync(x => x.Id, x => x.CommuneId, cancellationToken);
        var technicianIds = diagnostics.Where(d => d.TechnicianId != null).Select(d => d.TechnicianId!.Value).Distinct().ToList();
        var names = await (
                from t in db.Technicians
                join u in db.Users on t.UserId equals u.Id
                where technicianIds.Contains(t.Id)
                select new { t.Id, u.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        return [.. diagnostics.Select(d =>
        {
            var covered = sites.GetValueOrDefault(d.SubscriptionId) ?? [];
            return new SupervisedDiagnostic(
                d.Id, accounts.GetValueOrDefault(d.AccountId, string.Empty), d.Status, d.ScheduledFor, d.PeriodCovered,
                d.TechnicianId is { } t ? names.GetValueOrDefault(t) : null, d.ControlPoints.Count, d.AnomalyCount, covered.Count,
                covered.Count > 0 && communes.TryGetValue(covered[0], out var commune) ? commune : null);
        })];
    }

    /// <summary>Report content for review: control points per site, recommendations.</summary>
    public async Task<(SupervisedDiagnostic Diagnostic, IReadOnlyList<DiagnosticSiteOption> Sites, IReadOnlyList<FieldControlPoint> Points, string? Recommendations)?> DetailAsync(
        Guid diagnosticId, CancellationToken cancellationToken = default)
    {
        var summary = (await ListAsync(cancellationToken)).FirstOrDefault(d => d.Id == diagnosticId);
        var diagnostic = await db.Diagnostics.AsNoTracking().FirstOrDefaultAsync(d => d.Id == diagnosticId, cancellationToken);
        if (summary is null || diagnostic is null)
        {
            return null;
        }

        var subscription = await db.Subscriptions.AsNoTracking().FirstAsync(s => s.Id == diagnostic.SubscriptionId, cancellationToken);
        var siteIds = subscription.CoveredPropertyIds.ToList();
        var sites = await (
                from p in db.Properties
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                where siteIds.Contains(p.Id)
                orderby p.Name
                select new DiagnosticSiteOption(p.Id, p.Name, n.Name))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return (summary, sites,
            [.. diagnostic.ControlPoints.Select(c => new FieldControlPoint(c.Id, c.PropertyId, c.Domain, c.Label, c.Result, c.Comment, c.Photos.Count))],
            diagnostic.Recommendations);
    }

    public Task<Result<Guid>> AssignAsync(Guid diagnosticId, Guid technicianId, CancellationToken cancellationToken = default) =>
        assignments.AssignDiagnosticAsync(diagnosticId, technicianId, cancellationToken);

    public Task<Result<Guid>> RescheduleAsync(Guid diagnosticId, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default) =>
        scheduledFor < clock.GetUtcNow().AddHours(-1)
            ? Task.FromResult(Result<Guid>.Failure("dispatch.past_date"))
            : ChangeAsync(diagnosticId, d => d.Reschedule(scheduledFor), cancellationToken);

    public async Task<Result<Guid>> ValidateAsync(Guid supervisorId, Guid diagnosticId, CancellationToken cancellationToken = default)
    {
        var result = await ChangeAsync(diagnosticId, d => d.Validate(supervisorId, clock.GetUtcNow()), cancellationToken);
        if (result.IsSuccess)
        {
            db.AuditLogs.Add(new AuditLog(supervisorId, "diagnostic.validate", nameof(Diagnostic), diagnosticId.ToString(), false, null, null, clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>Back to the technician for completion (missing site, unclear photo…).</summary>
    public Task<Result<Guid>> ReturnAsync(Guid diagnosticId, CancellationToken cancellationToken = default) =>
        ChangeAsync(diagnosticId, d => d.Reject(), cancellationToken);

    private async Task<Result<Guid>> ChangeAsync(Guid diagnosticId, Action<Diagnostic> change, CancellationToken cancellationToken)
    {
        var diagnostic = await db.Diagnostics.FirstOrDefaultAsync(d => d.Id == diagnosticId, cancellationToken);
        if (diagnostic is null)
        {
            return Result<Guid>.Failure("diagnostic.not_found");
        }

        try
        {
            change(diagnostic);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(diagnostic.Id);
    }
}
