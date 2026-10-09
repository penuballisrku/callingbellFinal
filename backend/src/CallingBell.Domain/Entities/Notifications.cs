using CallingBell.Domain.Common;

namespace CallingBell.Domain.Entities;

/// <summary>
/// A browser (or, later, app) registered for push notifications: a Firebase Cloud Messaging registration token belonging to a user.
/// A user can have several. Tokens Firebase reports as invalid are marked inactive, never reused.
/// </summary>
public class NotificationDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    /// <summary>SHA-256 of <see cref="Token"/> (hex): the unique key, since tokens are too long for an index key.</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>"WEB" (more types when the mobile apps arrive).</summary>
    public string DeviceType { get; set; } = "WEB";
    public string? Browser { get; set; }
    public string? Platform { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Why it was turned off, e.g. "UNREGISTERED" from Firebase, or "SignedOut".</summary>
    public string? DeactivatedReason { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// One attempt to deliver a notification (or an OTP) over one channel through one provider. The router stops at the first attempt a
/// provider accepts; <see cref="Status"/> then moves on as the provider reports back (webhooks): Accepted → Sent → Delivered → Read, or Failed.
/// Message text and OTP codes are never stored here.
/// </summary>
public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? NotificationId { get; set; }
    public Guid? OtpCodeId { get; set; }
    public string RouteCode { get; set; } = string.Empty;
    /// <summary>Position of the channel in the route (0 = first), so a later failure can continue from the next one.</summary>
    public int RouteStep { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    /// <summary>Masked recipient for support ("+91 •••••43210", "2 devices").</summary>
    public string? Recipient { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public Notification? Notification { get; set; }
}

/// <summary>
/// A provider able to send over a channel (e.g. "Msg91" for SMS in India, "Twilio" elsewhere). Within a channel the router tries the active
/// providers for the recipient's country (or any country: null) by <see cref="Priority"/>. <see cref="ConfigurationKey"/> names the
/// configuration section holding its settings; credentials themselves live only in environment variables / Key Vault.
/// </summary>
public class NotificationProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderName { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; }
    public string? CountryCode { get; set; }
    public string? ConfigurationKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The text for a route on a channel. For WhatsApp, <see cref="TemplateName"/> is the Meta-approved template's name and
/// <see cref="TemplateContent"/> its body for reference; for SMS, <see cref="TemplateName"/> is the DLT template id (India) and
/// <see cref="TemplateContent"/> the registered text. Parameters are written {{1}}, {{2}}, …
/// </summary>
public class NotificationTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TemplateCode { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en";
    public string TemplateName { get; set; } = string.Empty;
    public string TemplateContent { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The channels a route tries, in <see cref="Priority"/> order (1 first), e.g. NEW_LEAD: WEB_PUSH, WHATSAPP, RCS, SMS.</summary>
public class NotificationRoutingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RouteCode { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
