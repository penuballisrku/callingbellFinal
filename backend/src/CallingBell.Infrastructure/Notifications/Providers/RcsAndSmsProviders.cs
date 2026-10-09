using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CallingBell.Application.Common;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Notifications.Providers;

internal static class ProviderHttp
{
    public const string RcsClient = "notify-rcs";
    public const string SmsClient = "notify-sms";

    public static bool IsTemporary(HttpStatusCode status) => (int)status >= 500 || status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout;

    public static string? ReadString(string body, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Find(doc.RootElement, property);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The first property of that name anywhere in the JSON (vendors nest the id differently).</summary>
    private static string? Find(JsonElement e, string property)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                        return p.Value.ToString();
                    if (Find(p.Value, property) is { } nested) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (Find(item, property) is { } found) return found;
                break;
        }
        return null;
    }
}

/// <summary>
/// RCS business messaging through any vendor exposing a JSON send endpoint (configured base URL, path and bearer credential). Unavailable
/// when not configured, so the route simply moves on to SMS.
/// </summary>
internal sealed class RcsProvider(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<RcsProvider> logger) : INotificationProvider
{
    private RcsOptions O => options.Value.Rcs;

    public string ProviderName => "Rcs";
    public string Channel => NotificationChannels.Rcs;
    public bool IsConfigured => O.Enabled && !string.IsNullOrWhiteSpace(O.BaseUrl) && !string.IsNullOrWhiteSpace(O.AgentId)
                                && !string.IsNullOrWhiteSpace(O.Credentials);

    public async Task<NotificationResult> SendAsync(NotificationMessage m, CancellationToken ct = default)
    {
        if (m.Recipient.PhoneE164 is not { } phone) return NotificationResult.Unavailable("NO_PHONE", "No mobile number for RCS.");
        using var request = new HttpRequestMessage(HttpMethod.Post, O.BaseUrl!.TrimEnd('/') + "/" + O.SendPath.TrimStart('/'))
        {
            Content = JsonContent.Create(new
            {
                agentId = O.AgentId,
                to = phone,
                messageId = m.DeliveryId.ToString(),
                templateName = m.TemplateName,
                parameters = m.TemplateParameters,
                text = m.RenderedText ?? m.Body,
                suggestions = m.Url is null ? null : new[] { new { type = "openUrl", text = "Open", url = m.TemplateParameters.LastOrDefault(p => p.StartsWith("http")) ?? m.Url } },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.Credentials);
        using var response = await http.CreateClient(ProviderHttp.RcsClient).SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
            return NotificationResult.Accepted(ProviderHttp.ReadString(body, O.MessageIdField) ?? m.DeliveryId.ToString(), Phones.Mask(phone));

        logger.LogWarning("RCS gateway rejected a message: {Status}", (int)response.StatusCode);
        // Many RCS gateways answer 404/422 for a handset without RCS: that's "can't use this channel", not an outage.
        return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity
            ? NotificationResult.Unavailable($"HTTP_{(int)response.StatusCode}", "The handset can't receive RCS.")
            : NotificationResult.Failed($"HTTP_{(int)response.StatusCode}", "The RCS gateway rejected the message.", ProviderHttp.IsTemporary(response.StatusCode));
    }
}

/// <summary>
/// MSG91 (India): the Flow API sends a DLT-approved template (dbo.NotificationTemplates.TemplateName = MSG91 template id) with its
/// variables {{1}}, {{2}}, … as var1, var2, …. The request id it returns is matched by delivery reports.
/// </summary>
internal sealed class Msg91SmsProvider(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<Msg91SmsProvider> logger) : INotificationProvider
{
    private Msg91Options O => options.Value.Sms.Msg91;

    public string ProviderName => "Msg91";
    public string Channel => NotificationChannels.Sms;
    public bool IsConfigured => O.Enabled && !string.IsNullOrWhiteSpace(O.AuthKey);

    public async Task<NotificationResult> SendAsync(NotificationMessage m, CancellationToken ct = default)
    {
        if (m.Recipient.PhoneE164 is not { } phone) return NotificationResult.Unavailable("NO_PHONE", "No mobile number for SMS.");
        if (string.IsNullOrWhiteSpace(m.TemplateName) || m.TemplateName.StartsWith("DLT_TEMPLATE_ID", StringComparison.Ordinal))
            return NotificationResult.Unavailable("NO_TEMPLATE", "No MSG91 template id configured for this message.");

        var recipient = new Dictionary<string, string> { ["mobiles"] = phone.TrimStart('+') };
        for (var i = 0; i < m.TemplateParameters.Count; i++) recipient[$"var{i + 1}"] = m.TemplateParameters[i];
        var payload = new Dictionary<string, object> { ["template_id"] = m.TemplateName!, ["short_url"] = "0", ["recipients"] = new[] { recipient } };
        if (!string.IsNullOrWhiteSpace(O.SenderId)) payload["sender"] = O.SenderId!;
        if (!string.IsNullOrWhiteSpace(O.DltEntityId)) payload["DLT_TE_ID"] = O.DltEntityId!;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{O.BaseUrl.TrimEnd('/')}/api/v5/flow") { Content = JsonContent.Create(payload) };
        request.Headers.Add("authkey", O.AuthKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.CreateClient(ProviderHttp.SmsClient).SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var type = ProviderHttp.ReadString(body, "type");
        if (response.IsSuccessStatusCode && string.Equals(type, "success", StringComparison.OrdinalIgnoreCase))
            return NotificationResult.Accepted(ProviderHttp.ReadString(body, "message"), Phones.Mask(phone));

        var reason = ProviderHttp.ReadString(body, "message") ?? $"HTTP {(int)response.StatusCode}";
        logger.LogWarning("MSG91 rejected an SMS: {Status} {Reason}", (int)response.StatusCode, reason);
        return NotificationResult.Failed($"MSG91_{(int)response.StatusCode}", reason, ProviderHttp.IsTemporary(response.StatusCode));
    }
}

/// <summary>Twilio Programmable Messaging (outside India, or wherever it is configured): plain text from the template.</summary>
internal sealed class TwilioSmsProvider(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<TwilioSmsProvider> logger) : INotificationProvider
{
    private TwilioOptions O => options.Value.Sms.Twilio;

    public string ProviderName => "Twilio";
    public string Channel => NotificationChannels.Sms;
    public bool IsConfigured => O.Enabled && !string.IsNullOrWhiteSpace(O.AccountSid) && !string.IsNullOrWhiteSpace(O.AuthToken)
                                && (!string.IsNullOrWhiteSpace(O.From) || !string.IsNullOrWhiteSpace(O.MessagingServiceSid));

    public async Task<NotificationResult> SendAsync(NotificationMessage m, CancellationToken ct = default)
    {
        if (m.Recipient.PhoneE164 is not { } phone) return NotificationResult.Unavailable("NO_PHONE", "No mobile number for SMS.");
        if (string.IsNullOrWhiteSpace(m.RenderedText)) return NotificationResult.Unavailable("NO_TEMPLATE", "No SMS text.");

        var form = new Dictionary<string, string> { ["To"] = phone, ["Body"] = m.RenderedText };
        if (!string.IsNullOrWhiteSpace(O.MessagingServiceSid)) form["MessagingServiceSid"] = O.MessagingServiceSid!;
        else form["From"] = O.From!;
        if (!string.IsNullOrWhiteSpace(O.StatusCallbackUrl)) form["StatusCallback"] = O.StatusCallbackUrl!;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{O.BaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{O.AccountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{O.AccountSid}:{O.AuthToken}")));
        using var response = await http.CreateClient(ProviderHttp.SmsClient).SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode) return NotificationResult.Accepted(ProviderHttp.ReadString(body, "sid"), Phones.Mask(phone));

        var code = ProviderHttp.ReadString(body, "code") ?? $"HTTP_{(int)response.StatusCode}";
        logger.LogWarning("Twilio rejected an SMS: {Status} {Code}", (int)response.StatusCode, code);
        // 21211 invalid number, 21610 opted out, 21408 region not enabled: this recipient can't get SMS from us.
        return code is "21211" or "21610" or "21408" or "21614"
            ? NotificationResult.Failed($"TWILIO_{code}", ProviderHttp.ReadString(body, "message") ?? "Rejected")
            : NotificationResult.Failed($"TWILIO_{code}", ProviderHttp.ReadString(body, "message") ?? "Rejected", ProviderHttp.IsTemporary(response.StatusCode));
    }
}

/// <summary>
/// Development stand-in for an SMS gateway: accepts the message and logs that it would have been sent, with the number masked and never
/// the text (which may hold a code). Off unless Notifications:Sms:Log:Enabled is true.
/// </summary>
internal sealed class LogSmsProvider(IOptions<NotificationOptions> options, ILogger<LogSmsProvider> logger) : INotificationProvider
{
    public string ProviderName => "Log";
    public string Channel => NotificationChannels.Sms;
    public bool IsConfigured => options.Value.Sms.Log.Enabled;

    public Task<NotificationResult> SendAsync(NotificationMessage m, CancellationToken ct = default)
    {
        if (m.Recipient.PhoneE164 is not { } phone) return Task.FromResult(NotificationResult.Unavailable("NO_PHONE", "No mobile number for SMS."));
        logger.LogWarning("SMS ({Route}) to {Phone} not sent: no SMS gateway is configured (development log provider)", m.RouteCode, Phones.Mask(phone));
        return Task.FromResult(NotificationResult.Accepted($"log-{m.DeliveryId:N}", Phones.Mask(phone)));
    }
}
