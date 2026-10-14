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

/// <summary>A review the customer agreed to publish: first name and initial only, commune, score, text.</summary>
public sealed record PublicReview(string Author, string Commune, int Score, string Comment, DateTimeOffset GivenAt, ServiceDomain Domain)
{
    /// <summary>« Awa Ndiaye » → « Awa N. »; nothing more about the person.</summary>
    public static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}

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
    IReadOnlyList<PublicReview> Reviews,
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

        // Latest published reviews, whatever their score: consent given, not hidden by moderation.
        var reviews = (await (
                    from r in db.ServiceRequests.AsNoTracking()
                    where r.Rating != null && r.Rating.PublicationConsent && !r.Rating.IsHidden && r.Rating.Comment != null
                    // Attributed to whoever gave the rating and the consent, not to the reporter.
                    join u in db.Users on r.Rating!.AuthorUserId equals (Guid?)u.Id
                    where u.FullName != null && u.AnonymizedAt == null
                    join p in db.Properties on r.PropertyId equals p.Id
                    join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                    join c in db.Communes on n.CommuneId equals c.Id
                    orderby r.Rating!.GivenAt descending
                    select new { u.FullName, Commune = c.Name, r.Rating!.Score, r.Rating!.Comment, r.Rating!.GivenAt, r.Domain })
                .Take(3)
                .ToListAsync(cancellationToken))
            .Select(x => new PublicReview(PublicReview.ShortName(x.FullName!), x.Commune, x.Score, x.Comment!, x.GivenAt, x.Domain))
            .ToList();

        var threshold = await settings.IntAsync(SettingKeys.NeighborhoodThreshold, cancellationToken);
        return new PublicHome(plans, figures, advice, reviews, threshold);
    }
}
