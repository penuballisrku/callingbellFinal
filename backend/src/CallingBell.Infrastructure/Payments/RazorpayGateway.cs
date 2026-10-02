using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Payments;

/// <summary>
/// Razorpay credentials. Never commit real values: set them with user-secrets in development
/// (<c>dotnet user-secrets set "Payments:Razorpay:KeySecret" "..."</c>) and environment variables in production
/// (<c>Payments__Razorpay__KeySecret</c>). Test keys start with <c>rzp_test_</c>.
/// </summary>
public sealed class RazorpayOptions
{
    public const string Section = "Payments:Razorpay";
    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.razorpay.com/v1/";
}

/// <summary>Razorpay Orders + Payments API (https://razorpay.com/docs/api/).</summary>
internal sealed class RazorpayGateway(HttpClient http, IOptions<RazorpayOptions> options, ILogger<RazorpayGateway> logger) : IPaymentGateway
{
    private readonly RazorpayOptions _options = options.Value;

    public string Name => "Razorpay";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.KeyId) && !string.IsNullOrWhiteSpace(_options.KeySecret);
    public string? PublicKey => IsConfigured ? _options.KeyId : null;

    public async Task<GatewayOrder> CreateOrderAsync(string receipt, long amountInPaise, string currency, IReadOnlyDictionary<string, string> notes, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "orders");
        // Buffered JSON (explicit Content-Length) rather than a chunked stream.
        request.Content = new StringContent(JsonSerializer.Serialize(new { amount = amountInPaise, currency, receipt, notes }), Encoding.UTF8, "application/json");
        var order = await SendAsync<OrderResponse>(request, "create the payment order", ct);
        return new GatewayOrder(order.Id, order.Amount, order.Currency, order.Status);
    }

    public async Task<GatewayPayment> GetPaymentAsync(string paymentId, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}");
        var p = await SendAsync<PaymentResponse>(request, "confirm the payment", ct);
        return new GatewayPayment(p.Id, p.OrderId, p.Amount, p.Currency, p.Status, p.Method, p.ErrorDescription);
    }

    public bool VerifyPaymentSignature(string gatewayOrderId, string paymentId, string signature) =>
        IsConfigured && Matches($"{gatewayOrderId}|{paymentId}", _options.KeySecret, signature);

    public bool VerifyWebhookSignature(string body, string signature) =>
        !string.IsNullOrWhiteSpace(_options.WebhookSecret) && Matches(body, _options.WebhookSecret, signature);

    private static bool Matches(string payload, string secret, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        if (!IsConfigured) throw new BadRequestException("Online payments are not available right now. Please try again later.");
        var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}")));
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, string action, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Razorpay request failed: {Action}", action);
            throw new BadRequestException($"We couldn't reach the payment provider to {action}. Please try again.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new BadRequestException($"Unexpected response while trying to {action}.");

            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(ct).ConfigureAwait(false);
            logger.LogWarning("Razorpay {Status} while trying to {Action}: {Code} {Description}", (int)response.StatusCode, action, error?.Error?.Code, error?.Error?.Description);
            throw new BadRequestException((int)response.StatusCode == 401
                ? "Online payments are misconfigured. Please contact support."
                : $"The payment provider couldn't {action}: {error?.Error?.Description ?? response.ReasonPhrase}");
        }
    }

    private sealed record OrderResponse([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("currency")] string Currency, [property: JsonPropertyName("status")] string Status);

    private sealed record PaymentResponse([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("order_id")] string? OrderId,
        [property: JsonPropertyName("amount")] long Amount, [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("status")] string Status, [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record ErrorResponse([property: JsonPropertyName("error")] ErrorBody? Error);
    private sealed record ErrorBody([property: JsonPropertyName("code")] string? Code, [property: JsonPropertyName("description")] string? Description);
}
