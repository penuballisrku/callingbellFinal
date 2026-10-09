using System.Collections.Concurrent;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Features.Chat;
using CallingBell.Application.Features.Video;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CallingBell.Api.Hubs;

/// <summary>
/// Live chat. Messages are sent and stored over the REST API (so nothing is lost if the socket drops) and pushed here to both people:
/// "message", "read" (read receipts) and "typing". Only people in a conversation get its events.
/// </summary>
[Authorize]
public sealed class ChatHub(ISender sender) : Hub
{
    public const string MessageEvent = "message";
    public const string ReadEvent = "read";
    public const string TypingEvent = "typing";

    /// <summary>The caller is typing in a conversation: tells the other person (checked once per conversation and connection).</summary>
    public async Task Typing(Guid conversationId)
    {
        var key = $"peer:{conversationId:N}";
        if (Context.Items[key] is not ChatPeerDto peer)
        {
            try { peer = await sender.Send(new GetChatPeerQuery(conversationId)); }
            catch (Exception ex) when (ex is ForbiddenAccessException or NotFoundException) { return; }
            Context.Items[key] = peer;
        }
        await Clients.User(peer.OtherUserId).SendAsync(TypingEvent, new { conversationId, role = peer.MyRole });
    }
}

public sealed class ChatRealtime(IHubContext<ChatHub> hub) : IChatRealtime
{
    public Task MessageAsync(IReadOnlyCollection<string> userIds, ChatMessageDto message, CancellationToken ct) =>
        hub.Clients.Users(userIds).SendAsync(ChatHub.MessageEvent, message, ct);

    public Task ReadAsync(IReadOnlyCollection<string> userIds, Guid conversationId, string readerRole, DateTimeOffset readAt, CancellationToken ct) =>
        hub.Clients.Users(userIds).SendAsync(ChatHub.ReadEvent, new { conversationId, role = readerRole, readAt }, ct);
}

/// <summary>
/// Signalling for browser-to-browser video calls. The server never sees the audio or video: it only lets the two people allowed in a
/// room find each other and exchange connection details (WebRTC offer/answer and network candidates).
/// Events: "peer-joined" / "peer-left" (connection id and role) and "signal" (from, data).
/// </summary>
[Authorize]
public sealed class VideoHub(ISender sender, ILogger<VideoHub> logger) : Hub
{
    private sealed record Participant(string ConnectionId, string UserId, string Role);

    // Rooms → connections in them (this server); and connection → its room, for disconnects.
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, Participant>> Rooms = new();
    private static readonly ConcurrentDictionary<string, Guid> ConnectionRooms = new();

    private static string Group(Guid roomId) => $"video:{roomId:N}";

    /// <summary>Joins a room the caller belongs to, when it is open. Returns the caller's role and who is already there.</summary>
    public async Task<object> Join(Guid roomId)
    {
        var userId = Context.UserIdentifier ?? throw new HubException("Sign in to join the call.");
        var room = Rooms.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, Participant>());
        var others = room.Values.Where(p => p.UserId != userId).ToList();

        VideoJoinDto join;
        try { join = await sender.Send(new AuthorizeVideoJoinCommand(roomId, BothPresent: others.Count > 0)); }
        catch (Exception ex) when (ex is ForbiddenAccessException or NotFoundException) { throw new HubException("You can't join this call."); }
        catch (BadRequestException ex) { throw new HubException(ex.Message); }

        // The same person again (another tab, or a reload): the newer connection replaces the older one.
        foreach (var stale in room.Values.Where(p => p.UserId == userId).ToList())
        {
            room.TryRemove(stale.ConnectionId, out _);
            ConnectionRooms.TryRemove(stale.ConnectionId, out _);
            await Groups.RemoveFromGroupAsync(stale.ConnectionId, Group(roomId));
            await Clients.Group(Group(roomId)).SendAsync("peer-left", new { connectionId = stale.ConnectionId, role = stale.Role });
        }

        var me = new Participant(Context.ConnectionId, userId, join.Role);
        room[Context.ConnectionId] = me;
        ConnectionRooms[Context.ConnectionId] = roomId;
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(roomId));
        await Clients.OthersInGroup(Group(roomId)).SendAsync("peer-joined", new { connectionId = me.ConnectionId, role = me.Role });
        logger.LogInformation("Video room {RoomId}: {Role} joined ({Count} present)", roomId, join.Role, room.Count);
        return new { role = join.Role, peers = others.Select(p => new { connectionId = p.ConnectionId, role = p.Role }) };
    }

    /// <summary>Relays connection details to the other person in the same room (and nobody else).</summary>
    public async Task Signal(Guid roomId, string toConnectionId, string data)
    {
        if (data.Length > 64_000) return;
        if (!Rooms.TryGetValue(roomId, out var room) || !room.ContainsKey(Context.ConnectionId) || !room.ContainsKey(toConnectionId)) return;
        await Clients.Client(toConnectionId).SendAsync("signal", new { from = Context.ConnectionId, data });
    }

    public Task Leave(Guid roomId) => RemoveAsync(Context.ConnectionId, roomId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionRooms.TryGetValue(Context.ConnectionId, out var roomId)) await RemoveAsync(Context.ConnectionId, roomId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task RemoveAsync(string connectionId, Guid roomId)
    {
        ConnectionRooms.TryRemove(connectionId, out _);
        if (!Rooms.TryGetValue(roomId, out var room) || !room.TryRemove(connectionId, out var gone)) return;
        if (room.IsEmpty) Rooms.TryRemove(roomId, out _);
        await Groups.RemoveFromGroupAsync(connectionId, Group(roomId));
        await Clients.Group(Group(roomId)).SendAsync("peer-left", new { connectionId, role = gone.Role });
    }
}
