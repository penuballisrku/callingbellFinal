using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Chat;
using CallingBell.Application.Features.Staff;
using CallingBell.Application.Features.Video;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CallingBell.Api.Controllers;

/// <summary>Customer-business chat. Live events come over the ChatHub (/hubs/chat); everything is stored first through these endpoints.</summary>
[Authorize, Route("api/chat")]
public sealed class ChatController : ApiControllerBase
{
    public sealed record StartRequest(Guid BusinessId);
    public sealed record MessageRequest(string? Body);

    /// <summary>Opens (or reopens) my conversation with a business.</summary>
    [HttpPost("conversations")]
    public async Task<ActionResult<ApiResponse<ConversationDto>>> Start(StartRequest body, CancellationToken ct) =>
        Success(await Sender.Send(new StartConversationCommand(body.BusinessId), ct));

    /// <param name="role">"Customer" (default) or "Business" (with <paramref name="businessId"/>, one of my businesses).</param>
    [HttpGet("conversations")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ConversationDto>>>> List([FromQuery] string? role, [FromQuery] Guid? businessId, CancellationToken ct) =>
        Success(await Sender.Send(new GetConversationsQuery(role == ChatRoles.Business ? ChatRoles.Business : ChatRoles.Customer, businessId), ct));

    [HttpGet("unread")]
    public async Task<ActionResult<ApiResponse<ChatUnreadDto>>> Unread(CancellationToken ct) => Success(await Sender.Send(new GetChatUnreadQuery(), ct));

    /// <param name="before">Older messages than this (for scrolling back).</param>
    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ChatMessageDto>>>> Messages(Guid id, [FromQuery] DateTimeOffset? before, [FromQuery] int take = 40,
        CancellationToken ct = default) =>
        Success(await Sender.Send(new GetChatMessagesQuery(id, before, take), ct));

    [HttpPost("conversations/{id:guid}/messages"), EnableRateLimiting("chat")]
    public async Task<ActionResult<ApiResponse<ChatMessageDto>>> Send(Guid id, MessageRequest body, CancellationToken ct) =>
        Success(await Sender.Send(new SendChatMessageCommand(id, body.Body), ct));

    /// <summary>Sends an image (JPG, PNG, WebP) or a PDF, up to 10 MB, with an optional caption.</summary>
    [HttpPost("conversations/{id:guid}/attachments"), EnableRateLimiting("chat"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<ChatMessageDto>>> Attach(Guid id, IFormFile file, [FromForm] string? caption, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return Success(await Sender.Send(new SendChatAttachmentCommand(id, file.FileName, ms.ToArray(), caption), ct));
    }

    [HttpPost("conversations/{id:guid}/read")]
    public async Task<ActionResult<ApiResponse<object>>> Read(Guid id, CancellationToken ct)
    {
        await Sender.Send(new MarkConversationReadCommand(id), ct);
        return Done();
    }

    /// <summary>Starts (or rejoins) a video call from the chat, for businesses that offer video consultations. Returns the room id.</summary>
    [HttpPost("conversations/{id:guid}/video")]
    public async Task<ActionResult<ApiResponse<Guid>>> Video(Guid id, CancellationToken ct) => Success(await Sender.Send(new StartChatVideoCommand(id), ct));

    /// <summary>An attachment by its signed, expiring link (the links come with the messages, to the two people in the chat).</summary>
    [AllowAnonymous, HttpGet("attachments/{messageId:guid}")]
    public async Task<IActionResult> Attachment(Guid messageId, [FromQuery] long exp, [FromQuery] string? sig, CancellationToken ct)
    {
        var file = await Sender.Send(new GetChatAttachmentQuery(messageId, exp, sig), ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Data, file.ContentType, file.ContentType.StartsWith("image/") ? null : file.FileName);
    }
}

/// <summary>Private video calls for video consultations (signalling over the VideoHub, /hubs/video).</summary>
[Authorize, Route("api/video")]
public sealed class VideoController : ApiControllerBase
{
    /// <summary>The room, my role in it, whether it is open, and the STUN/TURN servers to connect with.</summary>
    [HttpGet("rooms/{id:guid}")]
    public async Task<ActionResult<ApiResponse<VideoRoomDto>>> Room(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetVideoRoomQuery(id), ct));
    }

    [HttpPost("rooms/{id:guid}/end")]
    public async Task<ActionResult<ApiResponse<object>>> End(Guid id, CancellationToken ct)
    {
        await Sender.Send(new EndVideoRoomCommand(id), ct);
        return Done("Call ended");
    }

    /// <summary>The video room of one of my confirmed video bookings (customer or business).</summary>
    [HttpGet("bookings/{bookingId:guid}/room")]
    public async Task<ActionResult<ApiResponse<Guid>>> BookingRoom(Guid bookingId, CancellationToken ct) =>
        Success(await Sender.Send(new GetBookingVideoRoomQuery(bookingId), ct));
}
