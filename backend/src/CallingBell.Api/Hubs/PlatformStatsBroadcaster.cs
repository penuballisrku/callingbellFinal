using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Content;
using Microsoft.AspNetCore.SignalR;

namespace CallingBell.Api.Hubs;

/// <summary>
/// Pushes the live platform figures (listed businesses, published reviews, completed bookings…) to pages watching them on
/// <see cref="PresenceHub"/>. A burst of changes is coalesced into one database read and one push about a second later; a change made
/// while that push is being computed schedules another, so the last figures sent are always current.
/// </summary>
public sealed class PlatformStatsBroadcaster(IServiceScopeFactory scopes, IHubContext<PresenceHub> hub, ILogger<PlatformStatsBroadcaster> log)
    : IPlatformStatsSignal
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);
    private int pending;

    public void Changed()
    {
        if (Interlocked.Exchange(ref pending, 1) == 1) return;
        _ = Task.Run(BroadcastAsync);
    }

    private async Task BroadcastAsync()
    {
        try
        {
            await Task.Delay(Delay);
            Volatile.Write(ref pending, 0);
            using var scope = scopes.CreateScope();
            var stats = await PlatformStats.ComputeAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), CancellationToken.None);
            await hub.Clients.Group(PresenceHub.StatsGroup).SendAsync(PresenceHub.PlatformStatsChanged, stats);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref pending, 0);
            log.LogWarning(ex, "Could not push the platform figures");
        }
    }
}
