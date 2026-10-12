using System.Security.Cryptography;
using System.Text;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Notifications;

// ===================== Browser registration for web push =====================

/// <summary>What a device's token is, which decides the provider that sends to it.</summary>
public static class NotificationDeviceTypes
{
    /// <summary>A Firebase Cloud Messaging registration token (sent through Firebase).</summary>
    public const string Web = "WEB";
    /// <summary>A standard Web Push subscription (sent straight to the browser's push service, signed with this site's VAPID key).</summary>
    public const string WebPush = "WEBPUSH";
}

/// <param name="Token">A Firebase registration token (WEB), or a Web Push subscription as {"endpoint","p256dh","auth"} JSON (WEBPUSH).</param>
/// <param name="DeviceType">"WEB" (default) or "WEBPUSH".</param>
public sealed record RegisterNotificationDeviceCommand(string Token, string? DeviceType, string? Browser, string? Platform) : IRequest<Unit>;

public sealed class RegisterNotificationDeviceValidator : AbstractValidator<RegisterNotificationDeviceCommand>
{
    public RegisterNotificationDeviceValidator()
    {
        RuleFor(x => x.DeviceType).Must(t => t is null or NotificationDeviceTypes.Web or NotificationDeviceTypes.WebPush)
            .WithMessage("deviceType must be WEB or WEBPUSH.");
        RuleFor(x => x.Token).NotEmpty().MaximumLength(1024);
        When(x => x.DeviceType == NotificationDeviceTypes.WebPush,
            () => RuleFor(x => x.Token).Must(t => WebPushSubscription.TryParse(t, out _)).WithMessage("Invalid push subscription."))
            .Otherwise(() => RuleFor(x => x.Token).MinimumLength(20).Matches("^[A-Za-z0-9_:\\-]+$").WithMessage("Invalid push token."));
        RuleFor(x => x.Browser).MaximumLength(40);
        RuleFor(x => x.Platform).MaximumLength(40);
    }
}

/// <summary>
/// A browser's standard Web Push subscription (W3C Push API), stored as the device token in the form the browser sends it:
/// {"endpoint":"https://…","p256dh":"…","auth":"…"} (keys base64url). The endpoint must belong to a browser push service, so a forged
/// subscription can't make the server post to any other address.
/// </summary>
public sealed record WebPushSubscription(Uri Endpoint, byte[] P256dh, byte[] Auth)
{
    /// <summary>Chrome/Edge (Chromium on Android, Opera, Brave, Samsung), Firefox, Edge on Windows, Safari.</summary>
    private static readonly string[] PushServices = ["fcm.googleapis.com", "push.services.mozilla.com", "notify.windows.com", "push.apple.com"];

    public static bool TryParse(string? json, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WebPushSubscription? subscription)
    {
        subscription = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1024) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (!Uri.TryCreate(Text(root, "endpoint"), UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
                || !endpoint.IsDefaultPort || !PushServices.Any(s => endpoint.Host == s || endpoint.Host.EndsWith("." + s, StringComparison.Ordinal)))
                return false;
            var p256dh = FromBase64Url(Text(root, "p256dh"));
            var auth = FromBase64Url(Text(root, "auth"));
            // An uncompressed P-256 public key, and the 16-byte authentication secret.
            if (p256dh is not { Length: 65 } || p256dh[0] != 0x04 || auth is not { Length: 16 }) return false;
            subscription = new WebPushSubscription(endpoint, p256dh, auth);
            return true;
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    private static string? Text(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

    private static byte[]? FromBase64Url(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 200) return null;
        var b64 = s.Replace('-', '+').Replace('_', '/').TrimEnd('=');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(b64); } catch (FormatException) { return null; }
    }
}

/// <summary>
/// Saves the browser's token for the signed-in user, or refreshes it: the same token again updates the row (and moves it to this user if
/// someone else had signed in on that browser), so there are never duplicates. Keeps each user's newest devices only.
/// </summary>
public sealed class RegisterNotificationDeviceHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<RegisterNotificationDeviceCommand, Unit>
{
    private const int MaxActiveDevicesPerUser = 10;

    public async Task<Unit> Handle(RegisterNotificationDeviceCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var token = r.Token.Trim();
        var hash = NotificationDeviceTokens.Hash(token);
        var now = DateTimeOffset.UtcNow;
        var devices = uow.Repository<NotificationDevice>();

        var device = await devices.Query().FirstOrDefaultAsync(d => d.TokenHash == hash, ct);
        if (device is null)
        {
            device = new NotificationDevice { Token = token, TokenHash = hash, CreatedAt = now };
            devices.Add(device);
        }
        device.UserId = userId;
        device.DeviceType = r.DeviceType ?? "WEB";
        device.Browser = Clean(r.Browser);
        device.Platform = Clean(r.Platform);
        device.IsActive = true;
        device.DeactivatedReason = null;
        device.FailureCount = 0;
        device.UpdatedAt = now;
        await uow.SaveChangesAsync(ct);

        // Old browsers the user no longer uses drop off once they have more than a handful.
        var stale = await devices.Query().Where(d => d.UserId == userId && d.IsActive)
            .OrderByDescending(d => d.UpdatedAt).Skip(MaxActiveDevicesPerUser).ToListAsync(ct);
        if (stale.Count > 0)
        {
            stale.ForEach(d => { d.IsActive = false; d.DeactivatedReason = "Replaced"; d.UpdatedAt = now; });
            await uow.SaveChangesAsync(ct);
        }
        return Unit.Value;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 40)];
}

/// <summary>Stops push notifications to this browser (on sign-out, or when the user turns them off).</summary>
public sealed record UnregisterNotificationDeviceCommand(string Token) : IRequest<Unit>;

public sealed class UnregisterNotificationDeviceHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<UnregisterNotificationDeviceCommand, Unit>
{
    public async Task<Unit> Handle(UnregisterNotificationDeviceCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        if (string.IsNullOrWhiteSpace(r.Token) || r.Token.Length > 1024) return Unit.Value;
        var hash = NotificationDeviceTokens.Hash(r.Token.Trim());
        var device = await uow.Repository<NotificationDevice>().Query().FirstOrDefaultAsync(d => d.TokenHash == hash && d.UserId == userId, ct);
        if (device is { IsActive: true })
        {
            device.IsActive = false;
            device.DeactivatedReason = "SignedOut";
            device.UpdatedAt = DateTimeOffset.UtcNow;
            await uow.SaveChangesAsync(ct);
        }
        return Unit.Value;
    }
}

public static class NotificationDeviceTokens
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

// ===================== Web push settings for the browser =====================

/// <summary>
/// What the browser needs to subscribe to push (identifiers, not secrets). <paramref name="Provider"/> "vapid": standard Web Push with
/// this site's VAPID public key, no Firebase; "fcm": through Firebase, with its public web app settings. Null when web push isn't
/// configured, so the app doesn't offer it.
/// </summary>
public sealed record WebPushClientConfigDto(string Provider, string VapidKey, string? ApiKey = null, string? AuthDomain = null, string? ProjectId = null,
    string? MessagingSenderId = null, string? AppId = null);

public interface IWebPushClientConfig
{
    WebPushClientConfigDto? Get();
}

public sealed record GetWebPushConfigQuery : IRequest<WebPushClientConfigDto?>;

public sealed class GetWebPushConfigHandler(IWebPushClientConfig config) : IRequestHandler<GetWebPushConfigQuery, WebPushClientConfigDto?>
{
    public Task<WebPushClientConfigDto?> Handle(GetWebPushConfigQuery request, CancellationToken ct) => Task.FromResult(config.Get());
}

// ===================== Browser acknowledgement of a push =====================

/// <summary>Signs a delivery id for the browser's acknowledgement, so the anonymous endpoint only accepts genuine pushes.</summary>
public interface IDeliveryAckSigner
{
    string Sign(Guid deliveryId);
    bool Verify(Guid deliveryId, string? signature);
}

/// <param name="Event">"received" (the browser got it) or "clicked" (the user opened it).</param>
public sealed record AcknowledgePushCommand(Guid DeliveryId, string Signature, string Event) : IRequest<bool>;

/// <summary>
/// The service worker reports a push it received (Delivered) or one the user clicked (Read). Firebase has no delivery receipts for web
/// push, so this is the only way to know it arrived.
/// </summary>
public sealed class AcknowledgePushHandler(IUnitOfWork uow, IDeliveryAckSigner signer, DeliveryStatusService statuses)
    : IRequestHandler<AcknowledgePushCommand, bool>
{
    public async Task<bool> Handle(AcknowledgePushCommand r, CancellationToken ct)
    {
        if (r.Event is not ("received" or "clicked") || !signer.Verify(r.DeliveryId, r.Signature)) return false;
        var delivery = await uow.Repository<NotificationDelivery>().Query()
            .FirstOrDefaultAsync(d => d.Id == r.DeliveryId && d.Channel == NotificationChannels.WebPush, ct);
        if (delivery is null) return false;
        var status = r.Event == "clicked" ? DeliveryStatuses.Read : DeliveryStatuses.Delivered;
        return await statuses.ApplyAsync(delivery, new DeliveryStatusReport(delivery.Provider, delivery.ProviderMessageId ?? "", status), ct);
    }
}
