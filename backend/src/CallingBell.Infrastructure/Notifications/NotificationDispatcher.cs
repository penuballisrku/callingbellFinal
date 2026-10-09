using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Identity;
using CallingBell.Infrastructure.Notifications.Providers;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallingBell.Infrastructure.Notifications;

/// <summary>Wakes the dispatcher when a notification is queued (at most one pending wake-up; extra ones are dropped).</summary>
public sealed class NotificationDispatchSignal : INotificationDispatchSignal
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _wake.Writer.TryWrite(true);

    internal async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await _wake.Reader.ReadAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* poll interval elapsed */ }
    }
}

/// <summary>
/// Sends queued notifications (dbo.Notifications with a route) in the background, so creating a lead or booking never waits for WhatsApp,
/// RCS or SMS providers. Each notification is claimed with a single conditional UPDATE, so several app instances never send the same one;
/// a claim whose instance died is picked up again when its lock expires. Also marks old accepted messages that never got a report as Expired.
/// </summary>
internal sealed class NotificationDispatcher(IServiceScopeFactory scopes, NotificationDispatchSignal signal, IOptions<NotificationOptions> options,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private DispatcherOptions O => options.Value.Dispatcher;
    private DateTimeOffset _nextExpirySweep = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!O.Enabled) { logger.LogInformation("Notification dispatcher is disabled"); return; }
        // Let the app finish starting first.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int handled;
                do handled = await DispatchDueAsync(stoppingToken);
                while (handled >= O.BatchSize && !stoppingToken.IsCancellationRequested);
                if (DateTimeOffset.UtcNow >= _nextExpirySweep) await ExpireStaleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification dispatcher round failed");
            }
            await signal.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, O.PollSeconds)), stoppingToken);
        }
    }

    private async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        List<Guid> claimed;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var due = await db.Notifications.AsNoTracking()
                .Where(n => n.DispatchStatus == DispatchStatuses.Pending && (n.NextDispatchAt == null || n.NextDispatchAt <= now)
                            || n.DispatchStatus == DispatchStatuses.Processing && n.DispatchLockedUntil < now)
                .OrderBy(n => n.NextDispatchAt).Select(n => n.Id).Take(O.BatchSize).ToListAsync(ct);

            claimed = [];
            var lockUntil = now.AddMinutes(O.LockMinutes);
            foreach (var id in due)
            {
                var rows = await db.Notifications
                    .Where(n => n.Id == id && (n.DispatchStatus == DispatchStatuses.Pending && (n.NextDispatchAt == null || n.NextDispatchAt <= now)
                                               || n.DispatchStatus == DispatchStatuses.Processing && n.DispatchLockedUntil < now))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.DispatchStatus, DispatchStatuses.Processing)
                        .SetProperty(n => n.DispatchLockedUntil, lockUntil)
                        .SetProperty(n => n.DispatchAttempts, n => n.DispatchAttempts + 1), ct);
                if (rows == 1) claimed.Add(id);
            }
        }
        if (claimed.Count == 0) return 0;

        await Parallel.ForEachAsync(claimed, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, O.Parallelism), CancellationToken = ct },
            async (id, token) =>
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatchProcessor>().ProcessAsync(id, token);
            });
        return claimed.Count;
    }

    private async Task ExpireStaleAsync(CancellationToken ct)
    {
        _nextExpirySweep = DateTimeOffset.UtcNow.AddHours(1);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = DateTimeOffset.UtcNow.AddHours(-O.ExpireAcceptedAfterHours);
        var expired = await db.NotificationDeliveries
            .Where(d => (d.Status == DeliveryStatuses.Accepted || d.Status == DeliveryStatuses.Sent) && d.Channel != NotificationChannels.WebPush
                        && d.CreatedAt < before)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DeliveryStatuses.Expired).SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (expired > 0) logger.LogInformation("Marked {Count} unconfirmed message deliveries as expired", expired);
    }
}

/// <summary>HMAC over the delivery id, so only the browser that got the push can acknowledge it.</summary>
internal sealed class DeliveryAckSigner : IDeliveryAckSigner
{
    private readonly byte[] _key;

    public DeliveryAckSigner(IOptions<NotificationOptions> options, IOptions<JwtOptions> jwt)
    {
        var secret = options.Value.Fcm.AckSigningKey;
        _key = !string.IsNullOrWhiteSpace(secret)
            ? Encoding.UTF8.GetBytes(secret)
            // A key of its own, derived from the JWT signing key (never the JWT key itself).
            : SHA256.HashData(Encoding.UTF8.GetBytes("callingbell:push-ack:" + jwt.Value.Key));
    }

    public string Sign(Guid deliveryId) => Base64UrlEncoder.Encode(HMACSHA256.HashData(_key, deliveryId.ToByteArray()));

    public bool Verify(Guid deliveryId, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || signature.Length > 100) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Base64UrlEncoder.DecodeBytes(signature), HMACSHA256.HashData(_key, deliveryId.ToByteArray()));
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>The Firebase web app's public settings for the browser; null unless web push is fully configured on the server.</summary>
internal sealed class WebPushClientConfig(IOptions<NotificationOptions> options, GoogleServiceAccountTokens tokens) : IWebPushClientConfig
{
    public WebPushClientConfigDto? Get()
    {
        var o = options.Value.Fcm;
        var w = o.Web;
        if (!o.Enabled || tokens.Account is null || new[] { w.ApiKey, w.ProjectId, w.MessagingSenderId, w.AppId, w.VapidKey }.Any(string.IsNullOrWhiteSpace))
            return null;
        return new WebPushClientConfigDto(w.ApiKey!, w.AuthDomain ?? $"{w.ProjectId}.firebaseapp.com", w.ProjectId!, w.MessagingSenderId!, w.AppId!, w.VapidKey!);
    }
}

/// <summary>Signed, expiring chat attachment links (HMAC of message id and expiry with a key derived from the JWT key).</summary>
internal sealed class ChatAttachmentSigner(IOptions<JwtOptions> jwt) : CallingBell.Application.Features.Chat.IChatAttachmentSigner
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes("callingbell:chat-attachment:" + jwt.Value.Key));

    public string Url(Guid messageId)
    {
        // Rounded to the hour, so a page of messages keeps the same links (and the browser cache works).
        var expires = (DateTimeOffset.UtcNow + Lifetime).ToUnixTimeSeconds() / 3600 * 3600 + 3600;
        return $"/api/chat/attachments/{messageId}?exp={expires}&sig={Sign(messageId, expires)}";
    }

    public bool Verify(Guid messageId, long expires, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        try { return CryptographicOperations.FixedTimeEquals(Base64UrlEncoder.DecodeBytes(signature), Raw(messageId, expires)); }
        catch (FormatException) { return false; }
    }

    private string Sign(Guid messageId, long expires) => Base64UrlEncoder.Encode(Raw(messageId, expires));
    private byte[] Raw(Guid messageId, long expires) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{messageId:N}:{expires}"));
}
