using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CallingBell.Application.Features.Notifications.Delivery;

/// <summary>A provider's (or the browser's) report about a message it accepted earlier.</summary>
/// <param name="Status">One of <see cref="DeliveryStatuses"/>: Sent, Delivered, Read or Failed.</param>
public sealed record DeliveryStatusReport(string Provider, string ProviderMessageId, string Status, DateTimeOffset? At = null,
    string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>
/// Applies delivery reports from provider webhooks. Status only moves forward (Accepted → Sent → Delivered → Read); a report of failure
/// after the provider had accepted the message (e.g. the number isn't on WhatsApp) continues the notification's route from the next
/// channel, so the business still hears about the lead. A notification that was merely not opened or not read is not a failure.
/// </summary>
public sealed class DeliveryStatusService(IUnitOfWork uow, INotificationDispatchSignal dispatch, IOtpFallback otpFallback,
    ILogger<DeliveryStatusService> logger)
{
    private static readonly string[] Order = [DeliveryStatuses.Queued, DeliveryStatuses.Accepted, DeliveryStatuses.Sent, DeliveryStatuses.Delivered, DeliveryStatuses.Read];

    public async Task<bool> ApplyAsync(DeliveryStatusReport report, CancellationToken ct)
    {
        var delivery = await uow.Repository<NotificationDelivery>().Query()
            .FirstOrDefaultAsync(d => d.Provider == report.Provider && d.ProviderMessageId == report.ProviderMessageId, ct);
        if (delivery is null) return false;
        return await ApplyAsync(delivery, report, ct);
    }

    /// <summary>For a delivery already looked up (web push acknowledgements identify the delivery by id).</summary>
    public async Task<bool> ApplyAsync(NotificationDelivery delivery, DeliveryStatusReport report, CancellationToken ct)
    {
        var at = report.At ?? DateTimeOffset.UtcNow;
        var current = Array.IndexOf(Order, delivery.Status);
        var next = Array.IndexOf(Order, report.Status);

        if (report.Status == DeliveryStatuses.Failed)
        {
            // Only a message still on its way can fail; a delivered or read one stays so.
            if (delivery.Status is not (DeliveryStatuses.Accepted or DeliveryStatuses.Sent)) return false;
            delivery.Status = DeliveryStatuses.Failed;
            delivery.ErrorCode = report.ErrorCode is null ? null : Cut(report.ErrorCode, 60);
            delivery.ErrorMessage = report.ErrorMessage is null ? null : Cut(report.ErrorMessage, 500);
            delivery.UpdatedAt = DateTimeOffset.UtcNow;
            await ContinueRouteAsync(delivery, ct);
            await uow.SaveChangesAsync(ct);
            return true;
        }

        // A failed (or never sent) message stays so; a late report can still upgrade one that had expired for lack of reports.
        if (delivery.Status is DeliveryStatuses.Failed or DeliveryStatuses.Unavailable) return false;
        if (next < 0 || current >= 0 && next <= current) return false;
        delivery.Status = report.Status;
        if (report.Status == DeliveryStatuses.Sent) delivery.SentAt ??= at;
        if (report.Status is DeliveryStatuses.Delivered or DeliveryStatuses.Read) delivery.DeliveredAt ??= at;
        if (report.Status == DeliveryStatuses.Read) delivery.ReadAt ??= at;
        delivery.UpdatedAt = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task ContinueRouteAsync(NotificationDelivery delivery, CancellationToken ct)
    {
        if (delivery.OtpCodeId is { } otpId)
        {
            // The code isn't stored, so only the instance that sent it can resend it by SMS (it keeps it in memory while it is valid).
            await otpFallback.TrySendBySmsAsync(otpId, ct);
            return;
        }
        if (delivery.NotificationId is not { } id) return;
        var n = await uow.Repository<Notification>().Query().FirstOrDefaultAsync(x => x.Id == id, ct);
        // Only when this attempt was the one that ended the route; a later channel may already have taken over.
        if (n is null || n.DispatchStatus != DispatchStatuses.Completed || n.DispatchStep != delivery.RouteStep) return;
        n.DispatchStatus = DispatchStatuses.Pending;
        n.DispatchStep = delivery.RouteStep + 1;
        n.NextDispatchAt = DateTimeOffset.UtcNow;
        n.DispatchLockedUntil = null;
        logger.LogInformation("Notification {NotificationId}: {Channel} reported failure ({Error}); continuing with the next channel",
            n.Id, delivery.Channel, delivery.ErrorCode);
        dispatch.Notify();
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Resends a one-time code by SMS when WhatsApp reports, after accepting it, that it could not be delivered.</summary>
public interface IOtpFallback
{
    Task TrySendBySmsAsync(Guid otpCodeId, CancellationToken ct);
}
