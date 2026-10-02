using System.Text.Json;
using System.Text.RegularExpressions;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Owner;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CallingBell.Application.Features.Payments;

// ===================== Contracts =====================

public sealed record PaymentConfigDto(bool Enabled, string Gateway, string? KeyId, decimal GstRatePercent);

public sealed record CheckoutPrefillDto(string Name, string Email, string Contact);

/// <summary>Everything the browser needs to open the gateway checkout for an order.</summary>
public sealed record CheckoutOrderDto(Guid OrderId, string OrderNumber, string Gateway, string KeyId, string GatewayOrderId, long AmountInPaise,
    string Currency, string PlanCode, string PlanName, string BillingCycle, decimal Subtotal, decimal Tax, decimal Total, string BusinessName,
    CheckoutPrefillDto Prefill);

public sealed record PaymentResultDto(Guid OrderId, string OrderNumber, string Status, string PlanName, string BillingCycle, DateTime? ActiveFrom,
    DateTime? ActiveUntil, string? InvoiceNumber, decimal Total, string? PaymentMethod, string? FailureReason);

public sealed record GetPaymentConfigQuery : IRequest<PaymentConfigDto>;

public sealed record CreatePlanOrderCommand(Guid BusinessId, string PlanCode, string BillingCycle, string? Gstin) : IRequest<CheckoutOrderDto>;

public sealed record VerifyPlanPaymentCommand(Guid BusinessId, Guid OrderId, string GatewayOrderId, string GatewayPaymentId, string Signature)
    : IRequest<PaymentResultDto>;

/// <summary>Records that the customer closed the checkout or the payment failed in the browser.</summary>
public sealed record RecordCheckoutOutcomeCommand(Guid BusinessId, Guid OrderId, string Outcome, string? Reason) : IRequest<PaymentResultDto>;

public sealed record HandlePaymentWebhookCommand(string Body, string? Signature) : IRequest<bool>;

internal static class Pricing
{
    public const decimal GstRate = 0.18m;
    public static (decimal Subtotal, decimal Tax, decimal Total) For(SubscriptionPlan plan, string cycle)
    {
        var subtotal = cycle == "Annual" ? plan.AnnualPrice : plan.MonthlyPrice;
        var tax = Math.Round(subtotal * GstRate, 2, MidpointRounding.AwayFromZero);
        return (subtotal, tax, subtotal + tax);
    }
}

// ===================== Config =====================

public sealed class GetPaymentConfigHandler(IPaymentGateway gateway) : IRequestHandler<GetPaymentConfigQuery, PaymentConfigDto>
{
    public Task<PaymentConfigDto> Handle(GetPaymentConfigQuery request, CancellationToken ct) =>
        Task.FromResult(new PaymentConfigDto(gateway.IsConfigured, gateway.Name, gateway.PublicKey, Pricing.GstRate * 100));
}

// ===================== Create order =====================

public sealed class CreatePlanOrderValidator : AbstractValidator<CreatePlanOrderCommand>
{
    private static readonly Regex Gstin = new(@"^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.Compiled);

    public CreatePlanOrderValidator()
    {
        RuleFor(x => x.PlanCode).NotEmpty();
        RuleFor(x => x.BillingCycle).Must(c => c is "Monthly" or "Annual").WithMessage("Billing cycle must be Monthly or Annual.");
        RuleFor(x => x.Gstin).Must(g => Gstin.IsMatch(g!.Trim().ToUpperInvariant())).When(x => !string.IsNullOrWhiteSpace(x.Gstin))
            .WithMessage("Enter a valid 15-character GSTIN, e.g. 29ABCDE1234F1Z5.");
    }
}

public sealed class CreatePlanOrderHandler(IUnitOfWork uow, ICurrentUser user, IPaymentGateway gateway) : IRequestHandler<CreatePlanOrderCommand, CheckoutOrderDto>
{
    public async Task<CheckoutOrderDto> Handle(CreatePlanOrderCommand r, CancellationToken ct)
    {
        var business = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        if (!gateway.IsConfigured) throw new BadRequestException("Online payments are not available right now. Our team will help you activate your plan.");
        var plan = await uow.Repository<SubscriptionPlan>().QueryNoTracking().FirstOrDefaultAsync(p => p.Code == r.PlanCode && p.IsActive, ct)
                   ?? throw new BadRequestException("Choose a valid plan.");
        var (subtotal, tax, total) = Pricing.For(plan, r.BillingCycle);
        if (subtotal <= 0) throw new BadRequestException("The Free plan doesn't need a payment.");

        var owner = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Id == business.Id)
            .Select(b => new { b.Owner.DisplayName, b.Owner.Email, OwnerPhone = b.Owner.PhoneNumber, b.PhoneNumber }).FirstAsync(ct);

        var order = new PaymentOrder
        {
            OrderNumber = References.New("ORD"), BusinessId = business.Id, PlanId = plan.Id, BillingCycle = r.BillingCycle,
            Amount = subtotal, TaxAmount = tax, TotalAmount = total, Currency = "INR", Gateway = gateway.Name,
            Status = PaymentOrderStatuses.Created, Gstin = string.IsNullOrWhiteSpace(r.Gstin) ? null : r.Gstin.Trim().ToUpperInvariant()
        };
        uow.Repository<PaymentOrder>().Add(order);
        await uow.SaveChangesAsync(ct);

        try
        {
            var gatewayOrder = await gateway.CreateOrderAsync(order.OrderNumber, (long)(total * 100), order.Currency,
                new Dictionary<string, string> { ["orderNumber"] = order.OrderNumber, ["businessId"] = business.Id.ToString(), ["plan"] = plan.Code, ["cycle"] = r.BillingCycle },
                ct);
            order.GatewayOrderId = gatewayOrder.Id;
        }
        catch (BadRequestException ex)
        {
            order.Status = PaymentOrderStatuses.Failed;
            order.FailureReason = ex.Message;
            await uow.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        await uow.SaveChangesAsync(ct);

        var digits = new string((owner.OwnerPhone ?? owner.PhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        return new CheckoutOrderDto(order.Id, order.OrderNumber, gateway.Name, gateway.PublicKey!, order.GatewayOrderId!, (long)(total * 100), order.Currency,
            plan.Code, plan.Name, r.BillingCycle, subtotal, tax, total, business.Name,
            new CheckoutPrefillDto(owner.DisplayName, owner.Email ?? string.Empty, digits.Length >= 10 ? digits[^10..] : digits));
    }
}

// ===================== Verify (browser callback) =====================

public sealed class VerifyPlanPaymentValidator : AbstractValidator<VerifyPlanPaymentCommand>
{
    public VerifyPlanPaymentValidator()
    {
        RuleFor(x => x.GatewayOrderId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.GatewayPaymentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Signature).NotEmpty().MaximumLength(256);
    }
}

public sealed class VerifyPlanPaymentHandler(IUnitOfWork uow, ICurrentUser user, IPaymentGateway gateway, IRealtimeNotifier notifier)
    : IRequestHandler<VerifyPlanPaymentCommand, PaymentResultDto>
{
    public async Task<PaymentResultDto> Handle(VerifyPlanPaymentCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var order = await uow.Repository<PaymentOrder>().QueryNoTracking().FirstOrDefaultAsync(o => o.Id == r.OrderId && o.BusinessId == r.BusinessId, ct)
                    ?? throw new NotFoundException("Order", r.OrderId);
        if (order.Status == PaymentOrderStatuses.Paid) return await PaymentResults.ForAsync(uow, order.Id, ct);
        if (order.GatewayOrderId != r.GatewayOrderId || !gateway.VerifyPaymentSignature(r.GatewayOrderId, r.GatewayPaymentId, r.Signature))
            throw new BadRequestException("We couldn't verify this payment. If money was deducted, it will be confirmed automatically or refunded by Razorpay.");

        // Never trust the browser alone: confirm the payment, its order and its amount with the gateway.
        var payment = await gateway.GetPaymentAsync(r.GatewayPaymentId, ct);
        return await SubscriptionActivator.ActivateAsync(uow, notifier, order.Id, payment, ct);
    }
}

// ===================== Cancelled / failed in the browser =====================

public sealed class RecordCheckoutOutcomeHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<RecordCheckoutOutcomeCommand, PaymentResultDto>
{
    public async Task<PaymentResultDto> Handle(RecordCheckoutOutcomeCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var order = await uow.Repository<PaymentOrder>().Query().FirstOrDefaultAsync(o => o.Id == r.OrderId && o.BusinessId == r.BusinessId, ct)
                    ?? throw new NotFoundException("Order", r.OrderId);
        // Only an open order can be marked; a payment confirmed meanwhile (e.g. by webhook) wins.
        if (order.Status == PaymentOrderStatuses.Created)
        {
            order.Status = r.Outcome == PaymentOrderStatuses.Failed ? PaymentOrderStatuses.Failed : PaymentOrderStatuses.Cancelled;
            order.FailureReason = string.IsNullOrWhiteSpace(r.Reason) ? (order.Status == PaymentOrderStatuses.Cancelled ? "Checkout closed before payment" : "Payment failed")
                : r.Reason.Trim()[..Math.Min(r.Reason.Trim().Length, 500)];
            try { await uow.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { /* confirmed concurrently */ }
        }
        return await PaymentResults.ForAsync(uow, order.Id, ct);
    }
}

// ===================== Webhook (server-to-server) =====================

public sealed class HandlePaymentWebhookHandler(IUnitOfWork uow, IPaymentGateway gateway, IRealtimeNotifier notifier, ILogger<HandlePaymentWebhookHandler> logger)
    : IRequestHandler<HandlePaymentWebhookCommand, bool>
{
    public async Task<bool> Handle(HandlePaymentWebhookCommand r, CancellationToken ct)
    {
        if (!gateway.VerifyWebhookSignature(r.Body, r.Signature ?? string.Empty)) return false;

        using var doc = JsonDocument.Parse(r.Body);
        var root = doc.RootElement;
        var evt = root.GetProperty("event").GetString();
        if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("payment", out var paymentWrap)) return true;
        var p = paymentWrap.GetProperty("entity");
        string? Str(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var payment = new GatewayPayment(Str("id")!, Str("order_id"), p.GetProperty("amount").GetInt64(), Str("currency") ?? "INR", Str("status") ?? string.Empty,
            Str("method"), Str("error_description"));
        if (payment.OrderId is null) return true;

        var order = await uow.Repository<PaymentOrder>().QueryNoTracking().FirstOrDefaultAsync(o => o.GatewayOrderId == payment.OrderId, ct);
        if (order is null) { logger.LogWarning("Webhook {Event} for unknown order {OrderId}", evt, payment.OrderId); return true; }

        switch (evt)
        {
            case "payment.captured" or "payment.authorized" or "order.paid":
                await SubscriptionActivator.ActivateAsync(uow, notifier, order.Id, payment, ct);
                break;
            case "payment.failed":
                var tracked = await uow.Repository<PaymentOrder>().Query().FirstAsync(o => o.Id == order.Id, ct);
                if (tracked.Status == PaymentOrderStatuses.Created)
                {
                    tracked.Status = PaymentOrderStatuses.Failed;
                    tracked.FailureReason = payment.ErrorDescription ?? "Payment failed";
                    tracked.GatewayPaymentId = payment.Id;
                    try { await uow.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { }
                }
                break;
        }
        return true;
    }
}

// ===================== Activation =====================

internal static class SubscriptionActivator
{
    private static readonly string[] SuccessStatuses = ["captured", "authorized"];

    /// <summary>
    /// Marks the order paid, starts the subscription and writes the invoice - exactly once, even if the browser callback and the
    /// webhook arrive together (row-version concurrency on the order).
    /// </summary>
    public static async Task<PaymentResultDto> ActivateAsync(IUnitOfWork uow, IRealtimeNotifier notifier, Guid orderId, GatewayPayment payment, CancellationToken ct)
    {
        // Reject payments that don't match this order (wrong order, amount or status) and record why - outside the activation transaction.
        var snapshot = await uow.Repository<PaymentOrder>().QueryNoTracking().Where(o => o.Id == orderId)
            .Select(o => new { o.Status, o.GatewayOrderId, o.TotalAmount, o.Currency }).FirstAsync(ct);
        if (snapshot.Status != PaymentOrderStatuses.Paid &&
            (payment.OrderId != snapshot.GatewayOrderId || payment.Amount != (long)(snapshot.TotalAmount * 100) || !SuccessStatuses.Contains(payment.Status)))
        {
            var failed = await uow.Repository<PaymentOrder>().Query().FirstAsync(o => o.Id == orderId, ct);
            failed.Status = PaymentOrderStatuses.Failed;
            failed.GatewayPaymentId = payment.Id;
            failed.FailureReason = payment.ErrorDescription
                ?? (payment.OrderId != snapshot.GatewayOrderId ? "The payment belongs to a different order."
                    : payment.Amount != (long)(snapshot.TotalAmount * 100) ? $"Paid amount {payment.Amount / 100m:0.00} does not match the order total {snapshot.TotalAmount:0.00} {snapshot.Currency}."
                    : $"Payment status is '{payment.Status}'.");
            try { await uow.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return await PaymentResults.ForAsync(uow, orderId, ct); }
            throw new BadRequestException("This payment could not be confirmed. If money was deducted, Razorpay will refund it automatically.");
        }

        string? ownerId = null, planName = null;
        try
        {
            var activated = await uow.ExecuteInTransactionAsync(async token =>
            {
                var order = await uow.Repository<PaymentOrder>().Query().Include(o => o.Plan).FirstAsync(o => o.Id == orderId, token);
                if (order.Status == PaymentOrderStatuses.Paid) return false;

                var today = IndianTime.Today;
                var subs = uow.Repository<BusinessSubscription>();
                var current = await subs.Query()
                    .Where(s => s.BusinessId == order.BusinessId && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.EndDate >= today)
                    .ToListAsync(token);

                // Renewing the same paid plan extends it; any other change replaces the current plan from today.
                var renewal = current.Where(s => s.PlanId == order.PlanId && s.Status == SubscriptionStatuses.Active).MaxBy(s => s.EndDate);
                var start = renewal is not null ? renewal.EndDate.AddDays(1) : today;
                if (renewal is null) current.ForEach(s => s.Status = SubscriptionStatuses.Cancelled);

                var subscription = new BusinessSubscription
                {
                    SubscriptionNumber = References.New("SUB"), BusinessId = order.BusinessId, PlanId = order.PlanId, BillingCycle = order.BillingCycle,
                    StartDate = start, EndDate = (order.BillingCycle == "Annual" ? start.AddYears(1) : start.AddMonths(1)).AddDays(-1),
                    Amount = order.Amount, Status = SubscriptionStatuses.Active, AutoRenew = false
                };
                subs.Add(subscription);

                var invoice = new Payment
                {
                    InvoiceNumber = $"INV-{References.New("SB")}", BusinessId = order.BusinessId, PaymentType = "Subscription", ReferenceId = subscription.Id,
                    Amount = order.Amount, TaxAmount = order.TaxAmount, TotalAmount = order.TotalAmount, PaymentMode = Mode(payment.Method),
                    Status = "Success", PaidOn = DateTimeOffset.UtcNow
                };
                uow.Repository<Payment>().Add(invoice);

                order.Status = PaymentOrderStatuses.Paid;
                order.GatewayPaymentId = payment.Id;
                order.PaymentMethod = Mode(payment.Method);
                order.PaidOn = invoice.PaidOn;
                order.SubscriptionId = subscription.Id;
                order.InvoiceNumber = invoice.InvoiceNumber;
                order.FailureReason = null;
                await uow.SaveChangesAsync(token);

                ownerId = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Id == order.BusinessId).Select(b => b.OwnerUserId).FirstAsync(token);
                planName = order.Plan.Name;
                return true;
            }, ct);

            if (activated && ownerId is not null)
                await NotificationPublisher.PublishAsync(uow, notifier, ownerId, "Payment received",
                    $"Your {planName} plan is now active. Your invoice is available in Plan & billing.", "PaymentReceived", "/business/plan", ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The other confirmation path activated the order first - nothing more to do.
        }
        return await PaymentResults.ForAsync(uow, orderId, ct);
    }

    private static string Mode(string? method) => method?.ToLowerInvariant() switch
    {
        "upi" => "UPI", "card" => "Card", "netbanking" => "NetBanking", "wallet" => "Wallet", "emi" => "EMI", "paylater" => "PayLater",
        null or "" => "Online", var m => m
    };
}

internal static class PaymentResults
{
    public static Task<PaymentResultDto> ForAsync(IUnitOfWork uow, Guid orderId, CancellationToken ct) =>
        uow.Repository<PaymentOrder>().QueryNoTracking().Where(o => o.Id == orderId)
            .Select(o => new PaymentResultDto(o.Id, o.OrderNumber, o.Status, o.Plan.Name, o.BillingCycle,
                uow.Repository<BusinessSubscription>().QueryNoTracking().Where(s => s.Id == o.SubscriptionId).Select(s => (DateTime?)s.StartDate).FirstOrDefault(),
                uow.Repository<BusinessSubscription>().QueryNoTracking().Where(s => s.Id == o.SubscriptionId).Select(s => (DateTime?)s.EndDate).FirstOrDefault(),
                o.InvoiceNumber, o.TotalAmount, o.PaymentMethod, o.FailureReason))
            .FirstAsync(ct);
}
