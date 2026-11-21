using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Localization;
using SeneCare.Application.Abstractions;
using SeneCare.Web.Localization;

namespace SeneCare.Web.Hubs;

/// <summary>
/// Signed-in pages listen here: each new notification of the person arrives at once (bell, short message, refresh of
/// the page concerned). Addressed per user (sign-in user id), nothing to call from the page.
/// </summary>
[Authorize]
public sealed class LiveHub : Hub
{
    public const string Path = "/hubs/direct";
}

/// <summary>Sends the saved notifications to their people's open pages: localized title and the link of the notification.</summary>
public sealed class LiveNotifications(IHubContext<LiveHub> hub, IStringLocalizer<SharedResource> localizer) : ILiveNotifications
{
    public async Task PublishAsync(IReadOnlyList<LiveNotification> notifications, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        foreach (var notification in notifications)
        {
            var title = localizer["Notif_" + notification.TemplateKey.Replace('.', '_') + "_Title"];
            try
            {
                await hub.Clients.User(notification.UserId.ToString()).SendAsync(
                    "notify", new { title = title.ResourceNotFound ? string.Empty : title.Value, url = Url(notification.PayloadJson) }, cancellationToken);
            }
#pragma warning disable CA1031 // A page that cannot be reached must never undo the saved action.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                _ = ex;
            }
        }
    }

    private static string? Url(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("url", out var url)
                && url.ValueKind == JsonValueKind.String && url.GetString() is { } value && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
