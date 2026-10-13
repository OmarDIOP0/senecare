using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SeneCare.Application.Technicians;
using SeneCare.Infrastructure.Identity;

namespace SeneCare.Web.Hubs;

/// <summary>
/// Live follow-up of a request: the technician « en route » shares its position, the customer sees it
/// move. Status changes are pushed too, so the customer page refreshes by itself.
/// </summary>
[Authorize]
public sealed class TrackingHub(PositionSharing sharing) : Hub
{
    public const string Path = "/hubs/suivi";

    public static string Group(Guid requestId) => "request:" + requestId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Technician: sends its position (the phone throttles to about one every 20 s).</summary>
    public async Task Share(Guid interventionId, double latitude, double longitude)
    {
        var userId = Context.User?.GetUserId();
        if (userId is null)
        {
            return;
        }

        var shared = await sharing.ShareAsync(userId.Value, interventionId, latitude, longitude, Context.ConnectionAborted);
        if (shared is { } s)
        {
            await Clients.Group(Group(s.RequestId)).SendAsync("position", s.Position.Latitude, s.Position.Longitude, s.Position.At, Context.ConnectionAborted);
        }
    }

    /// <summary>Customer: joins the request's group and gets the last known position (or null).</summary>
    public async Task<LivePosition?> Follow(Guid requestId)
    {
        var userId = Context.User?.GetUserId();
        if (userId is null)
        {
            return null;
        }

        var (allowed, position) = await sharing.FollowAsync(userId.Value, requestId, Context.ConnectionAborted);
        if (!allowed)
        {
            throw new HubException("Not allowed.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(requestId), Context.ConnectionAborted);
        return position;
    }
}
