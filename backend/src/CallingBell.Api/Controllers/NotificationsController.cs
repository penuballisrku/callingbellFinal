using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CallingBell.Api.Controllers;

/// <summary>Browser registration for web push, the browser's delivery acknowledgements, and the web push settings it needs.</summary>
[Route("api/notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    public sealed record DeviceTokenRequest(string Token);
    public sealed record AckRequest(string Signature, string Event);

    /// <summary>Registers (or refreshes) this browser's FCM token for the signed-in user. The same token again never creates a duplicate.</summary>
    [Authorize, HttpPost("devices"), EnableRateLimiting("submissions")]
    public async Task<ActionResult<ApiResponse<object>>> RegisterDevice(RegisterNotificationDeviceCommand command, CancellationToken ct)
    {
        await Sender.Send(command, ct);
        return Done("Notifications turned on for this browser");
    }

    /// <summary>Stops push notifications to this browser (sign-out, or turned off).</summary>
    [Authorize, HttpPost("devices/unregister")]
    public async Task<ActionResult<ApiResponse<object>>> UnregisterDevice(DeviceTokenRequest request, CancellationToken ct)
    {
        await Sender.Send(new UnregisterNotificationDeviceCommand(request.Token), ct);
        return Done("Notifications turned off for this browser");
    }

    /// <summary>
    /// The Firebase web app settings and VAPID public key the browser needs to get a push token (public identifiers; server credentials
    /// never leave the API). <c>data</c> is null when web push isn't configured.
    /// </summary>
    [AllowAnonymous, HttpGet("web-push/config")]
    public async Task<ActionResult<ApiResponse<WebPushClientConfigDto?>>> WebPushConfig(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Success(await Sender.Send(new GetWebPushConfigQuery(), ct));
    }

    /// <summary>The service worker reports a push it showed ("received") or the user opened ("clicked"); signed per delivery.</summary>
    [AllowAnonymous, HttpPost("deliveries/{id:guid}/ack"), EnableRateLimiting("public-search")]
    public async Task<IActionResult> Acknowledge(Guid id, AckRequest request, CancellationToken ct)
    {
        await Sender.Send(new AcknowledgePushCommand(id, request.Signature, request.Event), ct);
        return NoContent();
    }
}

/// <summary>
/// Delivery reports from providers. Each request is authenticated (Meta's signature, Twilio's signature, or a secret token in the URL)
/// and only updates the status of messages the platform sent; nothing else can be changed through them.
/// </summary>
[Route("api/notifications/webhooks"), AllowAnonymous, DisableRateLimiting, ApiExplorerSettings(IgnoreApi = true)]
public sealed class NotificationWebhooksController(DeliveryStatusService statuses, IOptions<NotificationOptions> options,
    ILogger<NotificationWebhooksController> logger) : ControllerBase
{
    private NotificationOptions O => options.Value;

    /// <summary>Meta's subscription check: echoes hub.challenge when hub.verify_token matches.</summary>
    [HttpGet("whatsapp")]
    public IActionResult VerifyWhatsApp([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = O.WhatsApp.WebhookVerifyToken;
        if (mode == "subscribe" && !string.IsNullOrEmpty(expected) && token is not null && FixedEquals(token, expected))
            return Content(challenge ?? "", "text/plain");
        return Forbid();
    }

    /// <summary>WhatsApp message statuses (sent, delivered, read, failed), verified with X-Hub-Signature-256.</summary>
    [HttpPost("whatsapp")]
    public async Task<IActionResult> WhatsApp(CancellationToken ct)
    {
        var body = await ReadBodyAsync();
        var secret = O.WhatsApp.AppSecret;
        var header = Request.Headers["X-Hub-Signature-256"].ToString();
        if (string.IsNullOrEmpty(secret) || !header.StartsWith("sha256=", StringComparison.Ordinal)
            || !FixedEquals(header[7..], Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant()))
            return Unauthorized();

        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var entry in Array(doc.RootElement, "entry"))
            foreach (var change in Array(entry, "changes"))
            {
                if (!change.TryGetProperty("value", out var value)) continue;
                foreach (var s in Array(value, "statuses"))
                {
                    var id = s.TryGetProperty("id", out var i) ? i.GetString() : null;
                    var status = (s.TryGetProperty("status", out var st) ? st.GetString() : null) switch
                    {
                        "sent" => DeliveryStatuses.Sent,
                        "delivered" => DeliveryStatuses.Delivered,
                        "read" => DeliveryStatuses.Read,
                        "failed" => DeliveryStatuses.Failed,
                        _ => null,
                    };
                    if (id is null || status is null) continue;
                    DateTimeOffset? at = s.TryGetProperty("timestamp", out var ts) && long.TryParse(ts.GetString(), out var unix)
                        ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;
                    var error = Array(s, "errors").FirstOrDefault();
                    string? code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var c) ? c.ToString() : null;
                    string? title = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("title", out var t) ? t.GetString() : null;
                    await statuses.ApplyAsync(new DeliveryStatusReport("Meta", id, status, at, code, title), ct);
                }
            }
        }
        catch (JsonException)
        {
            logger.LogWarning("WhatsApp webhook: unreadable body");
            return BadRequest();
        }
        // Meta retries anything but 200.
        return Ok();
    }

    /// <summary>SMS delivery reports: Twilio (signed with the auth token) or MSG91 (?token= must match the configured secret).</summary>
    [HttpPost("sms/{provider}")]
    public async Task<IActionResult> Sms(string provider, [FromQuery] string? token, CancellationToken ct)
    {
        switch (provider.ToLowerInvariant())
        {
            case "twilio":
            {
                if (!Request.HasFormContentType) return BadRequest();
                var form = await Request.ReadFormAsync(ct);
                if (!ValidTwilioSignature(form)) return Unauthorized();
                var status = form["MessageStatus"].ToString() switch
                {
                    "sent" => DeliveryStatuses.Sent,
                    "delivered" => DeliveryStatuses.Delivered,
                    "read" => DeliveryStatuses.Read,
                    "undelivered" or "failed" => DeliveryStatuses.Failed,
                    _ => null,
                };
                if (status is not null)
                    await statuses.ApplyAsync(new DeliveryStatusReport("Twilio", form["MessageSid"].ToString(), status, null,
                        form["ErrorCode"].ToString() is { Length: > 0 } e ? $"TWILIO_{e}" : null), ct);
                return Ok();
            }
            case "msg91":
            {
                if (!TokenMatches(token, O.Sms.Msg91.WebhookToken)) return Unauthorized();
                var body = await ReadBodyAsync();
                foreach (var (id, status, code) in Msg91Reports(body))
                    await statuses.ApplyAsync(new DeliveryStatusReport("Msg91", id, status, null, code), ct);
                return Ok();
            }
            default:
                return NotFound();
        }
    }

    /// <summary>RCS delivery reports from the configured gateway: JSON { messageId, status } with ?token=.</summary>
    [HttpPost("rcs")]
    public async Task<IActionResult> Rcs([FromQuery] string? token, CancellationToken ct)
    {
        if (!TokenMatches(token, O.Rcs.WebhookToken)) return Unauthorized();
        try
        {
            using var doc = JsonDocument.Parse(await ReadBodyAsync());
            var reports = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var r in reports)
            {
                var id = Str(r, O.Rcs.MessageIdField) ?? Str(r, "messageId");
                var status = MapGeneric(Str(r, "status"));
                if (id is not null && status is not null)
                    await statuses.ApplyAsync(new DeliveryStatusReport("Rcs", id, status, null, Str(r, "errorCode")), ct);
            }
        }
        catch (JsonException) { return BadRequest(); }
        return Ok();
    }

    // ---------- helpers ----------

    private async Task<byte[]> ReadBodyAsync()
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Twilio signs the full callback URL followed by each form field (sorted by name) as name+value, with HMAC-SHA1 of the auth token.
    /// The configured StatusCallbackUrl is used as the URL, since a proxy may change the scheme or host the app sees.
    /// </summary>
    private bool ValidTwilioSignature(IFormCollection form)
    {
        var authToken = O.Sms.Twilio.AuthToken;
        var url = O.Sms.Twilio.StatusCallbackUrl;
        var header = Request.Headers["X-Twilio-Signature"].ToString();
        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(header)) return false;
        var data = new StringBuilder(url);
        foreach (var key in form.Keys.OrderBy(k => k, StringComparer.Ordinal)) data.Append(key).Append(form[key].ToString());
        var expected = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString())));
        return FixedEquals(header, expected);
    }

    /// <summary>MSG91 reports (a single object or a list): request id plus a status or description.</summary>
    private static IEnumerable<(string Id, string Status, string? Code)> Msg91Reports(byte[] body)
    {
        var results = new List<(string, string, string?)>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList()
                : doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList()
                : [doc.RootElement];
            foreach (var item in items)
            {
                var id = Str(item, "requestId") ?? Str(item, "request_id") ?? Str(item, "reqId");
                var status = MapGeneric(Str(item, "status") ?? Str(item, "desc") ?? Str(item, "event"));
                if (id is not null && status is not null) results.Add((id, status, Str(item, "failureReason") ?? Str(item, "code")));
            }
        }
        catch (JsonException) { /* ignored: nothing to apply */ }
        return results;
    }

    private static string? MapGeneric(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "sent" or "submitted" => DeliveryStatuses.Sent,
        "delivered" or "1" or "delivrd" => DeliveryStatuses.Delivered,
        "read" or "seen" => DeliveryStatuses.Read,
        "failed" or "undelivered" or "rejected" or "2" or "16" or "expired" or "undeliv" => DeliveryStatuses.Failed,
        _ => null,
    };

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString() : null;

    private static bool TokenMatches(string? token, string? expected) =>
        !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(token) && FixedEquals(token, expected);

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
