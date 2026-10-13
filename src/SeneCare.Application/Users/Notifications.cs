using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Platform;

namespace SeneCare.Application.Users;

public sealed record InAppNotification(Guid Id, string TemplateKey, string PayloadJson, DateTimeOffset At, bool IsUnread);

/// <summary>The bell of the topbar: the user's in-app notifications, most recent first.</summary>
public sealed class NotificationInbox(IApplicationDbContext db, TimeProvider clock)
{
    public const int PageSize = 20;

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && n.Channel == NotificationChannel.InApp && n.ReadAt == null, cancellationToken);

    /// <summary>Latest notifications; opening the panel marks them as read.</summary>
    public async Task<IReadOnlyList<InAppNotification>> OpenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await db.Notifications
            .Where(n => n.UserId == userId && n.Channel == NotificationChannel.InApp)
            .OrderByDescending(n => n.CreatedAt)
            .Take(PageSize)
            .ToListAsync(cancellationToken);
        var result = items.Select(n => new InAppNotification(n.Id, n.TemplateKey, n.PayloadJson, n.CreatedAt, n.ReadAt is null)).ToList();

        var now = clock.GetUtcNow();
        foreach (var unread in items.Where(n => n.ReadAt is null))
        {
            unread.MarkRead(now);
        }

        if (result.Exists(n => n.IsUnread))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
