using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallingBell.Infrastructure.Notifications.Providers;

/// <summary>
/// Web push through Firebase Cloud Messaging (HTTP v1 API) to every browser the user registered. Sent as a data message that the site's
/// service worker (/firebase-messaging-sw.js) shows, so clicks open the right page and the browser can acknowledge delivery. Accepted when
/// FCM takes it for at least one browser; unavailable when the user has none. Tokens FCM reports as gone are turned off.
/// </summary>
internal sealed class FcmWebPushProvider(ApplicationDbContext db, IHttpClientFactory http, GoogleServiceAccountTokens tokens,
    IDeliveryAckSigner signer, IOptions<NotificationOptions> options, ILogger<FcmWebPushProvider> logger) : INotificationProvider
{
    public const string HttpClientName = "notify-fcm";
    private FcmOptions O => options.Value.Fcm;

    public string ProviderName => "Fcm";
    public string Channel => NotificationChannels.WebPush;
    public bool IsConfigured => O.Enabled && tokens.Account is not null && ProjectId is not null;
    private string? ProjectId => string.IsNullOrWhiteSpace(O.ProjectId) ? tokens.Account?.ProjectId : O.ProjectId;

    public async Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        if (message.Recipient.UserId is not { } userId) return NotificationResult.Unavailable("NO_USER", "Web push needs a signed-in user.");
        var devices = await db.NotificationDevices.AsNoTracking().Where(d => d.UserId == userId && d.IsActive)
            .OrderByDescending(d => d.UpdatedAt).Select(d => new { d.Id, d.Token }).Take(10).ToListAsync(ct);
        if (devices.Count == 0) return NotificationResult.Unavailable("NO_DEVICE", "No browser registered for push notifications.");

        var accessToken = await tokens.GetAccessTokenAsync(ct);
        var client = http.CreateClient(HttpClientName);
        var url = $"{O.BaseUrl.TrimEnd('/')}/v1/projects/{ProjectId}/messages:send";
        var data = Payload(message);

        string? firstId = null;
        NotificationResult? lastFailure = null;
        var accepted = 0;
        foreach (var device in devices)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    message = new
                    {
                        token = device.Token,
                        data,
                        webpush = new { headers = new Dictionary<string, string> { ["Urgency"] = "high", ["TTL"] = O.TimeToLiveSeconds.ToString() } },
                    },
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var sent = await response.Content.ReadFromJsonAsync<FcmSendResponse>(ct);
                firstId ??= sent?.Name;
                accepted++;
                await db.NotificationDevices.Where(d => d.Id == device.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastUsedAt, DateTimeOffset.UtcNow).SetProperty(d => d.FailureCount, 0), ct);
                continue;
            }

            var error = await ReadErrorAsync(response, ct);
            if (IsGoneToken(response.StatusCode, error))
            {
                await db.NotificationDevices.Where(d => d.Id == device.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.IsActive, false).SetProperty(d => d.DeactivatedReason, error.Code)
                    .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), ct);
                lastFailure = NotificationResult.Failed(error.Code, "The browser's push registration is no longer valid.");
                continue;
            }
            // Credentials or project wrong: every device would fail the same way.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && error.Code != "SENDER_ID_MISMATCH")
            {
                logger.LogError("FCM rejected the credentials: {Status} {Code}", (int)response.StatusCode, error.Code);
                return NotificationResult.Failed(error.Code, "Firebase rejected the server credentials.");
            }
            var retryable = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
            await db.NotificationDevices.Where(d => d.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.FailureCount, d => d.FailureCount + 1), ct);
            lastFailure = NotificationResult.Failed(error.Code, error.Message, retryable && accepted == 0);
        }

        if (accepted > 0) return NotificationResult.Accepted(firstId, $"{accepted} browser{(accepted == 1 ? "" : "s")}");
        return lastFailure ?? NotificationResult.Failed("FCM_FAILED", "No browser accepted the push.");
    }

    /// <summary>Everything as strings (FCM data messages); the service worker builds the notification from it.</summary>
    private Dictionary<string, string> Payload(NotificationMessage m)
    {
        var data = new Dictionary<string, string>(m.Data)
        {
            ["title"] = m.Title,
            ["body"] = m.Body,
            ["url"] = m.Url ?? "/",
            ["icon"] = O.IconUrl,
            ["tag"] = m.Data.TryGetValue("leadId", out var lead) ? $"lead-{lead}" : m.Data.TryGetValue("bookingId", out var booking) ? $"booking-{booking}" : m.DeliveryId.ToString("N"),
            ["deliveryId"] = m.DeliveryId.ToString(),
            ["ack"] = signer.Sign(m.DeliveryId),
        };
        return data;
    }

    private static bool IsGoneToken(HttpStatusCode status, FcmError e) =>
        e.Code is "UNREGISTERED" or "SENDER_ID_MISMATCH"
        || status == HttpStatusCode.NotFound
        || status == HttpStatusCode.BadRequest && e.Code == "INVALID_ARGUMENT" && e.Message.Contains("registration token", StringComparison.OrdinalIgnoreCase);

    private static async Task<FcmError> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var error = doc.RootElement.GetProperty("error");
            var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            var code = error.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                foreach (var d in details.EnumerateArray())
                    if (d.TryGetProperty("errorCode", out var ec) && ec.GetString() is { Length: > 0 } fcmCode) code = fcmCode;
            return new FcmError(string.IsNullOrEmpty(code) ? $"HTTP_{(int)response.StatusCode}" : code, message);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new FcmError($"HTTP_{(int)response.StatusCode}", response.ReasonPhrase ?? "FCM error");
        }
    }

    private sealed record FcmSendResponse(string? Name);
    private sealed record FcmError(string Code, string Message);
}

/// <summary>
/// OAuth access tokens for Google APIs from the Firebase service account (a JWT signed with its private key, exchanged at Google's token
/// endpoint), cached until shortly before they expire. The key never leaves the server.
/// </summary>
public sealed class GoogleServiceAccountTokens(IHttpClientFactory http, IOptions<NotificationOptions> options, ILogger<GoogleServiceAccountTokens> logger)
{
    public const string HttpClientName = "notify-google-oauth";
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;
    private readonly Lazy<ServiceAccount?> _account = new(() => Load(options.Value.Fcm, logger));

    public ServiceAccount? Account => _account.Value;

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expires) return _token;
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expires) return _token;
            var account = Account ?? throw new InvalidOperationException("The Firebase service account is not configured.");
            var now = DateTimeOffset.UtcNow;
            var assertion = SignJwt(account, now);
            using var response = await http.CreateClient(HttpClientName).PostAsync(account.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }), ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Google token endpoint returned {(int)response.StatusCode}.", null, response.StatusCode);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct) ?? throw new HttpRequestException("Empty token response.");
            _token = token.access_token;
            _expires = now.AddSeconds(Math.Max(60, token.expires_in - 300));
            return _token;
        }
        finally { _lock.Release(); }
    }

    private static string SignJwt(ServiceAccount a, DateTimeOffset now)
    {
        var header = Base64UrlEncoder.Encode("""{"alg":"RS256","typ":"JWT"}""");
        var claims = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = a.ClientEmail, ["scope"] = Scope, ["aud"] = a.TokenUri,
            ["iat"] = now.ToUnixTimeSeconds(), ["exp"] = now.AddMinutes(60).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(a.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{Base64UrlEncoder.Encode(signature)}";
    }

    private static ServiceAccount? Load(FcmOptions o, ILogger logger)
    {
        if (!o.Enabled) return null;
        try
        {
            var json = o.ServiceAccountJson?.Trim();
            if (string.IsNullOrEmpty(json) && !string.IsNullOrWhiteSpace(o.ServiceAccountFile) && File.Exists(o.ServiceAccountFile))
                json = File.ReadAllText(o.ServiceAccountFile);
            if (string.IsNullOrEmpty(json)) { logger.LogWarning("Web push is enabled but no Firebase service account is configured"); return null; }
            if (!json.StartsWith('{')) json = Encoding.UTF8.GetString(Convert.FromBase64String(json));
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new ServiceAccount(r.GetProperty("client_email").GetString()!, r.GetProperty("private_key").GetString()!,
                r.TryGetProperty("project_id", out var p) ? p.GetString() : null,
                r.TryGetProperty("token_uri", out var t) ? t.GetString() ?? "https://oauth2.googleapis.com/token" : "https://oauth2.googleapis.com/token");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or IOException)
        {
            // Never log the key itself.
            logger.LogError("The Firebase service account could not be read ({Error}); web push is off", ex.GetType().Name);
            return null;
        }
    }

    public sealed record ServiceAccount(string ClientEmail, string PrivateKey, string? ProjectId, string TokenUri);
    // ReSharper disable InconsistentNaming
    private sealed record TokenResponse(string access_token, int expires_in);
}
