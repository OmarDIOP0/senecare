using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Media;
using SeneCare.Application.Resources;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Technicians;

/// <summary>
/// Gives a confirmed request (or a planned diagnostic) to a technician. Used by the dispatch screen
/// (Phase 9) and by the development simulator.
/// </summary>
public sealed class AssignmentHandler(IApplicationDbContext db)
{
    public async Task<Result<Guid>> AssignInterventionAsync(Guid requestId, Guid technicianId, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        var technician = await db.Technicians.FirstOrDefaultAsync(t => t.Id == technicianId && t.IsActive, cancellationToken);
        if (request is null || technician is null)
        {
            return Result<Guid>.Failure("assignment.not_found");
        }

        if (request.Status != RequestStatus.AppointmentConfirmed)
        {
            return Result<Guid>.Failure("assignment.not_confirmed");
        }

        if (await db.Interventions.AnyAsync(i => i.RequestId == request.Id && i.CompletedAt == null, cancellationToken))
        {
            return Result<Guid>.Failure("assignment.already_assigned");
        }

        var language = await db.Users.Where(u => u.Id == technician.UserId).Select(u => u.Language).FirstAsync(cancellationToken);
        var intervention = new Intervention(request.Id, request.PropertyId, request.Domain, technician.Id, Messages.Checklist(request.Domain, language));
        db.Interventions.Add(intervention);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(intervention.Id);
    }

    public async Task<Result<Guid>> AssignDiagnosticAsync(Guid diagnosticId, Guid technicianId, CancellationToken cancellationToken = default)
    {
        var diagnostic = await db.Diagnostics.FirstOrDefaultAsync(d => d.Id == diagnosticId, cancellationToken);
        if (diagnostic is null || !await db.Technicians.AnyAsync(t => t.Id == technicianId && t.IsActive, cancellationToken))
        {
            return Result<Guid>.Failure("assignment.not_found");
        }

        try
        {
            diagnostic.Assign(technicianId);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(diagnostic.Id);
    }
}

public sealed record DiagnosticSiteOption(Guid Id, string Name, string Place);

public sealed record FieldControlPoint(Guid Id, Guid? SiteId, ServiceDomain Domain, string Label, ControlPointResult Result, string? Comment, int Photos);

public sealed record DiagnosticMission(
    Guid Id,
    string AccountName,
    DiagnosticStatus Status,
    DateTimeOffset ScheduledFor,
    DateRange Period,
    IReadOnlyList<DiagnosticSiteOption> Sites,
    IReadOnlyList<FieldControlPoint> Points,
    string? Recommendations)
{
    public bool CanEdit => Status is DiagnosticStatus.Planned or DiagnosticStatus.InProgress;
}

/// <summary>Quarterly diagnostic on the field: control points per site and trade, then the report.</summary>
public sealed class DiagnosticFieldHandler(IApplicationDbContext db, TechnicianAccess technicians, MediaUploader uploader, TimeProvider clock)
{
    public async Task<DiagnosticMission?> DetailAsync(Guid userId, Guid diagnosticId, CancellationToken cancellationToken = default)
    {
        var diagnostic = await LoadAsync(userId, diagnosticId, cancellationToken);
        if (diagnostic is null)
        {
            return null;
        }

        var account = await db.CustomerAccounts.AsNoTracking().FirstAsync(a => a.Id == diagnostic.AccountId, cancellationToken);
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

        return new DiagnosticMission(
            diagnostic.Id, account.DisplayName, diagnostic.Status, diagnostic.ScheduledFor, diagnostic.PeriodCovered, sites,
            [.. diagnostic.ControlPoints.Select(c => new FieldControlPoint(c.Id, c.PropertyId, c.Domain, c.Label, c.Result, c.Comment, c.Photos.Count))],
            diagnostic.Recommendations);
    }

    /// <summary>Records one control point (starting the diagnostic on the first one).</summary>
    public async Task<Result<Guid>> AddPointAsync(
        Guid userId,
        Guid diagnosticId,
        Guid? siteId,
        ServiceDomain? domain,
        string? label,
        ControlPointResult? result,
        string? comment,
        UploadedFile? photo,
        CancellationToken cancellationToken = default)
    {
        if (domain is null || result is null)
        {
            return Result<Guid>.Failure("controlpoint.incomplete");
        }

        var diagnostic = await LoadAsync(userId, diagnosticId, cancellationToken, tracked: true);
        var technician = await technicians.OfAsync(userId, cancellationToken);
        if (diagnostic is null || technician is null)
        {
            return Result<Guid>.Failure("mission.not_found");
        }

        if (siteId is { } site)
        {
            var covered = await db.Subscriptions.AnyAsync(s => s.Id == diagnostic.SubscriptionId && s.CoveredPropertyIds.Contains(site), cancellationToken);
            if (!covered)
            {
                return Result<Guid>.Failure("mission.not_found");
            }
        }

        try
        {
            if (diagnostic.Status == DiagnosticStatus.Planned)
            {
                diagnostic.Start(technician.Id, clock.GetUtcNow());
            }

            var point = diagnostic.RecordControlPoint(siteId, domain.Value, label ?? string.Empty, result.Value, comment);
            if (photo is not null)
            {
                var stored = await uploader.StoreAsync(photo, MediaKind.Photo, userId, diagnostic.AccountId, cancellationToken);
                if (!stored.IsSuccess)
                {
                    return Result<Guid>.Failure(stored.Error);
                }

                diagnostic.AddControlPointPhoto(point.Id, stored.Value.Id);
            }
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(diagnostic.Id);
    }

    /// <summary>Report handed in; the supervisor validates it (which unlocks the next quarter's invoice).</summary>
    public async Task<Result<Guid>> SubmitAsync(Guid userId, Guid diagnosticId, string? recommendations, CancellationToken cancellationToken = default)
    {
        var diagnostic = await LoadAsync(userId, diagnosticId, cancellationToken, tracked: true);
        if (diagnostic is null)
        {
            return Result<Guid>.Failure("mission.not_found");
        }

        if (diagnostic.Status == DiagnosticStatus.Submitted)
        {
            return Result<Guid>.Success(diagnostic.Id);
        }

        try
        {
            diagnostic.Submit(recommendations, null, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(diagnostic.Id);
    }

    private async Task<Diagnostic?> LoadAsync(Guid userId, Guid diagnosticId, CancellationToken cancellationToken, bool tracked = false)
    {
        var technician = await technicians.OfAsync(userId, cancellationToken);
        if (technician is null)
        {
            return null;
        }

        var query = tracked ? db.Diagnostics : db.Diagnostics.AsNoTracking();
        return await query.FirstOrDefaultAsync(d => d.Id == diagnosticId && d.TechnicianId == technician.Id, cancellationToken);
    }
}

public sealed record LivePosition(double Latitude, double Longitude, DateTimeOffset At);

/// <summary>Last known position of the technician heading to a request. Memory only: nothing is kept in the database.</summary>
public interface ILivePositions
{
    void Set(Guid requestId, LivePosition position);

    LivePosition? Get(Guid requestId);

    void Clear(Guid requestId);
}

/// <summary>
/// « En route » position sharing: only the technician of the mission, only while on the way, only
/// coordinates inside Senegal; only the customer's account members may follow it.
/// </summary>
public sealed class PositionSharing(IApplicationDbContext db, TechnicianAccess technicians, AccessControl access, ILivePositions positions, TimeProvider clock)
{
    public async Task<(Guid RequestId, LivePosition Position)?> ShareAsync(
        Guid userId, Guid interventionId, double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        // Senegal bounding box (with margin): rejects junk and stray positions.
        if (latitude is < 12 or > 17 || longitude is < -18 or > -11)
        {
            return null;
        }

        var technician = await technicians.OfAsync(userId, cancellationToken);
        var mission = await (
                from i in db.Interventions
                join r in db.ServiceRequests on i.RequestId equals r.Id
                where i.Id == interventionId
                select new { i.LeadTechnicianId, Team = i.TeamTechnicianIds, r.Id, r.Status })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (technician is null || mission is null || mission.Status != RequestStatus.OnTheWay
            || (mission.LeadTechnicianId != technician.Id && !mission.Team.Contains(technician.Id)))
        {
            return null;
        }

        var position = new LivePosition(Math.Round(latitude, 5), Math.Round(longitude, 5), clock.GetUtcNow());
        positions.Set(mission.Id, position);
        return (mission.Id, position);
    }

    /// <summary>Customer side: may this user follow the request, and where is the technician now?</summary>
    public async Task<(bool Allowed, LivePosition? Position)> FollowAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.AsNoTracking().Where(r => r.Id == requestId).Select(r => new { r.PropertyId }).FirstOrDefaultAsync(cancellationToken);
        if (request is null || await access.PropertyAsync(userId, request.PropertyId, cancellationToken) is null)
        {
            return (false, null);
        }

        return (true, positions.Get(requestId));
    }
}
