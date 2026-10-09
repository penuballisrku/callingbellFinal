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

/// <param name="Token">Firebase Cloud Messaging registration token from the browser.</param>
/// <param name="DeviceType">"WEB".</param>
public sealed record RegisterNotificationDeviceCommand(string Token, string? DeviceType, string? Browser, string? Platform) : IRequest<Unit>;

public sealed class RegisterNotificationDeviceValidator : AbstractValidator<RegisterNotificationDeviceCommand>
{
    public RegisterNotificationDeviceValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MinimumLength(20).MaximumLength(1024)
            .Matches("^[A-Za-z0-9_:\\-]+$").WithMessage("Invalid push token.");
        RuleFor(x => x.DeviceType).Must(t => t is null or "WEB").WithMessage("deviceType must be WEB.");
        RuleFor(x => x.Browser).MaximumLength(40);
        RuleFor(x => x.Platform).MaximumLength(40);
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
/// What the browser needs to get a push token: Firebase's public web app settings and VAPID public key (identifiers, not secrets).
/// Null when web push isn't configured, so the app doesn't offer it.
/// </summary>
public sealed record WebPushClientConfigDto(string ApiKey, string AuthDomain, string ProjectId, string MessagingSenderId, string AppId, string VapidKey);

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
