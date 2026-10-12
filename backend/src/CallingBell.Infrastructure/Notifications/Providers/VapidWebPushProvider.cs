using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallingBell.Infrastructure.Notifications.Providers;

/// <summary>
/// Standard Web Push (no Firebase) to every browser the user subscribed: the message is encrypted for that browser (RFC 8291) and posted
/// to its push service with this site's VAPID signature (RFC 8292). The service worker (/firebase-messaging-sw.js) shows it, exactly as
/// for Firebase pushes. Accepted when a push service takes it for at least one browser; unavailable when the user has none.
/// Subscriptions the push service reports as gone (404/410) are turned off.
/// </summary>
internal sealed class VapidWebPushProvider(ApplicationDbContext db, IHttpClientFactory http, VapidSigner vapid, IDeliveryAckSigner signer,
    IOptions<NotificationOptions> options, ILogger<VapidWebPushProvider> logger) : INotificationProvider
{
    public const string HttpClientName = "notify-webpush";
    private WebPushOptions O => options.Value.WebPush;

    public string ProviderName => "WebPush";
    public string Channel => NotificationChannels.WebPush;
    public bool IsConfigured => vapid.IsConfigured;

    public async Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        if (message.Recipient.UserId is not { } userId) return NotificationResult.Unavailable("NO_USER", "Web push needs a signed-in user.");
        var devices = await db.NotificationDevices.AsNoTracking()
            .Where(d => d.UserId == userId && d.IsActive && d.DeviceType == NotificationDeviceTypes.WebPush)
            .OrderByDescending(d => d.UpdatedAt).Select(d => new { d.Id, d.Token }).Take(10).ToListAsync(ct);
        if (devices.Count == 0) return NotificationResult.Unavailable("NO_DEVICE", "No browser subscribed to push notifications.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(WebPushPayload.Build(message, O.IconUrl, signer));
        var client = http.CreateClient(HttpClientName);
        string? firstId = null;
        NotificationResult? lastFailure = null;
        var accepted = 0;
        foreach (var device in devices)
        {
            if (!WebPushSubscription.TryParse(device.Token, out var subscription))
            {
                await DeactivateAsync(device.Id, "INVALID_SUBSCRIPTION", ct);
                continue;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint)
            {
                Content = new ByteArrayContent(WebPushEncryption.Encrypt(payload, subscription.P256dh, subscription.Auth)),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("TTL", O.TimeToLiveSeconds.ToString());
            request.Headers.TryAddWithoutValidation("Urgency", "high");
            request.Headers.TryAddWithoutValidation("Authorization", vapid.AuthorizationFor(subscription.Endpoint));

            HttpResponseMessage response;
            try { response = await client.SendAsync(request, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                lastFailure = NotificationResult.Failed("NETWORK", $"The push service couldn't be reached ({ex.GetType().Name}).", retryable: accepted == 0);
                continue;
            }
            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    firstId ??= response.Headers.Location?.ToString() is { Length: > 0 and <= 200 } location ? location : device.Id.ToString("N");
                    accepted++;
                    await db.NotificationDevices.Where(d => d.Id == device.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastUsedAt, DateTimeOffset.UtcNow).SetProperty(d => d.FailureCount, 0), ct);
                    continue;
                }

                var status = (int)response.StatusCode;
                var detail = await ReadDetailAsync(response, ct);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    await DeactivateAsync(device.Id, $"HTTP_{status}", ct);
                    lastFailure = NotificationResult.Failed($"HTTP_{status}", "The browser's push subscription has expired.");
                    continue;
                }
                // The VAPID signature was refused: every browser would fail the same way.
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    logger.LogError("Push service {Host} refused the VAPID signature: {Status} {Detail}", subscription.Endpoint.Host, status, detail);
                    return NotificationResult.Failed($"HTTP_{status}", "The push service refused this site's VAPID key.");
                }
                var retryable = status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                await db.NotificationDevices.Where(d => d.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.FailureCount, d => d.FailureCount + 1), ct);
                logger.LogWarning("Push service {Host} returned {Status}: {Detail}", subscription.Endpoint.Host, status, detail);
                lastFailure = NotificationResult.Failed($"HTTP_{status}", string.IsNullOrEmpty(detail) ? "The push service rejected the message." : detail,
                    retryable && accepted == 0);
            }
        }

        if (accepted > 0) return NotificationResult.Accepted(firstId, $"{accepted} browser{(accepted == 1 ? "" : "s")}");
        return lastFailure ?? NotificationResult.Failed("WEBPUSH_FAILED", "No browser accepted the push.");
    }

    private Task<int> DeactivateAsync(Guid id, string reason, CancellationToken ct) =>
        db.NotificationDevices.Where(d => d.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.IsActive, false).SetProperty(d => d.DeactivatedReason, reason).SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), ct);

    private static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        return text.Length > 300 ? text[..300] : text;
    }
}

/// <summary>
/// The push's content, read by the service worker: everything as strings, the same for Firebase and standard Web Push. The tag groups
/// repeated pushes about the same lead or booking into one notification.
/// </summary>
internal static class WebPushPayload
{
    public static Dictionary<string, string> Build(NotificationMessage m, string iconUrl, IDeliveryAckSigner signer) =>
        new(m.Data)
        {
            ["title"] = m.Title,
            ["body"] = m.Body,
            ["url"] = m.Url ?? "/",
            ["icon"] = iconUrl,
            ["tag"] = m.Data.TryGetValue("leadId", out var lead) ? $"lead-{lead}" : m.Data.TryGetValue("bookingId", out var booking) ? $"booking-{booking}" : m.DeliveryId.ToString("N"),
            ["deliveryId"] = m.DeliveryId.ToString(),
            ["ack"] = signer.Sign(m.DeliveryId),
        };
}

/// <summary>
/// This site's VAPID identity (RFC 8292): signs a short-lived ES256 token per push service, which tells the service who is sending and
/// that it holds the private key matching the public key the browser subscribed with. Tokens are reused until shortly before they expire.
/// </summary>
public sealed class VapidSigner
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly ECDsa? _key;
    private readonly string? _publicKey;
    private readonly string _subject;
    private readonly ConcurrentDictionary<string, (string Header, DateTimeOffset RenewAfter)> _tokens = new(StringComparer.Ordinal);

    public VapidSigner(IOptions<NotificationOptions> options, ILogger<VapidSigner> logger)
    {
        var o = options.Value.WebPush;
        _subject = o.Subject;
        if (!o.Enabled) return;
        if (string.IsNullOrWhiteSpace(o.PublicKey) || string.IsNullOrWhiteSpace(o.PrivateKey))
        {
            logger.LogWarning("Web push (VAPID) is enabled but its key pair is not configured");
            return;
        }
        try
        {
            var publicKey = Base64UrlEncoder.DecodeBytes(o.PublicKey.Trim());
            var privateKey = Base64UrlEncoder.DecodeBytes(o.PrivateKey.Trim());
            if (publicKey is not { Length: 65 } || publicKey[0] != 0x04 || privateKey is not { Length: 32 }) throw new FormatException();
            var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256, D = privateKey, Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..] },
            });
            // The pair must match, or every push service would refuse the signatures.
            using var check = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..] } });
            var probe = "callinbell"u8.ToArray();
            if (!check.VerifyData(probe, key.SignData(probe, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256)) throw new CryptographicException();
            _key = key;
            _publicKey = o.PublicKey.Trim();
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            // Never log the key itself.
            logger.LogError("The VAPID key pair is invalid ({Error}); web push is off", ex.GetType().Name);
        }
    }

    public bool IsConfigured => _key is not null;

    /// <summary>The public key browsers subscribe with (base64url).</summary>
    public string? PublicKey => _publicKey;

    /// <summary>The Authorization header for a push to this endpoint: "vapid t=&lt;JWT&gt;, k=&lt;public key&gt;".</summary>
    public string AuthorizationFor(Uri endpoint)
    {
        var key = _key ?? throw new InvalidOperationException("VAPID is not configured.");
        var audience = endpoint.GetLeftPart(UriPartial.Authority);
        var now = DateTimeOffset.UtcNow;
        if (_tokens.TryGetValue(audience, out var cached) && now < cached.RenewAfter) return cached.Header;

        var header = Base64UrlEncoder.Encode("""{"typ":"JWT","alg":"ES256"}""");
        var claims = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["aud"] = audience, ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds(), ["sub"] = _subject,
        }));
        // ES256 signatures are the raw r||s pair (IEEE P1363), which is .NET's default.
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256);
        var value = $"vapid t={header}.{claims}.{Base64UrlEncoder.Encode(signature)}, k={_publicKey}";
        _tokens[audience] = (value, now.Add(Lifetime - TimeSpan.FromHours(1)));
        return value;
    }
}

/// <summary>
/// Message encryption for Web Push (RFC 8291) in the aes128gcm content coding (RFC 8188), as a single record. Only the browser that
/// subscribed (holding the private key behind p256dh and the auth secret) can read the message; the push service sees ciphertext.
/// </summary>
public static class WebPushEncryption
{
    private const int RecordSize = 4096;
    /// <summary>Push services accept at most 4096 bytes of body: the 86-byte header, the 16-byte tag and the delimiter leave this much text.</summary>
    public const int MaxPlaintextLength = RecordSize - 86 - 16 - 1;

    public static byte[] Encrypt(byte[] plaintext, byte[] userAgentPublicKey, byte[] authSecret)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, userAgentPublicKey, authSecret, ephemeral, RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>With a given sender key and salt: for the RFC's test vector. Real pushes use a fresh key and salt every time.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] userAgentPublicKey, byte[] authSecret, ECDiffieHellman senderKey, byte[] salt)
    {
        if (plaintext.Length > MaxPlaintextLength) throw new ArgumentException($"A push message can hold at most {MaxPlaintextLength} bytes.", nameof(plaintext));
        if (userAgentPublicKey is not { Length: 65 } || userAgentPublicKey[0] != 0x04) throw new ArgumentException("Invalid p256dh key.", nameof(userAgentPublicKey));
        if (authSecret is not { Length: 16 }) throw new ArgumentException("Invalid auth secret.", nameof(authSecret));

        var q = senderKey.ExportParameters(false).Q;
        var senderPublicKey = Concat([0x04], q.X!, q.Y!);
        using var userAgent = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = userAgentPublicKey[1..33], Y = userAgentPublicKey[33..] },
        });
        var sharedSecret = senderKey.DeriveRawSecretAgreement(userAgent.PublicKey);

        // IKM = HKDF(auth secret, ECDH secret, "WebPush: info" || 0 || ua_public || as_public); then the content key and nonce.
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, Concat("WebPush: info\0"u8.ToArray(), userAgentPublicKey, senderPublicKey));
        var contentKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        // One record: the text and the last-record delimiter (2), no padding.
        var record = new byte[plaintext.Length + 1];
        plaintext.CopyTo(record, 0);
        record[^1] = 0x02;
        var ciphertext = new byte[record.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(contentKey, tag.Length)) aes.Encrypt(nonce, record, ciphertext, tag);

        // Header: salt (16) || record size (uint32) || key id length (1) || key id = the sender's public key (65).
        var recordSize = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(recordSize, RecordSize);
        return Concat(salt, recordSize, [(byte)senderPublicKey.Length], senderPublicKey, ciphertext, tag);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var p in parts) { p.CopyTo(result, offset); offset += p.Length; }
        return result;
    }
}
