using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CallingBell.Application.Common;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Notifications.Providers;

/// <summary>
/// The official WhatsApp Business Platform (Meta Cloud API): approved template messages only, since free-form messages can only be sent
/// inside a 24-hour customer-service window. Accepted means Meta took the message; whether it reached the phone arrives later on the
/// webhook (sent / delivered / read / failed).
/// </summary>
internal sealed partial class MetaWhatsAppClient(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<MetaWhatsAppClient> logger)
{
    public const string HttpClientName = "notify-whatsapp";
    private WhatsAppOptions O => options.Value.WhatsApp;

    public bool IsConfigured => O.Enabled && !string.IsNullOrWhiteSpace(O.AccessToken) && !string.IsNullOrWhiteSpace(O.PhoneNumberId);

    /// <param name="authentication">Authentication template: the code also goes to the copy-code button.</param>
    public async Task<NotificationResult> SendTemplateAsync(NotificationMessage m, bool authentication, CancellationToken ct)
    {
        if (m.Recipient.PhoneE164 is not { } phone) return NotificationResult.Unavailable("NO_PHONE", "No mobile number for WhatsApp.");
        if (string.IsNullOrWhiteSpace(m.TemplateName)) return NotificationResult.Unavailable("NO_TEMPLATE", "No WhatsApp template.");

        var count = authentication ? 1 : m.TemplateParameterCount;
        var values = m.TemplateParameters.Take(count).Select(Clean).ToList();
        while (values.Count < count) values.Add("-");
        var components = new List<object>();
        if (values.Count > 0) components.Add(new { type = "body", parameters = values.Select(v => new { type = "text", text = v }).ToList() });
        if (authentication && values.Count > 0)
            components.Add(new { type = "button", sub_type = "url", index = "0", parameters = new[] { new { type = "text", text = values[0] } } });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{O.BaseUrl.TrimEnd('/')}/{O.ApiVersion}/{O.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = phone.TrimStart('+'),
                type = "template",
                template = new { name = m.TemplateName, language = new { code = m.TemplateLanguage ?? "en" }, components },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.AccessToken);
        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            var id = ReadMessageId(body);
            return id is null ? NotificationResult.Failed("NO_MESSAGE_ID", "WhatsApp returned no message id.") : NotificationResult.Accepted(id, Phones.Mask(phone));
        }

        var (code, message) = ReadError(body, response.StatusCode);
        // Logged without the request (which holds the number, and for codes the code) or the token.
        logger.LogWarning("WhatsApp rejected a {Template} message: {Status} {Code} {Message}", m.TemplateName, (int)response.StatusCode, code, message);
        return code switch
        {
            // Token expired / invalid, or the app lacks permission: a configuration problem, not this recipient.
            "190" or "10" or "200" => NotificationResult.Failed(code, "WhatsApp rejected the access token."),
            // Template missing, not approved or wrong parameters.
            "132000" or "132001" or "132005" or "132007" or "132012" or "132015" or "132016" => NotificationResult.Failed(code, message),
            // Number not on WhatsApp / can't receive / not a valid number.
            "131026" or "131030" or "131009" or "100" => NotificationResult.Failed(code, message),
            _ => NotificationResult.Failed(code, message, retryable: IsTemporary(code, response.StatusCode)),
        };
    }

    private static bool IsTemporary(string code, HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.TooManyRequests
        || code is "1" or "2" or "4" or "80007" or "130429" or "131000" or "131016" or "131048" or "131056" or "133004";

    private static string? ReadMessageId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.GetArrayLength() > 0 && msgs[0].TryGetProperty("id", out var id)
                ? id.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static (string Code, string Message) ReadError(string body, HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var e = doc.RootElement.GetProperty("error");
            var code = e.TryGetProperty("code", out var c) ? c.ToString() : $"HTTP_{(int)status}";
            var message = e.TryGetProperty("error_data", out var data) && data.TryGetProperty("details", out var details)
                ? details.GetString() ?? "" : e.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
            return (code, message);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ($"HTTP_{(int)status}", "WhatsApp error");
        }
    }

    /// <summary>Template parameters may not contain new lines, tabs or more than four spaces in a row.</summary>
    private static string Clean(string value)
    {
        var text = Whitespace().Replace(value.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' '), " ").Trim();
        return text.Length == 0 ? "-" : text.Length > 1000 ? text[..1000] : text;
    }

    [GeneratedRegex(@"\s{2,}")] private static partial Regex Whitespace();
}

/// <summary>Leads and bookings on WhatsApp (utility templates such as callingbell_new_lead).</summary>
internal sealed class WhatsAppProvider(MetaWhatsAppClient client) : INotificationProvider
{
    public string ProviderName => "Meta";
    public string Channel => NotificationChannels.WhatsApp;
    public bool IsConfigured => client.IsConfigured;
    public Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken ct = default) => client.SendTemplateAsync(message, false, ct);
}

/// <summary>One-time codes on WhatsApp (an authentication template with a copy-code button, e.g. callingbell_otp).</summary>
internal sealed class WhatsAppAuthenticationProvider(MetaWhatsAppClient client) : INotificationProvider
{
    public string ProviderName => "Meta";
    public string Channel => NotificationChannels.WhatsAppAuthentication;
    public bool IsConfigured => client.IsConfigured;
    public Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken ct = default) => client.SendTemplateAsync(message, true, ct);
}
