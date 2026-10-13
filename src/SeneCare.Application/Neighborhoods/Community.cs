using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Media;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Neighborhoods;

public sealed record PublicOperation(DateOnly Date, int Trees, string Description, IReadOnlyList<Guid> PhotoIds);

/// <summary>What anyone can see about a neighborhood (no personal data: counts and published operations only).</summary>
public sealed record NeighborhoodPage(
    string Name,
    string Commune,
    string CommuneSlug,
    string Slug,
    int Members,
    int Threshold,
    NeighborhoodProgramStatus Status,
    int Trees,
    IReadOnlyList<PublicOperation> Operations)
{
    public int Remaining => Math.Max(0, Threshold - Members);

    public int Percent => Math.Min(100, (int)Math.Round(100.0 * Members / Math.Max(1, Threshold)));
}

public sealed record RankingRow(int Rank, string Name, string Commune, string CommuneSlug, string Slug, int Members, int Threshold, NeighborhoodProgramStatus Status, int Trees)
{
    public int Percent => Math.Min(100, (int)Math.Round(100.0 * Members / Math.Max(1, Threshold)));
}

/// <summary>
/// Public side of the Programme Quartiers: a shareable page per neighborhood, a sober ranking, and the
/// photos of published operations (the only files ever served without signing in).
/// </summary>
public sealed class NeighborhoodCommunity(IApplicationDbContext db, AccessControl access, IFileStorage storage)
{
    public const int RankingSize = 20;

    public async Task<NeighborhoodPage?> PageAsync(string communeSlug, string slug, CancellationToken cancellationToken = default)
    {
        var row = await (
                from n in db.Neighborhoods
                join c in db.Communes on n.CommuneId equals c.Id
                join p in db.NeighborhoodPrograms on n.Id equals p.NeighborhoodId
                where c.Slug == communeSlug && n.Slug == slug
                select new { Neighborhood = n.Name, NeighborhoodSlug = n.Slug, Commune = c.Name, CommuneSlug = c.Slug, Program = p })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var program = row.Program;
        return new NeighborhoodPage(
            row.Neighborhood, row.Commune, row.CommuneSlug, row.NeighborhoodSlug, program.MemberCount, program.Threshold, program.Status, program.PublishedTreesPlanted,
            [.. program.Operations.Where(o => o.IsPublished).OrderByDescending(o => o.Date)
                .Select(o => new PublicOperation(o.Date, o.TreesPlanted, o.Description, [.. o.Photos.Select(ph => ph.MediaFileId)]))]);
    }

    /// <summary>The neighborhood of the user's first building (to share it, or show its news).</summary>
    public async Task<NeighborhoodPage?> OfUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var propertyIds = await access.PropertyIdsOfAsync(userId, cancellationToken);
        var slugs = await (
                from p in db.Properties
                join n in db.Neighborhoods on p.Address.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where propertyIds.Contains(p.Id)
                orderby p.CreatedAt
                select new { Commune = c.Slug, Neighborhood = n.Slug })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        return slugs is null ? null : await PageAsync(slugs.Commune, slugs.Neighborhood, cancellationToken);
    }

    /// <summary>
    /// Most engaged neighborhoods: those already active (most trees first), then the closest to their
    /// threshold. Only neighborhoods with members appear.
    /// </summary>
    public async Task<IReadOnlyList<RankingRow>> RankingAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from p in db.NeighborhoodPrograms
                join n in db.Neighborhoods on p.NeighborhoodId equals n.Id
                join c in db.Communes on n.CommuneId equals c.Id
                where p.MemberCount > 0
                select new { Program = p, n.Name, n.Slug, Commune = c.Name, CommuneSlug = c.Slug })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return [.. rows
            .OrderByDescending(r => r.Program.Status)
            .ThenByDescending(r => r.Program.PublishedTreesPlanted)
            .ThenByDescending(r => (double)r.Program.MemberCount / Math.Max(1, r.Program.Threshold))
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Take(RankingSize)
            .Select((r, i) => new RankingRow(i + 1, r.Name, r.Commune, r.CommuneSlug, r.Slug, r.Program.MemberCount, r.Program.Threshold, r.Program.Status, r.Program.PublishedTreesPlanted))];
    }

    /// <summary>A photo of a published operation, for the public page; anything else stays private.</summary>
    public async Task<MediaContent?> PublicPhotoAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var published = (await db.NeighborhoodPrograms.AsNoTracking()
                .Where(p => p.Status == NeighborhoodProgramStatus.Active)
                .ToListAsync(cancellationToken))
            .SelectMany(p => p.Operations)
            .Any(o => o.IsPublished && o.Photos.Any(ph => ph.MediaFileId == mediaId));
        if (!published)
        {
            return null;
        }

        var media = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken);
        var stream = media is null ? null : await storage.OpenReadAsync(media.StorageKey, cancellationToken);
        return stream is null ? null : new MediaContent(stream, media!.ContentType);
    }
}

public sealed record ReferralStats(string Code, int Invited, int Members);

/// <summary>
/// « Parrainage »: each customer shares a link carrying a personal code; a neighbour who signs up through
/// it is linked to them, so they can see how many people they brought in.
/// </summary>
public sealed class ReferralService(IApplicationDbContext db)
{
    // No 0/O/1/I: codes are read aloud and typed on phones.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<ReferralStats?> StatsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (user.ReferralCode is null)
        {
            user.AssignReferralCode(await NewCodeAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }

        var invited = await db.Users.AsNoTracking().Where(u => u.ReferredByUserId == userId).Select(u => u.Id).ToListAsync(cancellationToken);
        var members = invited.Count == 0
            ? 0
            : await db.CustomerAccounts.AsNoTracking()
                .Where(a => a.Members.Any(m => invited.Contains(m.UserId)))
                .Where(a => db.Subscriptions.Any(s => s.AccountId == a.Id && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)))
                .CountAsync(cancellationToken);
        return new ReferralStats(user.ReferralCode!, invited.Count, members);
    }

    /// <summary>At registration: links the new user to the owner of <paramref name="code"/> (unknown codes are ignored).</summary>
    public async Task AttachAsync(Guid newUserId, string? code, CancellationToken cancellationToken = default)
    {
        if (code is not { Length: User.ReferralCodeLength })
        {
            return;
        }

        var normalized = code.ToUpperInvariant();
        var referrer = await db.Users.AsNoTracking().Where(u => u.ReferralCode == normalized).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == newUserId, cancellationToken);
        if (referrer is null || user is null)
        {
            return;
        }

        user.SetReferrer(referrer.Value);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> NewCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = string.Create(User.ReferralCodeLength, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
                }
            });
            if (!await db.Users.AnyAsync(u => u.ReferralCode == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("No free referral code.");
    }
}
