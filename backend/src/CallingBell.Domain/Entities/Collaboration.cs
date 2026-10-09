using CallingBell.Domain.Common;

namespace CallingBell.Domain.Entities;

/// <summary>
/// A chat between a customer and a business (one per pair). Unread counts and last-read times are kept per side, so lists show unread
/// badges without counting messages, and read receipts are a timestamp comparison.
/// </summary>
public class Conversation : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string CustomerUserId { get; set; } = string.Empty;
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    /// <summary>"Customer" or "Business".</summary>
    public string? LastSenderRole { get; set; }
    public int CustomerUnreadCount { get; set; }
    public int BusinessUnreadCount { get; set; }
    public DateTimeOffset? CustomerLastReadAt { get; set; }
    public DateTimeOffset? BusinessLastReadAt { get; set; }

    public Business Business { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;
}

/// <summary>A message: text, an attachment (image or PDF in dbo.Media), or both.</summary>
public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public string SenderUserId { get; set; } = string.Empty;
    /// <summary>"Customer" or "Business".</summary>
    public string SenderRole { get; set; } = string.Empty;
    public string? Body { get; set; }
    public Guid? AttachmentMediaId { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentContentType { get; set; }
    public int? AttachmentSize { get; set; }
    /// <summary>A video call started from the chat ("Join video call").</summary>
    public Guid? VideoRoomId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public bool IsDeleted { get; set; }

    public Conversation Conversation { get; set; } = null!;
}

/// <summary>A member of a business's team: who does which services, and when they work.</summary>
public class BusinessStaff : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Title { get; set; }
    /// <summary>Private: shown to the owner only, never on public pages.</summary>
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Bio { get; set; }
    public int? YearsExperience { get; set; }
    public string? Languages { get; set; }
    /// <summary>The Calling Bell account of this person, when linked.</summary>
    public string? UserId { get; set; }
    /// <summary>Can be booked by customers (some team members only support the others).</summary>
    public bool AcceptsBookings { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public Business Business { get; set; } = null!;
    public ICollection<BusinessStaffService> Services { get; set; } = new List<BusinessStaffService>();
    public ICollection<BusinessStaffHour> Hours { get; set; } = new List<BusinessStaffHour>();
}

public class BusinessStaffService
{
    public Guid StaffId { get; set; }
    public Guid ServiceId { get; set; }

    public BusinessStaff Staff { get; set; } = null!;
    public BusinessService Service { get; set; } = null!;
}

/// <summary>A team member's hours on one day (0 = Sunday). No row for a day = the business's hours apply.</summary>
public class BusinessStaffHour
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffId { get; set; }
    public byte DayOfWeek { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public bool IsClosed { get; set; }

    public BusinessStaff Staff { get; set; } = null!;
}

/// <summary>
/// A private video call between a customer and a business, for a booking (opens shortly before it starts) or from a chat. Peers connect
/// browser to browser (WebRTC); the server only relays the connection set-up between the two people allowed in the room.
/// </summary>
public class VideoRoom : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string CustomerUserId { get; set; } = string.Empty;
    public Guid? BookingId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? StaffId { get; set; }
    /// <summary>Scheduled, Live or Ended.</summary>
    public string Status { get; set; } = "Scheduled";
    public DateTimeOffset? OpensAt { get; set; }
    public DateTimeOffset? ClosesAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }

    public Business Business { get; set; } = null!;
    public Booking? Booking { get; set; }
}
