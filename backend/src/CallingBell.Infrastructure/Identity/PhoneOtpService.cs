using System.Security.Cryptography;
using System.Text;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Auth;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallingBell.Infrastructure.Identity;

public sealed class OtpOptions
{
    public const string Section = "Authentication:Otp";
    public int CodeLength { get; set; } = 6;
    public int ExpiryMinutes { get; set; } = 5;
    /// <summary>Minimum wait before another code can be sent to the same number.</summary>
    public int ResendSeconds { get; set; } = 30;
    /// <summary>Wrong guesses allowed per code before a new one must be requested.</summary>
    public int MaxAttempts { get; set; } = 5;
    public int MaxSendsPerHour { get; set; } = 5;
    /// <summary>Codes per number within <see cref="SendWindowMinutes"/>.</summary>
    public int MaxSendsPerWindow { get; set; } = 3;
    /// <summary>Codes requested from one IP address (any numbers) within <see cref="SendWindowMinutes"/>.</summary>
    public int MaxSendsPerIpPerWindow { get; set; } = 10;
    public int SendWindowMinutes { get; set; } = 15;
    /// <summary>How long a verified sign-up number stays usable for creating the account.</summary>
    public int VerificationMinutes { get; set; } = 60;
    /// <summary>Returns the code in the API response. Local development only - never enable in production.</summary>
    public bool ExposeCodeInResponse { get; set; }
}

internal sealed class PhoneOtpService(ApplicationDbContext db, NotificationRouter router, OtpCodeVault vault, ICurrentUser currentUser,
    IOptions<OtpOptions> options, ILogger<PhoneOtpService> logger) : IPhoneOtpService
{
    private readonly OtpOptions _options = options.Value;

    public int ExpirySeconds => _options.ExpiryMinutes * 60;
    public int ResendSeconds => _options.ResendSeconds;

    public async Task<OtpChallengeDto> SendAsync(string phoneNumber, OtpPurpose purpose, CancellationToken ct,
        OtpChannelPreference channel = OtpChannelPreference.Auto)
    {
        var now = DateTimeOffset.UtcNow;
        var e164 = Phones.ToE164(phoneNumber) ?? throw Invalid("phoneNumber", "Enter a valid mobile number.");
        var sentLastHour = await db.OtpCodes.Where(o => o.PhoneNumber == phoneNumber && o.CreatedAt > now.AddHours(-1))
            .OrderByDescending(o => o.CreatedAt).Select(o => o.CreatedAt).ToListAsync(ct);
        var window = now.AddMinutes(-_options.SendWindowMinutes);
        if (sentLastHour.Count >= _options.MaxSendsPerHour || sentLastHour.Count(t => t > window) >= _options.MaxSendsPerWindow)
            throw new BadRequestException($"Too many codes were requested for this number. Please try again in {_options.SendWindowMinutes} minutes.");
        if (currentUser.IpAddress is { } ip
            && await db.OtpCodes.CountAsync(o => o.IpAddress == ip && o.CreatedAt > window, ct) >= _options.MaxSendsPerIpPerWindow)
            throw new BadRequestException($"Too many codes were requested. Please try again in {_options.SendWindowMinutes} minutes.");
        if (sentLastHour.Count > 0)
        {
            var wait = (int)Math.Ceiling((sentLastHour[0].AddSeconds(_options.ResendSeconds) - now).TotalSeconds);
            if (wait > 0) throw new BadRequestException($"Please wait {wait} seconds before requesting another code.");
        }

        // Only the newest code for a number and purpose is valid.
        var purposeName = purpose.ToString();
        var skip = channel == OtpChannelPreference.Sms || await PreviousWhatsAppNotReceivedAsync(phoneNumber, purposeName, window, ct)
            ? new HashSet<string> { NotificationChannels.WhatsAppAuthentication }
            : null;
        await db.OtpCodes.Where(o => o.PhoneNumber == phoneNumber && o.Purpose == purposeName && o.ConsumedAt == null && o.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, now), ct);

        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, _options.CodeLength)).ToString($"D{_options.CodeLength}");
        var otp = new OtpCode
        {
            PhoneNumber = phoneNumber, Purpose = purposeName, ExpiresAt = now.AddMinutes(_options.ExpiryMinutes),
            IpAddress = currentUser.IpAddress, CreatedAt = now
        };
        otp.CodeHash = HashCode(otp.Id, code);
        db.OtpCodes.Add(otp);
        await db.SaveChangesAsync(ct);

        // WhatsApp (authentication template) first, then SMS; one at a time, never both. The code itself is never stored or logged.
        var message = OtpMessages.Create(e164, code, _options.ExpiryMinutes);
        var result = await router.RouteAsync(message, new DeliveryOwner(null, otp.Id), maxAttemptsPerProvider: 2, skipChannels: skip, ct: ct);
        if (!result.Success)
        {
            otp.ExpiresAt = now;
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("Verification code for {Phone} could not be sent on any channel", Phones.Mask(e164));
            throw new ExternalServiceException(503, "We couldn't send a verification code right now. Please try again in a minute.");
        }
        // Kept in memory (never in the database) while valid, only so a WhatsApp failure reported later can resend it by SMS.
        if (result.Channel == NotificationChannels.WhatsAppAuthentication) vault.Remember(otp.Id, e164, code, otp.ExpiresAt);

        return new OtpChallengeDto(Mask(phoneNumber), _options.ExpiryMinutes * 60, _options.ResendSeconds,
            _options.ExposeCodeInResponse ? code : null, ChannelName(result.Channel));
    }

    /// <summary>
    /// Asking again soon after a code went to WhatsApp, and WhatsApp never confirmed it arrived (or reported it failed): this time skip
    /// WhatsApp and send it by SMS.
    /// </summary>
    private async Task<bool> PreviousWhatsAppNotReceivedAsync(string phone, string purpose, DateTimeOffset since, CancellationToken ct)
    {
        var previous = await db.OtpCodes.Where(o => o.PhoneNumber == phone && o.Purpose == purpose && o.CreatedAt > since)
            .OrderByDescending(o => o.CreatedAt).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
        if (previous is null) return false;
        var last = await db.NotificationDeliveries.Where(d => d.OtpCodeId == previous && d.Status != DeliveryStatuses.Unavailable)
            .OrderByDescending(d => d.CreatedAt).Select(d => new { d.Channel, d.Status }).FirstOrDefaultAsync(ct);
        return last is { Channel: NotificationChannels.WhatsAppAuthentication }
               && last.Status is not (DeliveryStatuses.Delivered or DeliveryStatuses.Read);
    }

    private static string? ChannelName(string? channel) => channel switch
    {
        NotificationChannels.WhatsAppAuthentication or NotificationChannels.WhatsApp => "WhatsApp",
        NotificationChannels.Sms => "SMS",
        NotificationChannels.Rcs => "RCS",
        _ => null,
    };

    public async Task VerifyAsync(string phoneNumber, OtpPurpose purpose, string code, CancellationToken ct) =>
        await VerifyCodeAsync(phoneNumber, purpose, code, ct);

    public async Task<PhoneVerificationDto> VerifyForSignUpAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var otp = await VerifyCodeAsync(phoneNumber, OtpPurpose.SignUp, code, ct);
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        otp.VerificationTokenHash = Sha256(token);
        otp.VerificationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.VerificationMinutes);
        await db.SaveChangesAsync(ct);
        return new PhoneVerificationDto(token, otp.VerificationExpiresAt.Value);
    }

    public async Task ConsumeVerificationAsync(string phoneNumber, string verificationToken, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = Sha256(verificationToken);
        var otp = await db.OtpCodes.FirstOrDefaultAsync(o => o.VerificationTokenHash == hash, ct);
        if (otp is null || otp.PhoneNumber != phoneNumber || otp.VerificationUsedAt is not null || otp.VerificationExpiresAt <= now)
            throw Invalid("phoneNumber", "Your mobile verification has expired. Please verify your number again.");
        otp.VerificationUsedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<OtpCode> VerifyCodeAsync(string phoneNumber, OtpPurpose purpose, string code, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var purposeName = purpose.ToString();
        var otp = await db.OtpCodes.Where(o => o.PhoneNumber == phoneNumber && o.Purpose == purposeName && o.ConsumedAt == null)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);

        if (otp is null || otp.ExpiresAt <= now) throw Invalid("code", "This code has expired. Request a new one.");
        if (otp.Attempts >= _options.MaxAttempts) throw Invalid("code", "Too many incorrect attempts. Request a new code.");

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashCode(otp.Id, code)), Encoding.ASCII.GetBytes(otp.CodeHash)))
        {
            otp.Attempts++;
            await db.SaveChangesAsync(ct);
            var left = _options.MaxAttempts - otp.Attempts;
            throw Invalid("code", left > 0
                ? $"Incorrect code. {left} {(left == 1 ? "attempt" : "attempts")} left."
                : "Too many incorrect attempts. Request a new code.");
        }

        otp.ConsumedAt = now;
        await db.SaveChangesAsync(ct);
        return otp;
    }

    private static ValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });

    // Salted with the row id, so equal codes never share a hash.
    private static string HashCode(Guid id, string code) => Sha256($"{id:N}:{code}");

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>"+91 98765 43210" -> "+91 ••••• •3210".</summary>
    private static string Mask(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length >= 10 ? $"+91 ••••• •{digits[^4..]}" : phone;
    }
}
