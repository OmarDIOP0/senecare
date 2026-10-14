using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Admin;

/// <summary>A review the customer agreed to publish, as the back office sees it.</summary>
public sealed record ReviewRow(Guid RequestId, string Number, string? Author, string Commune, int Score, string Comment, DateTimeOffset GivenAt, bool IsHidden);

/// <summary>
/// Moderation of the reviews customers agreed to publish (consent given when validating an intervention).
/// Hiding keeps the score in the averages and only removes the text from the public site.
/// </summary>
public sealed class ReviewModeration(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<ReviewRow>> ListAsync(CancellationToken cancellationToken = default) =>
        await (
                from r in db.ServiceRequests.AsNoTracking()
                where r.Rating != null && r.Rating.PublicationConsent && r.Rating.Comment != null
                join u in db.Users on r.Rating!.AuthorUserId equals (Guid?)u.Id
                join p in db.Properties on r.PropertyId equals p.Id
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                orderby r.Rating!.GivenAt descending
                select new ReviewRow(r.Id, r.Number, u.FullName, c.Name, r.Rating!.Score, r.Rating!.Comment!, r.Rating!.GivenAt, r.Rating!.IsHidden))
            .Take(200)
            .ToListAsync(cancellationToken);

    public async Task<Result<Guid>> SetHiddenAsync(Guid adminId, Guid requestId, bool hidden, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Result<Guid>.Failure("request.not_found");
        }

        try
        {
            request.SetReviewHidden(hidden);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.AuditLogs.Add(new AuditLog(adminId, hidden ? "content.review_hide" : "content.review_show", nameof(ServiceRequest), request.Id.ToString(), false, null, request.Number, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }
}
