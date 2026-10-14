using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Public;

/// <summary>An offer as shown on the public site: prices come from the database, never from the page.</summary>
public sealed record PublicPlan(
    string Code,
    string Name,
    AccountKind Segment,
    Money RegistrationFee,
    Money MonthlyPrice,
    TimeSpan? CriticalDelay,
    int? IncludedVisitsPerPeriod,
    bool DiagnosticRequired);

public sealed record PublicAdvice(string Title, string Body, ServiceDomain? Domain);

/// <summary>
/// Real figures only. <see cref="HasEnough"/> is false until there is enough activity to mean something:
/// the page then shows the service commitments instead of small numbers.
/// </summary>
public sealed record PublicFigures(int CompletedInterventions, double? AverageRating, int Ratings, int Neighborhoods)
{
    public const int MinimumInterventions = 20;
    public const int MinimumRatings = 10;

    public bool HasEnough => CompletedInterventions >= MinimumInterventions && Ratings >= MinimumRatings;
}

public sealed record PublicHome(
    IReadOnlyList<PublicPlan> Plans,
    PublicFigures Figures,
    IReadOnlyList<PublicAdvice> Advice,
    int NeighborhoodThreshold);

/// <summary>Data of the public home page (anonymous visitors): offers, aggregate figures, seasonal advice.</summary>
public sealed class GetPublicHomeHandler(IApplicationDbContext db, Settings settings, TimeProvider clock)
{
    public async Task<PublicHome> HandleAsync(CancellationToken cancellationToken)
    {
        var plans = (await db.Plans.AsNoTracking().Where(p => p.IsActive).ToListAsync(cancellationToken))
            .OrderBy(p => p.Segment)
            .ThenBy(p => p.MonthlyPrice.Amount)
            .Select(p => new PublicPlan(
                p.Code,
                p.Name,
                p.Segment,
                p.RegistrationFee,
                p.MonthlyPrice,
                p.GetMaxDelay(Urgency.Critical),
                p.IncludedVisitsPerPeriod,
                p.DiagnosticRequired))
            .ToList();

        var done = db.ServiceRequests.AsNoTracking()
            .Where(r => r.Status == RequestStatus.Completed || r.Status == RequestStatus.ValidatedByCustomer);
        var completed = await done.CountAsync(cancellationToken);
        var scores = await done.Where(r => r.Rating != null).Select(r => r.Rating!.Score).ToListAsync(cancellationToken);
        var neighborhoods = await db.Properties.AsNoTracking()
            .Select(p => p.Address.NeighborhoodId)
            .Distinct()
            .CountAsync(cancellationToken);
        var figures = new PublicFigures(completed, scores.Count == 0 ? null : scores.Average(), scores.Count, neighborhoods);

        // Advice of the current month first, then the rest; three at most.
        var month = clock.GetUtcNow().Month;
        var advice = (await db.PreventiveAdvices.AsNoTracking().Where(a => a.IsPublished).ToListAsync(cancellationToken))
            .OrderByDescending(a => a.Months.Contains(month))
            .ThenByDescending(a => a.UpdatedAt)
            .Take(3)
            .Select(a => new PublicAdvice(a.Title, a.Body, a.Domain))
            .ToList();

        var threshold = await settings.IntAsync(SettingKeys.NeighborhoodThreshold, cancellationToken);
        return new PublicHome(plans, figures, advice, threshold);
    }
}
