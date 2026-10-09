using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Notifications;

public sealed record NotificationDto(Guid Id, string Title, string Message, string NotificationType, string? LinkUrl, bool IsRead, DateTimeOffset CreatedOn);

public sealed record NotificationListDto(int UnreadCount, IReadOnlyList<NotificationDto> Items);

public sealed record GetMyNotificationsQuery(int Take = 20) : IRequest<NotificationListDto>;

public sealed class GetMyNotificationsHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetMyNotificationsQuery, NotificationListDto>
{
    public async Task<NotificationListDto> Handle(GetMyNotificationsQuery request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var query = uow.Repository<Notification>().QueryNoTracking().Where(n => n.UserId == userId);

        var unread = await query.CountAsync(n => !n.IsRead, ct);
        var items = await query
            .OrderByDescending(n => n.CreatedOn)
            .Take(Math.Clamp(request.Take, 1, 50))
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.NotificationType, n.LinkUrl, n.IsRead, n.CreatedOn))
            .ToListAsync(ct);

        return new NotificationListDto(unread, items);
    }
}

public sealed record MarkNotificationsReadCommand : IRequest;

public sealed class MarkNotificationsReadHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<MarkNotificationsReadCommand>
{
    public async Task Handle(MarkNotificationsReadCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var unread = await uow.Repository<Notification>().Query().Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        unread.ForEach(n => n.IsRead = true);
        await uow.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Delivery beyond the app for a notification: the route whose channels send it (web push, WhatsApp, …), what it is about, and a key
/// that makes the event notify only once.
/// </summary>
public sealed record NotificationRouting(string RouteCode, string ReferenceType, Guid ReferenceId, string IdempotencyKey);

/// <summary>Persists a notification and pushes it in real time. Used by lead and booking handlers.</summary>
public static class NotificationPublisher
{
    /// <param name="routing">
    /// Also deliver it outside the app through this route. The channels are worked through by a background dispatcher, so the caller
    /// (e.g. creating a lead) doesn't wait for WhatsApp or SMS providers. A notification with the same idempotency key is never created twice.
    /// </param>
    public static async Task PublishAsync(IUnitOfWork uow, IRealtimeNotifier notifier, string userId, string title, string message,
        string type, string? link, CancellationToken ct, NotificationRouting? routing = null, INotificationDispatchSignal? signal = null)
    {
        if (routing is not null && await uow.Repository<Notification>().QueryNoTracking().AnyAsync(n => n.IdempotencyKey == routing.IdempotencyKey, ct))
            return;

        var now = DateTimeOffset.UtcNow;
        var notification = new Notification
        {
            UserId = userId, Title = title, Message = message, NotificationType = type, LinkUrl = link, CreatedOn = now
        };
        if (routing is not null)
        {
            notification.RouteCode = routing.RouteCode;
            notification.ReferenceType = routing.ReferenceType;
            notification.ReferenceId = routing.ReferenceId;
            notification.IdempotencyKey = routing.IdempotencyKey;
            notification.DispatchStatus = DispatchStatuses.Pending;
            notification.NextDispatchAt = now;
        }
        uow.Repository<Notification>().Add(notification);
        await uow.SaveChangesAsync(ct);
        if (routing is not null) signal?.Notify();
        await notifier.NotifyUserAsync(userId,
            new NotificationDto(notification.Id, title, message, type, link, false, notification.CreatedOn), ct);
    }
}
