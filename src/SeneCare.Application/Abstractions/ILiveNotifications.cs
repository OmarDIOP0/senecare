namespace SeneCare.Application.Abstractions;

/// <summary>A notification just recorded for a user (template key and JSON payload, as in the bell).</summary>
public sealed record LiveNotification(Guid UserId, string TemplateKey, string PayloadJson);

/// <summary>
/// Pushes notifications to the people's open pages at once (web: SignalR), once they are saved: the bell lights up,
/// a short message shows and the page concerned refreshes. Optional: without it (API, tests), nothing is pushed.
/// </summary>
public interface ILiveNotifications
{
    Task PublishAsync(IReadOnlyList<LiveNotification> notifications, CancellationToken cancellationToken = default);
}
