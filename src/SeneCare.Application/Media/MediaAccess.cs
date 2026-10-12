using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;

namespace SeneCare.Application.Media;

public sealed record MediaContent(Stream Content, string ContentType);

/// <summary>
/// Serves a stored file only to members of the owning account, its uploader or SENECARE staff.
/// Anyone else gets « not found » (no hint that the file exists).
/// </summary>
public sealed class MediaAccess(IApplicationDbContext db, AccessControl access, IFileStorage storage)
{
    public async Task<MediaContent?> OpenAsync(Guid userId, bool isStaff, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var media = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken);
        if (media is null)
        {
            return null;
        }

        var allowed = isStaff
            || media.UploadedByUserId == userId
            || (media.AccountId is { } accountId && await access.CanAccessAccountAsync(userId, accountId, cancellationToken));
        if (!allowed)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(media.StorageKey, cancellationToken);
        return stream is null ? null : new MediaContent(stream, media.ContentType);
    }
}
