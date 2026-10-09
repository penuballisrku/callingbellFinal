using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Application.Features.Onboarding;
using CallingBell.Application.Features.Owner;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;

namespace CallingBell.Application.Features.Chat;

public static class ChatRoles
{
    public const string Customer = "Customer";
    public const string Business = "Business";
}

// ===================== DTOs =====================

/// <param name="Url">Signed, expiring link (the file is private to the conversation).</param>
public sealed record ChatAttachmentDto(string Url, string Name, string ContentType, int Size, bool IsImage);

/// <param name="VideoRoomId">A video call started in the chat (shows "Join video call").</param>
public sealed record ChatMessageDto(Guid Id, Guid ConversationId, string SenderRole, string? Body, ChatAttachmentDto? Attachment, DateTimeOffset SentAt,
    DateTimeOffset? ReadAt, Guid? VideoRoomId = null);

/// <summary>A conversation as one side sees it.</summary>
/// <param name="MyRole">"Customer" or "Business": which side the viewer is on.</param>
/// <param name="OtherLastReadAt">When the other side last read the conversation (read receipts for my messages).</param>
public sealed record ConversationDto(Guid Id, Guid BusinessId, string BusinessName, string BusinessSlug, string? BusinessLogoUrl, string? AvailabilityStatus,
    bool OffersVideoConsultation, string CustomerName, string MyRole, DateTimeOffset? LastMessageAt, string? LastMessagePreview, string? LastSenderRole,
    int UnreadCount, DateTimeOffset? OtherLastReadAt);

public sealed record ChatUnreadDto(int AsCustomer, int AsBusiness);

// ===================== Ports =====================

/// <summary>Pushes chat events to the people in a conversation (SignalR ChatHub). Implemented in the API.</summary>
public interface IChatRealtime
{
    Task MessageAsync(IReadOnlyCollection<string> userIds, ChatMessageDto message, CancellationToken ct);
    Task ReadAsync(IReadOnlyCollection<string> userIds, Guid conversationId, string readerRole, DateTimeOffset readAt, CancellationToken ct);
}

/// <summary>Signed, expiring links for chat attachments, which are private to the conversation.</summary>
public interface IChatAttachmentSigner
{
    string Url(Guid messageId);
    bool Verify(Guid messageId, long expires, string? signature);
}

internal static class ChatText
{
    /// <summary>Message text: control characters removed (line breaks kept), at most three blank lines in a row, trimmed, 2,000 characters.</summary>
    public static string? Clean(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var chars = body.Replace("\r\n", "\n").Where(ch => ch == '\n' || !char.IsControl(ch) && char.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.Format).ToArray();
        var text = System.Text.RegularExpressions.Regex.Replace(new string(chars), "\n{4,}", "\n\n\n").Trim();
        return text.Length == 0 ? null : text.Length <= 2000 ? text : text[..2000];
    }
}

// ===================== Access =====================

internal sealed record ChatSide(Conversation Conversation, string Role, string CustomerUserId, string OwnerUserId)
{
    public IReadOnlyCollection<string> Members => [CustomerUserId, OwnerUserId];
    public string OtherUserId(string me) => me == CustomerUserId ? OwnerUserId : CustomerUserId;
}

internal static class ChatAccess
{
    /// <summary>The conversation and the viewer's side in it: the customer, or the business's owner. Anyone else is refused.</summary>
    public static async Task<ChatSide> GetAsync(IUnitOfWork uow, ICurrentUser user, Guid conversationId, CancellationToken ct, bool track = false)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var query = track ? uow.Repository<Conversation>().Query() : uow.Repository<Conversation>().QueryNoTracking();
        var c = await query.Include(x => x.Business).FirstOrDefaultAsync(x => x.Id == conversationId, ct)
                ?? throw new NotFoundException("Conversation", conversationId);
        var role = c.CustomerUserId == userId ? ChatRoles.Customer : c.Business.OwnerUserId == userId ? ChatRoles.Business : null;
        return role is null ? throw new ForbiddenAccessException() : new ChatSide(c, role, c.CustomerUserId, c.Business.OwnerUserId);
    }

    public static ChatMessageDto ToDto(ChatMessage m, IChatAttachmentSigner signer) => new(m.Id, m.ConversationId, m.SenderRole, m.Body,
        m.AttachmentMediaId is null ? null : new ChatAttachmentDto(signer.Url(m.Id), m.AttachmentName ?? "file", m.AttachmentContentType ?? "application/octet-stream",
            m.AttachmentSize ?? 0, m.AttachmentContentType?.StartsWith("image/") == true),
        m.SentAt, m.ReadAt, m.VideoRoomId);
}

// ===================== Start / list =====================

/// <summary>A customer opens (or reopens) their conversation with a business.</summary>
public sealed record StartConversationCommand(Guid BusinessId) : IRequest<ConversationDto>;

public sealed class StartConversationHandler(IUnitOfWork uow, ICurrentUser user, ISender sender) : IRequestHandler<StartConversationCommand, ConversationDto>
{
    public async Task<ConversationDto> Handle(StartConversationCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var business = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Id == r.BusinessId && b.Status == BusinessStatuses.Active)
            .Select(b => new { b.Id, b.OwnerUserId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Business", r.BusinessId);
        if (business.OwnerUserId == userId) throw new BadRequestException("You can't start a chat with your own business.");

        var id = await uow.Repository<Conversation>().QueryNoTracking()
            .Where(c => c.BusinessId == business.Id && c.CustomerUserId == userId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (id is null)
        {
            var conversation = new Conversation { BusinessId = business.Id, CustomerUserId = userId };
            uow.Repository<Conversation>().Add(conversation);
            await uow.SaveChangesAsync(ct);
            id = conversation.Id;
        }
        var list = await sender.Send(new GetConversationsQuery(ChatRoles.Customer, null, id), ct);
        return list.Single();
    }
}

/// <param name="Role">"Customer" (my chats with businesses) or "Business" (chats with my business's customers).</param>
/// <param name="BusinessId">For "Business": which of my businesses.</param>
/// <param name="ConversationId">Just this conversation.</param>
public sealed record GetConversationsQuery(string Role, Guid? BusinessId, Guid? ConversationId = null) : IRequest<IReadOnlyList<ConversationDto>>;

public sealed class GetConversationsHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetConversationsQuery, IReadOnlyList<ConversationDto>>
{
    public async Task<IReadOnlyList<ConversationDto>> Handle(GetConversationsQuery r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var query = uow.Repository<Conversation>().QueryNoTracking();
        if (r.Role == ChatRoles.Business)
        {
            if (r.BusinessId is not { } businessId) throw new BadRequestException("Choose a business.");
            await OwnerAccess.GetOwnedAsync(uow, user, businessId, ct);
            query = query.Where(c => c.BusinessId == businessId && c.LastMessageAt != null);
        }
        else query = query.Where(c => c.CustomerUserId == userId);
        if (r.ConversationId is { } one) query = query.Where(c => c.Id == one);

        var business = r.Role == ChatRoles.Business;
        return await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedOn).Take(100)
            .Select(c => new ConversationDto(c.Id, c.BusinessId, c.Business.Name, c.Business.Slug, c.Business.LogoUrl, c.Business.AvailabilityStatus,
                c.Business.OffersVideoConsultation, c.Customer.DisplayName, business ? ChatRoles.Business : ChatRoles.Customer, c.LastMessageAt,
                c.LastMessagePreview, c.LastSenderRole, business ? c.BusinessUnreadCount : c.CustomerUnreadCount,
                business ? c.CustomerLastReadAt : c.BusinessLastReadAt))
            .ToListAsync(ct);
    }
}

/// <summary>Unread messages for the header badge: as a customer, and across the businesses I own.</summary>
public sealed record GetChatUnreadQuery : IRequest<ChatUnreadDto>;

public sealed class GetChatUnreadHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetChatUnreadQuery, ChatUnreadDto>
{
    public async Task<ChatUnreadDto> Handle(GetChatUnreadQuery r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var conversations = uow.Repository<Conversation>().QueryNoTracking();
        var asCustomer = await conversations.Where(c => c.CustomerUserId == userId).SumAsync(c => (int?)c.CustomerUnreadCount, ct) ?? 0;
        var asBusiness = await conversations.Where(c => c.Business.OwnerUserId == userId).SumAsync(c => (int?)c.BusinessUnreadCount, ct) ?? 0;
        return new ChatUnreadDto(asCustomer, asBusiness);
    }
}

// ===================== Messages =====================

/// <param name="Before">Messages sent before this time (older pages); omit for the latest.</param>
public sealed record GetChatMessagesQuery(Guid ConversationId, DateTimeOffset? Before, int Take = 40) : IRequest<IReadOnlyList<ChatMessageDto>>;

public sealed class GetChatMessagesHandler(IUnitOfWork uow, ICurrentUser user, IChatAttachmentSigner signer)
    : IRequestHandler<GetChatMessagesQuery, IReadOnlyList<ChatMessageDto>>
{
    public async Task<IReadOnlyList<ChatMessageDto>> Handle(GetChatMessagesQuery r, CancellationToken ct)
    {
        await ChatAccess.GetAsync(uow, user, r.ConversationId, ct);
        var query = uow.Repository<ChatMessage>().QueryNoTracking().Where(m => m.ConversationId == r.ConversationId);
        if (r.Before is { } before) query = query.Where(m => m.SentAt < before);
        var page = await query.OrderByDescending(m => m.SentAt).Take(Math.Clamp(r.Take, 1, 100)).ToListAsync(ct);
        page.Reverse();
        return page.Select(m => ChatAccess.ToDto(m, signer)).ToList();
    }
}

public sealed record SendChatMessageCommand(Guid ConversationId, string? Body) : IRequest<ChatMessageDto>;

public sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator() =>
        RuleFor(x => x.Body).NotEmpty().WithMessage("Type a message.").Must(b => b!.Trim().Length is > 0 and <= 2000).WithMessage("Keep messages under 2,000 characters.");
}

public sealed class SendChatMessageHandler(ChatMessenger messenger) : IRequestHandler<SendChatMessageCommand, ChatMessageDto>
{
    public Task<ChatMessageDto> Handle(SendChatMessageCommand r, CancellationToken ct) => messenger.SendAsync(r.ConversationId, r.Body, null, ct);
}

/// <summary>An image (JPG, PNG, WebP) or a PDF, up to 10 MB, with an optional caption.</summary>
public sealed record SendChatAttachmentCommand(Guid ConversationId, string FileName, byte[] Data, string? Caption) : IRequest<ChatMessageDto>;

public sealed class SendChatAttachmentValidator : AbstractValidator<SendChatAttachmentCommand>
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public SendChatAttachmentValidator()
    {
        RuleFor(x => x.Data).NotEmpty().WithMessage("Choose a file.").Must(d => d.LongLength <= MaxBytes).WithMessage("Files can be up to 10 MB.");
        RuleFor(x => x.FileName).MaximumLength(200);
        RuleFor(x => x.Caption).MaximumLength(2000);
    }
}

public sealed class SendChatAttachmentHandler(ChatMessenger messenger) : IRequestHandler<SendChatAttachmentCommand, ChatMessageDto>
{
    public Task<ChatMessageDto> Handle(SendChatAttachmentCommand r, CancellationToken ct)
    {
        var type = Owner.MediaRules.DetectImage(r.Data) is { } image ? image.ContentType
            : r.Data.Length > 5 && r.Data[0] == '%' && r.Data[1] == 'P' && r.Data[2] == 'D' && r.Data[3] == 'F' && r.Data[4] == '-' ? "application/pdf" : null;
        if (type is null) throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["Send a JPG, PNG or WebP image, or a PDF."] });
        var name = JoinImport.Clean(Path.GetFileName(r.FileName), 200) ?? (type == "application/pdf" ? "document.pdf" : "photo");
        return messenger.SendAsync(r.ConversationId, r.Caption, (name, type, r.Data), ct);
    }
}

/// <summary>
/// Stores a message, updates the conversation (preview, unread count for the other side), pushes it to both people live, and notifies the
/// other side when their conversation goes from read to unread (one alert per burst of messages, not one per message).
/// </summary>
public sealed class ChatMessenger(IUnitOfWork uow, ICurrentUser user, IChatRealtime realtime, IChatAttachmentSigner signer, IRealtimeNotifier notifier,
    INotificationDispatchSignal dispatch)
{
    public async Task<ChatMessageDto> SendAsync(Guid conversationId, string? body, (string Name, string Type, byte[] Data)? file, CancellationToken ct,
        Guid? videoRoomId = null)
    {
        var side = await ChatAccess.GetAsync(uow, user, conversationId, ct, track: true);
        var c = side.Conversation;
        var now = DateTimeOffset.UtcNow;
        var text = ChatText.Clean(body);

        var message = new ChatMessage { ConversationId = c.Id, SenderUserId = user.UserId!, SenderRole = side.Role, Body = text, SentAt = now, VideoRoomId = videoRoomId };
        if (file is { } f)
        {
            // Private: inactive media isn't served by /api/media; only the signed chat link serves it, to the two people in the chat.
            var media = new Domain.Entities.Media
            {
                EntityType = "ChatAttachment", EntityId = message.Id, FileName = f.Name, ContentType = f.Type,
                FileExtension = Path.GetExtension(f.Name).TrimStart('.').ToLowerInvariant() is { Length: > 0 and <= 10 } ext ? ext : (f.Type == "application/pdf" ? "pdf" : "img"),
                FileSize = f.Data.LongLength, FileData = f.Data, IsActive = false, CreatedOn = now, CreatedBy = user.UserId,
            };
            uow.Repository<Domain.Entities.Media>().Add(media);
            message.AttachmentMediaId = media.MediaId;
            message.AttachmentName = f.Name;
            message.AttachmentContentType = f.Type;
            message.AttachmentSize = (int)f.Data.LongLength;
        }
        if (message.Body is null && message.AttachmentMediaId is null) throw new ValidationException(new Dictionary<string, string[]> { ["body"] = ["Type a message."] });
        uow.Repository<ChatMessage>().Add(message);

        var preview = message.Body ?? (message.AttachmentContentType == "application/pdf" ? "📄 " + message.AttachmentName : "📷 Photo");
        c.LastMessageAt = now;
        c.LastMessagePreview = preview.Length > 200 ? preview[..197] + "…" : preview;
        c.LastSenderRole = side.Role;
        bool wasRead;
        if (side.Role == ChatRoles.Customer) { wasRead = c.BusinessUnreadCount == 0; c.BusinessUnreadCount++; c.CustomerLastReadAt = now; c.CustomerUnreadCount = 0; }
        else { wasRead = c.CustomerUnreadCount == 0; c.CustomerUnreadCount++; c.BusinessLastReadAt = now; c.BusinessUnreadCount = 0; }
        await uow.SaveChangesAsync(ct);

        var dto = ChatAccess.ToDto(message, signer);
        await realtime.MessageAsync(side.Members, dto, ct);

        if (wasRead)
        {
            var toBusiness = side.Role == ChatRoles.Customer;
            var recipient = toBusiness ? side.OwnerUserId : side.CustomerUserId;
            var from = toBusiness
                ? await uow.Repository<ApplicationUser>().QueryNoTracking().Where(u => u.Id == side.CustomerUserId).Select(u => u.DisplayName).FirstAsync(ct)
                : c.Business.Name;
            await NotificationPublisher.PublishAsync(uow, notifier, recipient, $"New message from {from}", c.LastMessagePreview, "Chat",
                toBusiness ? $"/owner/messages?c={c.Id}" : $"/account?tab=messages&c={c.Id}", ct,
                new NotificationRouting(NotificationRoutes.Chat, "ChatMessage", message.Id, $"{NotificationRoutes.Chat}:{message.Id}:{recipient}"), dispatch);
        }
        return dto;
    }
}

/// <summary>The viewer has seen the conversation: their unread count resets and the other side's messages are marked read.</summary>
public sealed record MarkConversationReadCommand(Guid ConversationId) : IRequest<Unit>;

public sealed class MarkConversationReadHandler(IUnitOfWork uow, ICurrentUser user, IChatRealtime realtime) : IRequestHandler<MarkConversationReadCommand, Unit>
{
    public async Task<Unit> Handle(MarkConversationReadCommand r, CancellationToken ct)
    {
        var side = await ChatAccess.GetAsync(uow, user, r.ConversationId, ct, track: true);
        var c = side.Conversation;
        var now = DateTimeOffset.UtcNow;
        var other = side.Role == ChatRoles.Customer ? ChatRoles.Business : ChatRoles.Customer;
        var unread = await uow.Repository<ChatMessage>().Query()
            .Where(m => m.ConversationId == c.Id && m.SenderRole == other && m.ReadAt == null).OrderByDescending(m => m.SentAt).Take(500).ToListAsync(ct);
        var hadUnread = (side.Role == ChatRoles.Customer ? c.CustomerUnreadCount : c.BusinessUnreadCount) > 0 || unread.Count > 0;
        unread.ForEach(m => m.ReadAt = now);
        if (side.Role == ChatRoles.Customer) { c.CustomerUnreadCount = 0; c.CustomerLastReadAt = now; }
        else { c.BusinessUnreadCount = 0; c.BusinessLastReadAt = now; }
        await uow.SaveChangesAsync(ct);
        if (hadUnread) await realtime.ReadAsync(side.Members, c.Id, side.Role, now, ct);
        return Unit.Value;
    }
}

// ===================== Attachments =====================

public sealed record ChatAttachmentFileDto(byte[] Data, string ContentType, string FileName);

/// <summary>An attachment by its signed link (checked against the message; the link expires).</summary>
public sealed record GetChatAttachmentQuery(Guid MessageId, long Expires, string? Signature) : IRequest<ChatAttachmentFileDto>;

public sealed class GetChatAttachmentHandler(IUnitOfWork uow, IChatAttachmentSigner signer) : IRequestHandler<GetChatAttachmentQuery, ChatAttachmentFileDto>
{
    public async Task<ChatAttachmentFileDto> Handle(GetChatAttachmentQuery r, CancellationToken ct)
    {
        if (!signer.Verify(r.MessageId, r.Expires, r.Signature)) throw new ForbiddenAccessException();
        var m = await uow.Repository<ChatMessage>().QueryNoTracking().Where(x => x.Id == r.MessageId && x.AttachmentMediaId != null)
            .Select(x => new { x.AttachmentMediaId, x.AttachmentName, x.AttachmentContentType }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Attachment", r.MessageId);
        var data = await uow.Repository<Domain.Entities.Media>().QueryNoTracking().Where(x => x.MediaId == m.AttachmentMediaId).Select(x => x.FileData).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Attachment", r.MessageId);
        return new ChatAttachmentFileDto(data, m.AttachmentContentType ?? "application/octet-stream", m.AttachmentName ?? "file");
    }
}

// ===================== Typing (hub) =====================

/// <summary>The other person in a conversation, for the typing indicator (the caller must be in it).</summary>
public sealed record GetChatPeerQuery(Guid ConversationId) : IRequest<ChatPeerDto>;

public sealed record ChatPeerDto(string MyRole, string OtherUserId);

public sealed class GetChatPeerHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetChatPeerQuery, ChatPeerDto>
{
    public async Task<ChatPeerDto> Handle(GetChatPeerQuery r, CancellationToken ct)
    {
        var side = await ChatAccess.GetAsync(uow, user, r.ConversationId, ct);
        return new ChatPeerDto(side.Role, side.OtherUserId(user.UserId!));
    }
}
