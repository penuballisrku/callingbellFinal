namespace CallingBell.Infrastructure.Notifications;

/// <summary>
/// Settings for delivering notifications beyond the app. Only non-secret values belong in appsettings.json; credentials (FCM service
/// account, WhatsApp access token and app secret, SMS / RCS keys, webhook tokens) come from environment variables (e.g.
/// <c>Notifications__WhatsApp__AccessToken</c>), user secrets in development, or Azure App Service settings backed by Key Vault.
/// </summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    public FcmOptions Fcm { get; set; } = new();
    public WhatsAppOptions WhatsApp { get; set; } = new();
    public RcsOptions Rcs { get; set; } = new();
    public SmsOptions Sms { get; set; } = new();
    public DispatcherOptions Dispatcher { get; set; } = new();
}

public sealed class FcmOptions
{
    public bool Enabled { get; set; }
    /// <summary>Secret. The Firebase service account key (JSON, or the JSON base64-encoded). Backend only.</summary>
    public string? ServiceAccountJson { get; set; }
    /// <summary>Alternative to <see cref="ServiceAccountJson"/>: path to the key file (outside the repository).</summary>
    public string? ServiceAccountFile { get; set; }
    /// <summary>Defaults to the service account's project.</summary>
    public string? ProjectId { get; set; }
    public string BaseUrl { get; set; } = "https://fcm.googleapis.com";
    /// <summary>How long FCM keeps a push for an offline browser.</summary>
    public int TimeToLiveSeconds { get; set; } = 86400;
    /// <summary>Notification icon shown by the browser (path on this site).</summary>
    public string IconUrl { get; set; } = "/icons/icon-192.png";
    /// <summary>Secret. Signs the browser's delivery acknowledgements; derived from Jwt:Key when empty.</summary>
    public string? AckSigningKey { get; set; }
    /// <summary>The Firebase web app's public settings, served to the browser (not secrets).</summary>
    public FirebaseWebOptions Web { get; set; } = new();
}

public sealed class FirebaseWebOptions
{
    public string? ApiKey { get; set; }
    public string? AuthDomain { get; set; }
    public string? ProjectId { get; set; }
    public string? MessagingSenderId { get; set; }
    public string? AppId { get; set; }
    /// <summary>Web Push certificate (VAPID) public key from Firebase Console › Cloud Messaging.</summary>
    public string? VapidKey { get; set; }
}

public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v21.0";
    public string? PhoneNumberId { get; set; }
    public string? BusinessAccountId { get; set; }
    /// <summary>Secret. System user access token with whatsapp_business_messaging.</summary>
    public string? AccessToken { get; set; }
    /// <summary>Secret. The Meta app secret, to verify webhook signatures (X-Hub-Signature-256).</summary>
    public string? AppSecret { get; set; }
    /// <summary>Secret. The verify token entered when subscribing the webhook in the Meta app.</summary>
    public string? WebhookVerifyToken { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>A vendor-neutral RCS gateway: JSON POST to <see cref="BaseUrl"/> + <see cref="SendPath"/> with a bearer credential.</summary>
public sealed class RcsOptions
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string SendPath { get; set; } = "/messages";
    public string? AgentId { get; set; }
    /// <summary>Secret. Sent as "Authorization: Bearer …".</summary>
    public string? Credentials { get; set; }
    /// <summary>The response's property holding the message id.</summary>
    public string MessageIdField { get; set; } = "messageId";
    /// <summary>Secret. Required as ?token= on the delivery-report webhook.</summary>
    public string? WebhookToken { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class SmsOptions
{
    public Msg91Options Msg91 { get; set; } = new();
    public TwilioOptions Twilio { get; set; } = new();
    public LogSmsOptions Log { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>MSG91 (India): Flow API with DLT-registered templates; dbo.NotificationTemplates.TemplateName holds the MSG91 template id.</summary>
public sealed class Msg91Options
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://control.msg91.com";
    /// <summary>Secret.</summary>
    public string? AuthKey { get; set; }
    /// <summary>DLT-registered sender id (header), when the template doesn't fix it.</summary>
    public string? SenderId { get; set; }
    /// <summary>DLT principal entity id, when the account requires it per request.</summary>
    public string? DltEntityId { get; set; }
    /// <summary>Secret. Required as ?token= on the delivery-report webhook.</summary>
    public string? WebhookToken { get; set; }
}

public sealed class TwilioOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.twilio.com";
    public string? AccountSid { get; set; }
    /// <summary>Secret. Also verifies the status-callback signature.</summary>
    public string? AuthToken { get; set; }
    /// <summary>Sender number, or use <see cref="MessagingServiceSid"/>.</summary>
    public string? From { get; set; }
    public string? MessagingServiceSid { get; set; }
    /// <summary>Public URL of /api/notifications/webhooks/sms/twilio, for delivery reports.</summary>
    public string? StatusCallbackUrl { get; set; }
}

/// <summary>Development only: "sends" by writing a line to the log (the number masked, never the text).</summary>
public sealed class LogSmsOptions
{
    public bool Enabled { get; set; }
}

public sealed class DispatcherOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>How often queued notifications are checked when nothing wakes the dispatcher.</summary>
    public int PollSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 20;
    public int Parallelism { get; set; } = 4;
    /// <summary>How long a claimed notification stays locked to one instance.</summary>
    public int LockMinutes { get; set; } = 3;
    /// <summary>WhatsApp / RCS / SMS messages accepted this long ago without any report become Expired.</summary>
    public int ExpireAcceptedAfterHours { get; set; } = 48;
}
