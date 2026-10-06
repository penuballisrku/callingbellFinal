using System.Security.Cryptography;
using System.Text;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Auth;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
    /// <summary>How long a verified sign-up number stays usable for creating the account.</summary>
    public int VerificationMinutes { get; set; } = 60;
    /// <summary>Returns the code in the API response. Local development only - never enable in production.</summary>
    public bool ExposeCodeInResponse { get; set; }
}

internal sealed class PhoneOtpService(ApplicationDbContext db, ISmsSender sms, ICurrentUser currentUser, IOptions<OtpOptions> options) : IPhoneOtpService
{
    private readonly OtpOptions _options = options.Value;

    public async Task<OtpChallengeDto> SendAsync(string phoneNumber, OtpPurpose purpose, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var sentLastHour = await db.OtpCodes.Where(o => o.PhoneNumber == phoneNumber && o.CreatedAt > now.AddHours(-1))
            .OrderByDescending(o => o.CreatedAt).Select(o => o.CreatedAt).ToListAsync(ct);
        if (sentLastHour.Count >= _options.MaxSendsPerHour)
            throw new BadRequestException("Too many codes were requested for this number. Please try again in an hour.");
        if (sentLastHour.Count > 0)
        {
            var wait = (int)Math.Ceiling((sentLastHour[0].AddSeconds(_options.ResendSeconds) - now).TotalSeconds);
            if (wait > 0) throw new BadRequestException($"Please wait {wait} seconds before requesting another code.");
        }

        // Only the newest code for a number and purpose is valid.
        var purposeName = purpose.ToString();
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

        await sms.SendAsync(phoneNumber,
            $"{code} is your Calling Bell verification code. It expires in {_options.ExpiryMinutes} minutes. Do not share it with anyone.", ct);

        return new OtpChallengeDto(Mask(phoneNumber), _options.ExpiryMinutes * 60, _options.ResendSeconds,
            _options.ExposeCodeInResponse ? code : null);
    }

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
