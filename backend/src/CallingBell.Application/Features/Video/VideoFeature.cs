using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Chat;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallingBell.Application.Features.Video;

/// <summary>
/// Video calls are browser to browser (WebRTC). STUN finds each side's public address; a TURN server relays the media when a direct
/// connection isn't possible (strict corporate or mobile networks). Configure your own TURN server for production.
/// </summary>
public sealed class VideoOptions
{
    public const string Section = "Video";

    public List<IceServerOptions> IceServers { get; set; } = [new() { Urls = ["stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302"] }];
    /// <summary>A booking's room opens this long before the start…</summary>
    public int OpenMinutesBefore { get; set; } = 15;
    /// <summary>…and closes this long after the end.</summary>
    public int CloseMinutesAfter { get; set; } = 60;
    /// <summary>How long a call started from chat stays open.</summary>
    public int InstantRoomMinutes { get; set; } = 120;
}

public sealed class IceServerOptions
{
    public List<string> Urls { get; set; } = [];
    public string? Username { get; set; }
    /// <summary>Secret (TURN credential): configure through environment variables or Key Vault.</summary>
    public string? Credential { get; set; }
}

public sealed record IceServerDto(IReadOnlyList<string> Urls, string? Username, string? Credential);

/// <param name="MyRole">"Customer" or "Business".</param>
/// <param name="CanJoinNow">Within the room's opening hours and not ended.</param>
public sealed record VideoRoomDto(Guid Id, string Status, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, string MyRole, Guid BusinessId,
    string BusinessName, string? BusinessLogoUrl, string CustomerName, string? ServiceName, DateTimeOffset? ScheduledStart, string? StaffName,
    bool CanJoinNow, Guid? ConversationId, IReadOnlyList<IceServerDto> IceServers);

internal static class VideoRules
{
    /// <summary>Online services, and consultations at businesses that offer video, happen over video.</summary>
    public static bool IsVideo(string serviceType, bool offersVideo) =>
        serviceType == "Online" || serviceType == "Consultation" && offersVideo;
}

internal static class VideoAccess
{
    /// <summary>The room and the viewer's side: the customer, or the business's owner. Anyone else is refused.</summary>
    public static async Task<(VideoRoom Room, string Role, string OwnerUserId)> GetAsync(IUnitOfWork uow, ICurrentUser user, Guid roomId, CancellationToken ct, bool track = false)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var query = track ? uow.Repository<VideoRoom>().Query() : uow.Repository<VideoRoom>().QueryNoTracking();
        var room = await query.Include(r => r.Business).FirstOrDefaultAsync(r => r.Id == roomId, ct) ?? throw new NotFoundException("Video call", roomId);
        var role = room.CustomerUserId == userId ? ChatRoles.Customer : room.Business.OwnerUserId == userId ? ChatRoles.Business : null;
        return role is null ? throw new ForbiddenAccessException() : (room, role, room.Business.OwnerUserId);
    }

    public static bool CanJoin(VideoRoom room, DateTimeOffset now) =>
        room.Status != "Ended" && (room.OpensAt is null || now >= room.OpensAt) && (room.ClosesAt is null || now <= room.ClosesAt);
}

// ===================== Rooms =====================

public sealed record GetVideoRoomQuery(Guid RoomId) : IRequest<VideoRoomDto>;

public sealed class GetVideoRoomHandler(IUnitOfWork uow, ICurrentUser user, IOptions<VideoOptions> options) : IRequestHandler<GetVideoRoomQuery, VideoRoomDto>
{
    public async Task<VideoRoomDto> Handle(GetVideoRoomQuery r, CancellationToken ct)
    {
        var (room, role, _) = await VideoAccess.GetAsync(uow, user, r.RoomId, ct);
        var extra = await uow.Repository<VideoRoom>().QueryNoTracking().Where(x => x.Id == room.Id).Select(x => new
        {
            CustomerName = uow.Repository<ApplicationUser>().QueryNoTracking().Where(u => u.Id == x.CustomerUserId).Select(u => u.DisplayName).FirstOrDefault(),
            ServiceName = x.Booking != null ? x.Booking.Service.Name : null,
            Start = x.Booking != null ? (DateTimeOffset?)x.Booking.ScheduledStart : null,
            StaffName = x.Booking != null && x.Booking.Staff != null ? x.Booking.Staff.FullName : null,
        }).FirstAsync(ct);
        // TURN credentials only go to the two people in the call.
        var ice = options.Value.IceServers.Where(s => s.Urls.Count > 0).Select(s => new IceServerDto(s.Urls, s.Username, s.Credential)).ToList();
        return new VideoRoomDto(room.Id, room.Status, room.OpensAt, room.ClosesAt, role, room.BusinessId, room.Business.Name, room.Business.LogoUrl,
            extra.CustomerName ?? "Customer", extra.ServiceName, extra.Start, extra.StaffName, VideoAccess.CanJoin(room, DateTimeOffset.UtcNow),
            room.ConversationId, ice);
    }
}

/// <summary>
/// The room for a booking held over video, created when the business confirms it (or on first request): it opens shortly before the
/// start. The customer is told where to join. Returns null for bookings that aren't video consultations.
/// </summary>
public sealed class VideoRoomService(IUnitOfWork uow, IRealtimeNotifier notifier, INotificationDispatchSignal dispatch, IOptions<VideoOptions> options)
{
    public async Task<Guid?> EnsureForBookingAsync(Guid bookingId, CancellationToken ct, bool notify = true)
    {
        var b = await uow.Repository<Booking>().QueryNoTracking().Where(x => x.Id == bookingId)
            .Select(x => new { x.Id, x.BusinessId, x.CustomerUserId, x.StaffId, x.ScheduledStart, x.ScheduledEnd, x.Status, ServiceType = x.Service.Type,
                ServiceName = x.Service.Name, BusinessName = x.Business.Name, x.Business.OffersVideoConsultation })
            .FirstOrDefaultAsync(ct);
        if (b is null || !VideoRules.IsVideo(b.ServiceType, b.OffersVideoConsultation)) return null;

        var existing = await uow.Repository<VideoRoom>().QueryNoTracking().Where(r => r.BookingId == bookingId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var o = options.Value;
        var room = new VideoRoom
        {
            BusinessId = b.BusinessId, CustomerUserId = b.CustomerUserId, BookingId = b.Id, StaffId = b.StaffId,
            OpensAt = b.ScheduledStart.AddMinutes(-o.OpenMinutesBefore), ClosesAt = b.ScheduledEnd.AddMinutes(o.CloseMinutesAfter),
        };
        uow.Repository<VideoRoom>().Add(room);
        await uow.SaveChangesAsync(ct);

        if (notify)
        {
            var when = b.ScheduledStart.ToOffset(IndianTime.Offset).ToString("ddd d MMM, h:mm tt");
            await NotificationPublisher.PublishAsync(uow, notifier, b.CustomerUserId, "Your video consultation is ready",
                $"{b.ServiceName} with {b.BusinessName} on {when}. Join from your bookings, or open the link {o.OpenMinutesBefore} minutes before the start.",
                "Booking", $"/video/{room.Id}", ct,
                new NotificationRouting(NotificationRoutes.Video, "VideoRoom", room.Id, $"{NotificationRoutes.Video}:{room.Id}:{b.CustomerUserId}"), dispatch);
        }
        return room.Id;
    }
}

/// <summary>The room for one of my bookings (customer or owner), creating it if it's a video booking that doesn't have one yet.</summary>
public sealed record GetBookingVideoRoomQuery(Guid BookingId) : IRequest<Guid>;

public sealed class GetBookingVideoRoomHandler(IUnitOfWork uow, ICurrentUser user, VideoRoomService rooms) : IRequestHandler<GetBookingVideoRoomQuery, Guid>
{
    public async Task<Guid> Handle(GetBookingVideoRoomQuery r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var b = await uow.Repository<Booking>().QueryNoTracking().Where(x => x.Id == r.BookingId)
            .Select(x => new { x.CustomerUserId, x.Business.OwnerUserId, x.Status }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Booking", r.BookingId);
        if (b.CustomerUserId != userId && b.OwnerUserId != userId) throw new ForbiddenAccessException();
        if (b.Status != BookingStatuses.Confirmed) throw new BadRequestException("The video call opens once the business confirms the booking.");
        return await rooms.EnsureForBookingAsync(r.BookingId, ct, notify: false) ?? throw new BadRequestException("This booking isn't a video consultation.");
    }
}

/// <summary>
/// Starts a video call from a chat, for businesses that offer video consultations: a room open for a couple of hours, announced in the
/// conversation with a "Join video call" message.
/// </summary>
public sealed record StartChatVideoCommand(Guid ConversationId) : IRequest<Guid>;

public sealed class StartChatVideoHandler(IUnitOfWork uow, ICurrentUser user, ChatMessenger messenger, IOptions<VideoOptions> options)
    : IRequestHandler<StartChatVideoCommand, Guid>
{
    public async Task<Guid> Handle(StartChatVideoCommand r, CancellationToken ct)
    {
        var side = await ChatAccess.GetAsync(uow, user, r.ConversationId, ct);
        if (!side.Conversation.Business.OffersVideoConsultation)
            throw new BadRequestException($"{side.Conversation.Business.Name} doesn't offer video consultations.");
        var now = DateTimeOffset.UtcNow;
        // One open call per conversation: starting again joins the same room.
        var open = await uow.Repository<VideoRoom>().QueryNoTracking()
            .Where(v => v.ConversationId == r.ConversationId && v.Status != "Ended" && v.ClosesAt > now).OrderByDescending(v => v.CreatedOn)
            .Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
        if (open is { } id) return id;

        var room = new VideoRoom
        {
            BusinessId = side.Conversation.BusinessId, CustomerUserId = side.CustomerUserId, ConversationId = r.ConversationId,
            OpensAt = now, ClosesAt = now.AddMinutes(options.Value.InstantRoomMinutes),
        };
        uow.Repository<VideoRoom>().Add(room);
        await uow.SaveChangesAsync(ct);
        await messenger.SendAsync(r.ConversationId, "📹 Started a video call", null, ct, room.Id);
        return room.Id;
    }
}

/// <summary>Either side ends the call: the room closes and its length is recorded.</summary>
public sealed record EndVideoRoomCommand(Guid RoomId) : IRequest<Unit>;

public sealed class EndVideoRoomHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<EndVideoRoomCommand, Unit>
{
    public async Task<Unit> Handle(EndVideoRoomCommand r, CancellationToken ct)
    {
        var (room, _, _) = await VideoAccess.GetAsync(uow, user, r.RoomId, ct, track: true);
        if (room.Status == "Ended") return Unit.Value;
        var now = DateTimeOffset.UtcNow;
        room.Status = "Ended";
        room.EndedAt = now;
        if (room.StartedAt is { } started) room.DurationSeconds = (int)Math.Max(0, (now - started).TotalSeconds);
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ===================== Joining (used by the signalling hub) =====================

/// <param name="Role">"Customer" or "Business".</param>
public sealed record VideoJoinDto(Guid RoomId, string Role, string OtherUserId);

/// <summary>May the current user join this room now? Marks the room live when the call starts.</summary>
public sealed record AuthorizeVideoJoinCommand(Guid RoomId, bool BothPresent) : IRequest<VideoJoinDto>;

public sealed class AuthorizeVideoJoinHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<AuthorizeVideoJoinCommand, VideoJoinDto>
{
    public async Task<VideoJoinDto> Handle(AuthorizeVideoJoinCommand r, CancellationToken ct)
    {
        var (room, role, ownerId) = await VideoAccess.GetAsync(uow, user, r.RoomId, ct, track: true);
        var now = DateTimeOffset.UtcNow;
        if (!VideoAccess.CanJoin(room, now))
            throw new BadRequestException(room.Status == "Ended" ? "This call has ended." : room.OpensAt > now
                ? $"The call opens at {room.OpensAt.Value.ToOffset(IndianTime.Offset):h:mm tt}." : "This call has closed.");
        if (r.BothPresent && room.Status != "Live")
        {
            room.Status = "Live";
            room.StartedAt ??= now;
            await uow.SaveChangesAsync(ct);
        }
        return new VideoJoinDto(room.Id, role, role == ChatRoles.Customer ? ownerId : room.CustomerUserId);
    }
}
