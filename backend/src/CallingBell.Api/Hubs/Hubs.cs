using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CallingBell.Api.Hubs;

/// <summary>
/// Real-time availability network. Clients subscribe to the businesses currently on screen and
/// receive "availabilityChanged" events when an owner changes status.
/// </summary>
public sealed class PresenceHub : Hub
{
    public const string AvailabilityChanged = "availabilityChanged";

    public static string BusinessGroup(Guid businessId) => $"business:{businessId:N}";

    public async Task Watch(Guid[] businessIds)
    {
        foreach (var id in businessIds.Distinct().Take(100))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BusinessGroup(id));
        }
    }

    public async Task Unwatch(Guid[] businessIds)
    {
        foreach (var id in businessIds.Distinct().Take(100))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BusinessGroup(id));
        }
    }
}

/// <summary>Per-user alerts: new leads, bookings, review and listing updates.</summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    public const string NotificationReceived = "notification";
}

public sealed class SignalRNotifier(IHubContext<PresenceHub> presence, IHubContext<NotificationHub> notifications) : IRealtimeNotifier
{
    public Task AvailabilityChangedAsync(Guid businessId, string status, DateTimeOffset lastSeenOn, CancellationToken ct = default) =>
        presence.Clients.Group(PresenceHub.BusinessGroup(businessId))
            .SendAsync(PresenceHub.AvailabilityChanged, new { businessId, status, lastSeenOn }, ct);

    public Task NotifyUserAsync(string userId, NotificationDto notification, CancellationToken ct = default) =>
        notifications.Clients.User(userId).SendAsync(NotificationHub.NotificationReceived, notification, ct);
}
