using CallingBell.Domain.Common;

namespace CallingBell.Domain.Entities;

public class Review : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string CustomerUserId { get; set; } = string.Empty;
    public Guid? BookingId { get; set; }
    public byte Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Published";
    public string? OwnerReply { get; set; }
    public DateTimeOffset? RepliedOn { get; set; }
    public int HelpfulCount { get; set; }
    public bool IsVerifiedVisit { get; set; }
    public string? ReportReason { get; set; }

    public Business Business { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;
}

/// <summary>A lead: general enquiry, quotation request or callback request.</summary>
public class Enquiry : AuditableEntity
{
    public string EnquiryNumber { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public Guid? ServiceId { get; set; }
    public string? CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string EnquiryType { get; set; } = "Enquiry";
    public string Message { get; set; } = string.Empty;
    public DateTime? PreferredDate { get; set; }
    public decimal? Budget { get; set; }
    public decimal? QuotedAmount { get; set; }
    public string Status { get; set; } = "New";
    public string Source { get; set; } = "Profile";
    public DateTimeOffset? RespondedOn { get; set; }

    public Business Business { get; set; } = null!;
    public BusinessService? Service { get; set; }
}

public class Booking : AuditableEntity
{
    public string BookingNumber { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public Guid ServiceId { get; set; }
    public string CustomerUserId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public DateTimeOffset ScheduledStart { get; set; }
    public DateTimeOffset ScheduledEnd { get; set; }
    public string Status { get; set; } = "Pending";
    public decimal Amount { get; set; }
    public decimal CommissionAmount { get; set; }
    public string PaymentStatus { get; set; } = "Pending";
    public string? ServiceAddress { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    /// <summary>The team member doing it (null = not assigned yet).</summary>
    public Guid? StaffId { get; set; }

    public Business Business { get; set; } = null!;
    public BusinessService Service { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;
    public BusinessStaff? Staff { get; set; }
}

public class Favorite : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public DateTimeOffset CreatedOn { get; set; }

    public Business Business { get; set; } = null!;
}
