using CallingBell.Application.Common;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CallingBell.Infrastructure.Identity;

internal static class OtpMessages
{
    /// <summary>The OTP route's message: the code for the WhatsApp authentication template ({{1}}) and the SMS text ({{1}}, {{2}} minutes).</summary>
    public static NotificationMessage Create(string e164, string code, int expiryMinutes) => new()
    {
        RouteCode = NotificationRoutes.Otp,
        Recipient = new NotificationRecipient(null, e164, Phones.CountryOf(e164)),
        Title = "Verification code",
        Body = "Your Calling Bell verification code",
        TemplateParameters = [code, expiryMinutes.ToString()],
        IsSensitive = true,
    };
}

/// <summary>
/// Codes sent on WhatsApp, held in this server's memory only (never the database or logs) until they expire, so a failure WhatsApp reports
/// after accepting the message can still be followed by the same code over SMS.
/// </summary>
public sealed class OtpCodeVault(IMemoryCache cache)
{
    internal sealed record Entry(string PhoneE164, string Code, int ExpiryMinutes);

    public void Remember(Guid otpId, string phoneE164, string code, DateTimeOffset expiresAt)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((expiresAt - DateTimeOffset.UtcNow).TotalMinutes));
        cache.Set(Key(otpId), new Entry(phoneE164, code, minutes), expiresAt);
    }

    internal Entry? Take(Guid otpId)
    {
        if (!cache.TryGetValue(Key(otpId), out Entry? entry)) return null;
        cache.Remove(Key(otpId));
        return entry;
    }

    private static string Key(Guid id) => $"otp-vault|{id:N}";
}

/// <summary>WhatsApp reported (via webhook) that a code it had accepted could not be delivered: send the same code by SMS, once.</summary>
internal sealed class OtpSmsFallback(ApplicationDbContext db, OtpCodeVault vault, NotificationRouter router, ILogger<OtpSmsFallback> logger) : IOtpFallback
{
    public async Task TrySendBySmsAsync(Guid otpCodeId, CancellationToken ct)
    {
        var otp = await db.OtpCodes.AsNoTracking().Where(o => o.Id == otpCodeId).Select(o => new { o.ConsumedAt, o.ExpiresAt }).FirstOrDefaultAsync(ct);
        if (otp is null || otp.ConsumedAt is not null || otp.ExpiresAt <= DateTimeOffset.UtcNow) return;
        // Another app instance sent it (or it was already resent): the visitor can ask for a new code, which will go by SMS.
        if (vault.Take(otpCodeId) is not { } entry) return;

        var result = await router.RouteAsync(OtpMessages.Create(entry.PhoneE164, entry.Code, entry.ExpiryMinutes), new DeliveryOwner(null, otpCodeId),
            maxAttemptsPerProvider: 2, skipChannels: new HashSet<string> { NotificationChannels.WhatsAppAuthentication, NotificationChannels.WhatsApp }, ct: ct);
        logger.LogInformation("Verification code for {Phone}: WhatsApp failed after accepting it; SMS fallback {Result}", Phones.Mask(entry.PhoneE164),
            result.Success ? "sent" : "failed");
    }
}
