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
}
