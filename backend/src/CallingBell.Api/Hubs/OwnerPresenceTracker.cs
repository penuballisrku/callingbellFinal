using CallingBell.Application.Features.Presence;
using MediatR;

namespace CallingBell.Api.Hubs;

/// <summary>
/// Which business owners have CallInBell open, from their NotificationHub connections (one per tab). The first connection after being
/// away puts their businesses Online; when the last one closes and stays closed for the grace period (Presence:OfflineAfterSeconds,
/// default 120) they go Offline. A reconnect within the grace period (a reload, a network blip) changes nothing.
/// </summary>
/// <remarks>
/// Held in memory for this API instance (use a shared store such as Redis before running several instances). After a restart, owners
/// who were connected keep their last status until they next open and leave the site; owners who never connect are never changed.
/// </remarks>
public sealed class OwnerPresenceTracker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OwnerPresenceTracker> logger)
{
    private sealed class Session
    {
        public int Connections;
        public CancellationTokenSource? Leaving;
    }

    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private TimeSpan Grace => TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Presence:OfflineAfterSeconds", 120)));

    public void Connected(string userId)
    {
        bool arrived;
        lock (_lock)
        {
            arrived = !_sessions.TryGetValue(userId, out var session);
            if (session is null) _sessions[userId] = session = new Session();
            session.Connections++;
            // Back within the grace period: still the same visit.
            session.Leaving?.Cancel();
            session.Leaving = null;
        }
        if (arrived) _ = SetAsync(userId, online: true);
    }

    public void Disconnected(string userId)
    {
        CancellationTokenSource? leaving = null;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(userId, out var session)) return;
            session.Connections = Math.Max(0, session.Connections - 1);
            if (session.Connections == 0) session.Leaving = leaving = new CancellationTokenSource();
        }
        if (leaving is not null) _ = LeaveAfterGraceAsync(userId, leaving);
    }

    private async Task LeaveAfterGraceAsync(string userId, CancellationTokenSource leaving)
    {
        try { await Task.Delay(Grace, leaving.Token); }
        catch (OperationCanceledException) { return; }
        finally { if (leaving.IsCancellationRequested) leaving.Dispose(); }

        lock (_lock)
        {
            if (!_sessions.TryGetValue(userId, out var session) || session.Leaving != leaving || session.Connections > 0) return;
            _sessions.Remove(userId);
        }
        leaving.Dispose();
        await SetAsync(userId, online: false);
    }

    private async Task SetAsync(string userId, bool online)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var count = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SetOwnerPresenceRequest(userId, online));
            if (count > 0) logger.LogInformation("Owner {UserId} {State}: {Count} business(es) updated", userId, online ? "arrived" : "left", count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't update presence for owner {UserId}", userId);
        }
    }
}
