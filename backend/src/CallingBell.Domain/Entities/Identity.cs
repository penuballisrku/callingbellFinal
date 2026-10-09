using CallingBell.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace CallingBell.Domain.Entities;

public class ApplicationUser : IdentityUser, ISoftDeletable
{
    /// <summary>The person's full name (pre-existing column).</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string UserType { get; set; } = "Customer";
    public Guid? CityId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public bool IsDeleted { get; set; }

    public City? City { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>
/// A one-time code sent by SMS for mobile sign-in or sign-up. Only hashes are stored. A verified sign-up code issues a
/// short-lived verification token that the registration request must present.
/// </summary>
public class OtpCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Normalised mobile number, e.g. "+91 98765 43210".</summary>
    public string PhoneNumber { get; set; } = string.Empty;
    /// <summary>"SignIn" or "SignUp".</summary>
    public string Purpose { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? VerificationTokenHash { get; set; }
    public DateTimeOffset? VerificationExpiresAt { get; set; }
    public DateTimeOffset? VerificationUsedAt { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
}

public class Notification : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedOn { get; set; }

    /// <summary>
    /// The route (NEW_LEAD, BOOKING) whose channels deliver it beyond the app (web push, WhatsApp, …); null = in-app only. A background
    /// dispatcher works through it, so the request that created the notification doesn't wait for providers.
    /// </summary>
    public string? RouteCode { get; set; }
    /// <summary>What it's about, e.g. "Enquiry" / the enquiry's id, for message details and deep links.</summary>
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    /// <summary>Unique when set: the same event never notifies twice (e.g. "NEW_LEAD:{enquiryId}:{userId}").</summary>
    public string? IdempotencyKey { get; set; }
    public string? DispatchStatus { get; set; }
    /// <summary>The route position to try next (0 = first channel).</summary>
    public int DispatchStep { get; set; }
    public int DispatchAttempts { get; set; }
    public DateTimeOffset? NextDispatchAt { get; set; }
    /// <summary>Claimed by a dispatcher until then (several app instances may run).</summary>
    public DateTimeOffset? DispatchLockedUntil { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}
