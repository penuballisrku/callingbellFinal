namespace CallingBell.Application.Features.Notifications.Delivery;

/// <summary>
/// Sends over one channel through one provider (FCM, Meta WhatsApp, an RCS vendor, an SMS gateway). Business logic never talks to a
/// provider directly: it creates a notification (or an OTP) and the <see cref="NotificationRouter"/> picks the channels and providers.
/// </summary>
public interface INotificationProvider
{
    /// <summary>Matches dbo.NotificationProviders.ProviderName, e.g. "Fcm", "Meta", "Msg91".</summary>
    string ProviderName { get; }
    /// <summary>One of <see cref="Domain.Constants.NotificationChannels"/>.</summary>
    string Channel { get; }
    /// <summary>False when its settings or credentials are missing: the router skips it (the channel counts as unavailable).</summary>
    bool IsConfigured { get; }

    Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Who receives it: the user (for their registered browsers) and/or a mobile number (WhatsApp, RCS, SMS).</summary>
/// <param name="PhoneE164">E.164, e.g. "+919876543210".</param>
/// <param name="CountryCode">ISO country, to pick providers for that country (e.g. the DLT-registered SMS gateway in India).</param>
public sealed record NotificationRecipient(string? UserId, string? PhoneE164, string? CountryCode, string LanguageCode = "en");

/// <summary>
/// What to send. The router fills <see cref="Channel"/>, <see cref="DeliveryId"/> and the template for each attempt; providers use what
/// their channel needs: web push the title, body, URL and data; WhatsApp the template name and parameters; SMS and RCS the rendered text.
/// </summary>
public sealed record NotificationMessage
{
    public required string RouteCode { get; init; }
    public required NotificationRecipient Recipient { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    /// <summary>Path in the app ("/owner/leads?lead=…") or an absolute URL.</summary>
    public string? Url { get; init; }
    /// <summary>Extra key/values for web push (type, leadId, …).</summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
    /// <summary>Values for the template's {{1}}, {{2}}, … in order.</summary>
    public IReadOnlyList<string> TemplateParameters { get; init; } = [];
    /// <summary>A one-time code: never logged; WhatsApp sends it with the authentication template's copy-code button.</summary>
    public bool IsSensitive { get; init; }

    public string Channel { get; init; } = string.Empty;
    public Guid DeliveryId { get; init; }
    /// <summary>WhatsApp: the approved template's name. SMS: the gateway/DLT template id.</summary>
    public string? TemplateName { get; init; }
    public string? TemplateLanguage { get; init; }
    /// <summary>The template's text with the parameters filled in (SMS, RCS).</summary>
    public string? RenderedText { get; init; }
    /// <summary>How many parameters the template takes (its highest {{n}}); WhatsApp must be sent exactly that many.</summary>
    public int TemplateParameterCount { get; init; }
}

public enum NotificationOutcome
{
    /// <summary>The provider took the message. Not proof the person received it.</summary>
    Accepted,
    /// <summary>The channel can't be used for this recipient (no device, no number, not configured): try the next.</summary>
    Unavailable,
    Failed,
}

public sealed record NotificationResult(NotificationOutcome Outcome, string? ProviderMessageId = null, string? ErrorCode = null,
    string? ErrorMessage = null, bool Retryable = false, string? RecipientLabel = null)
{
    public static NotificationResult Accepted(string? providerMessageId, string? recipientLabel = null) =>
        new(NotificationOutcome.Accepted, providerMessageId, RecipientLabel: recipientLabel);
    public static NotificationResult Unavailable(string code, string message) => new(NotificationOutcome.Unavailable, null, code, message);
    public static NotificationResult Failed(string code, string message, bool retryable = false) =>
        new(NotificationOutcome.Failed, null, code, message, retryable);
}

/// <summary>How a route went: the attempt that succeeded (if any) and every attempt made.</summary>
/// <param name="Step">Route position of the successful channel, or of the last one tried.</param>
public sealed record NotificationDeliveryResult(bool Success, string? Channel, string? Provider, Guid? DeliveryId, int Step,
    IReadOnlyList<DeliveryAttempt> Attempts);

public sealed record DeliveryAttempt(int Step, string Channel, string Provider, string Status, string? ErrorCode);

/// <summary>What the delivery belongs to: a notification, or an OTP code.</summary>
public sealed record DeliveryOwner(Guid? NotificationId, Guid? OtpCodeId);

/// <summary>Wakes the background dispatcher when a notification is queued, so it goes out at once rather than at the next poll.</summary>
public interface INotificationDispatchSignal
{
    void Notify();
}
