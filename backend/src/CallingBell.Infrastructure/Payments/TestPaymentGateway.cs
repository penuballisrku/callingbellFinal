using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CallingBell.Application.Common.Interfaces;

namespace CallingBell.Infrastructure.Payments;

/// <summary>
/// Simulated payments for development, used when <c>Payments:Test:Enabled</c> is true and no Razorpay keys are configured. The browser
/// shows its own test checkout (no money moves); everything after it (verify, subscription, invoice, notification) is the real flow.
/// The API refuses to start with it outside Development.
/// </summary>
public sealed class TestPaymentOptions
{
    public const string Section = "Payments:Test";
    public bool Enabled { get; set; }
}

/// <summary>
/// Order ids are <c>order_test_{32 hex}</c>; the test checkout pays one with <c>pay_test_{method}_{same 32 hex}</c>. Orders are kept in
/// memory, so an order created before an API restart can no longer be paid (start the checkout again).
/// </summary>
internal sealed partial class TestPaymentGateway : IPaymentGateway
{
    public const string GatewayName = "Test";
    public const string Signature = "test_signature";
    private const string OrderPrefix = "order_test_";
    private static readonly HashSet<string> Methods = ["upi", "card", "netbanking", "wallet"];
    private readonly ConcurrentDictionary<string, (long Amount, string Currency)> _orders = new();

    public string Name => GatewayName;
    public bool IsConfigured => true;
    public string? PublicKey => "test";

    public Task<GatewayOrder> CreateOrderAsync(string receipt, long amountInPaise, string currency, IReadOnlyDictionary<string, string> notes, CancellationToken ct)
    {
        var id = OrderPrefix + Guid.NewGuid().ToString("N");
        _orders[id] = (amountInPaise, currency);
        return Task.FromResult(new GatewayOrder(id, amountInPaise, currency, "created"));
    }

    public Task<GatewayPayment> GetPaymentAsync(string paymentId, CancellationToken ct)
    {
        if (PaymentId().Match(paymentId) is not { Success: true } m)
            return Task.FromResult(new GatewayPayment(paymentId, null, 0, "INR", "failed", null, "Not a test payment."));
        var orderId = OrderPrefix + m.Groups["order"].Value;
        return Task.FromResult(_orders.TryGetValue(orderId, out var o)
            ? new GatewayPayment(paymentId, orderId, o.Amount, o.Currency, "captured", m.Groups["method"].Value, null)
            : new GatewayPayment(paymentId, orderId, 0, "INR", "failed", null, "This test order has expired (the API restarted). Start the checkout again."));
    }

    public bool VerifyPaymentSignature(string gatewayOrderId, string paymentId, string signature) =>
        signature == Signature && PaymentId().Match(paymentId) is { Success: true } m && gatewayOrderId == OrderPrefix + m.Groups["order"].Value
        && Methods.Contains(m.Groups["method"].Value);

    /// <summary>The test checkout has no webhooks.</summary>
    public bool VerifyWebhookSignature(string body, string signature) => false;

    [GeneratedRegex("^pay_test_(?<method>[a-z]+)_(?<order>[0-9a-f]{32})$")]
    private static partial Regex PaymentId();
}
