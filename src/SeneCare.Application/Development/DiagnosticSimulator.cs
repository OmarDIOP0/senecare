using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Business;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;

namespace SeneCare.Application.Development;

/// <summary>
/// Development only (the Web exposes it in Development): the demo technician carries out and the
/// supervisor validates the planned diagnostic, so the business journey can be tried before Phase 8.
/// </summary>
public sealed class DiagnosticSimulator(
    IApplicationDbContext db,
    BusinessAccess access,
    DiagnosticPlanner planner,
    RequestSimulator requests,
    TimeProvider clock)
{
    private static readonly (ServiceDomain Domain, string Label, ControlPointResult Result, string? Comment)[] Checks =
    [
        (ServiceDomain.Electricity, "Tableau électrique et différentiels", ControlPointResult.Ok, null),
        (ServiceDomain.Plumbing, "Robinetterie et chasse d'eau des toilettes", ControlPointResult.Anomaly, "Fuite lente sur la chasse d'eau du WC du rez-de-chaussée."),
        (ServiceDomain.Waterproofing, "Toiture-terrasse et acrotères", ControlPointResult.Anomaly, "Fissure de 40 cm sur l'acrotère nord : à reprendre avant l'hivernage."),
        (ServiceDomain.Security, "Caméras et enregistreur", ControlPointResult.Ok, null),
        (ServiceDomain.Carpentry, "Portes et fenêtres", ControlPointResult.NotApplicable, null),
    ];

    public async Task<Result<Guid>> CompleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        if (member is not { IsManager: true })
        {
            return Result<Guid>.Failure("business.manager_only");
        }

        var subscription = await access.SubscriptionAsync(member.Account.Id, cancellationToken);
        if (subscription is null)
        {
            return Result<Guid>.Failure("subscription.not_found");
        }

        await planner.EnsurePlannedAsync(subscription, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var diagnostic = await db.Diagnostics
            .Where(d => d.SubscriptionId == subscription.Id && d.Status == DiagnosticStatus.Planned)
            .OrderBy(d => d.ScheduledFor)
            .FirstOrDefaultAsync(cancellationToken);
        if (diagnostic is null)
        {
            return Result<Guid>.Failure("simulation.no_diagnostic");
        }

        var now = clock.GetUtcNow();
        var technician = await requests.DemoTechnicianAsync(ServiceDomain.Waterproofing, cancellationToken);
        diagnostic.Start(technician.Id, now);
        foreach (var siteId in subscription.CoveredPropertyIds)
        {
            foreach (var (domain, label, result, comment) in Checks)
            {
                diagnostic.RecordControlPoint(siteId, domain, label, result, comment);
            }
        }

        diagnostic.Submit("Reprendre l'étanchéité de l'acrotère avant juillet. Prochain contrôle des climatiseurs au trimestre suivant.", null, now);
        diagnostic.Validate(userId, now);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(diagnostic.Id);
    }
}
