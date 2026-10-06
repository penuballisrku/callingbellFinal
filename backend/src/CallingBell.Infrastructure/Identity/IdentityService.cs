using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Auth;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallingBell.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "CallingBell";
    public string Audience { get; set; } = "CallingBell.Clients";
    public string Key { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 14;
}

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IOptions<JwtOptions> jwtOptions) : IIdentityService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResultDto> SignInWithPhoneAsync(string phoneNumber, CancellationToken ct)
    {
        var users = await userManager.Users.Where(u => u.PhoneNumber == phoneNumber && !u.IsDeleted).Take(2).ToListAsync(ct);
        if (users.Count == 0) throw new BadRequestException("No account uses this mobile number.");
        if (users.Count > 1)
            throw new BadRequestException("This mobile number is linked to more than one account. Sign in with your email and password instead.");

        var user = users[0];
        if (!user.IsActive) throw new ForbiddenAccessException("This account has been deactivated. Please contact support.");
        if (await userManager.IsLockedOutAsync(user)) throw new ForbiddenAccessException("This account is temporarily locked. Try again later.");

        user.PhoneNumberConfirmed = true; // the OTP proved ownership of the number
        user.LastLoginOn = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResultDto> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || user.IsDeleted) throw new BadRequestException("Invalid email or password.");
        if (!user.IsActive) throw new ForbiddenAccessException("This account has been deactivated. Please contact support.");
        if (await userManager.IsLockedOutAsync(user)) throw new ForbiddenAccessException("Too many failed attempts. Try again in 15 minutes.");

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            throw new BadRequestException("Invalid email or password.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginOn = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResultDto> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var cityId = string.IsNullOrWhiteSpace(request.CitySlug)
            ? null
            : await db.Cities.Where(c => c.Slug == request.CitySlug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            PhoneNumber = NormalizePhone(request.PhoneNumber),
            PhoneNumberConfirmed = true, // verified by OTP before registration
            UserType = request.AccountType,
            CityId = cityId,
            IsActive = true,
            EmailConfirmed = true,
            LastLoginOn = DateTimeOffset.UtcNow
        };

        // No password: the account signs in with a mobile OTP (a password can be added later in settings).
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded) throw new BadRequestException(result.Errors.First().Description);

        await userManager.AddToRoleAsync(user, request.AccountType == Roles.BusinessOwner ? Roles.BusinessOwner : Roles.Customer);
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResultDto> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                     ?? throw new ForbiddenAccessException("Session expired. Please sign in again.");

        var now = DateTimeOffset.UtcNow;
        if (stored.RevokedAt is not null)
        {
            // Reuse of a rotated token: treat as theft and revoke every session of the user.
            var active = await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null).ToListAsync(ct);
            active.ForEach(t => t.RevokedAt = now);
            await db.SaveChangesAsync(ct);
            throw new ForbiddenAccessException("Session expired. Please sign in again.");
        }
        if (!stored.IsActive(now)) throw new ForbiddenAccessException("Session expired. Please sign in again.");

        var user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null || !user.IsActive) throw new ForbiddenAccessException("Session expired. Please sign in again.");

        var result = await IssueTokensAsync(user, ct, persist: false);
        stored.RevokedAt = now;
        stored.ReplacedByTokenHash = Hash(result.RefreshToken);
        db.RefreshTokens.Add(NewToken(user.Id, result.RefreshToken, now));
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (stored is null) return;
        stored.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);
        var (roles, permissions) = await GetRolesAndPermissionsAsync(user, ct);
        return await ToDtoAsync(user, roles, permissions, ct);
    }

    public async Task<bool> IsEmailAvailableAsync(string email, CancellationToken ct) =>
        await userManager.FindByEmailAsync(email.Trim()) is null;

    public Task<bool> IsPhoneRegisteredAsync(string phoneNumber, CancellationToken ct, string? exceptUserId = null) =>
        userManager.Users.AnyAsync(u => u.PhoneNumber == phoneNumber && !u.IsDeleted && u.Id != exceptUserId, ct);

    public async Task<AccountSettingsDto> GetAccountSettingsAsync(string userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);
        var now = DateTimeOffset.UtcNow;
        var sessions = await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now, ct);
        return new AccountSettingsDto(user.DisplayName, user.Email ?? string.Empty, user.PhoneNumber, await userManager.HasPasswordAsync(user),
            user.PhoneNumberConfirmed, user.CreatedOn, user.LastLoginOn, sessions);
    }

    public async Task<CurrentUserDto> UpdateAccountAsync(string userId, string displayName, string phoneNumber, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);
        var phone = NormalizePhone(phoneNumber.Trim());
        if (phone != user.PhoneNumber)
        {
            // The mobile number is a sign-in credential, so it must stay unique.
            if (await IsPhoneRegisteredAsync(phone, ct, exceptUserId: userId))
                throw new ValidationException(new Dictionary<string, string[]> { ["phoneNumber"] = ["Another account already uses this mobile number."] });
            user.PhoneNumberConfirmed = false;
        }
        user.DisplayName = displayName.Trim();
        user.PhoneNumber = phone;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) throw new BadRequestException(result.Errors.First().Description);
        return await GetCurrentUserAsync(userId, ct);
    }

    public async Task ChangePasswordAsync(string userId, string? currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);
        var hasPassword = await userManager.HasPasswordAsync(user);
        if (hasPassword && string.IsNullOrEmpty(currentPassword))
            throw new ValidationException(new Dictionary<string, string[]> { ["currentPassword"] = ["Enter your current password."] });

        var result = hasPassword
            ? await userManager.ChangePasswordAsync(user, currentPassword!, newPassword)
            : await userManager.AddPasswordAsync(user, newPassword);
        if (!result.Succeeded)
        {
            var mismatch = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [mismatch ? "currentPassword" : "newPassword"] = mismatch ? ["Your current password is incorrect."] : result.Errors.Select(e => e.Description).ToArray()
            });
        }
    }

    public async Task<int> RevokeOtherSessionsAsync(string userId, string? keepRefreshToken, CancellationToken ct)
    {
        var keep = string.IsNullOrWhiteSpace(keepRefreshToken) ? null : Hash(keepRefreshToken);
        var now = DateTimeOffset.UtcNow;
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now && t.TokenHash != keep).ToListAsync(ct);
        tokens.ForEach(t => t.RevokedAt = now);
        await db.SaveChangesAsync(ct);
        return tokens.Count;
    }

    private async Task<AuthResultDto> IssueTokensAsync(ApplicationUser user, CancellationToken ct, bool persist = true)
    {
        var (roles, permissions) = await GetRolesAndPermissionsAsync(user, ct);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("user_type", user.UserType)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, now.UtcDateTime, expires.UtcDateTime, credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var refreshToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        if (persist)
        {
            db.RefreshTokens.Add(NewToken(user.Id, refreshToken, now));
            await db.SaveChangesAsync(ct);
        }

        return new AuthResultDto(accessToken, expires, refreshToken, await ToDtoAsync(user, roles, permissions, ct));
    }

    private RefreshToken NewToken(string userId, string token, DateTimeOffset now) => new()
    {
        UserId = userId, TokenHash = Hash(token), CreatedAt = now, ExpiresAt = now.AddDays(_jwt.RefreshTokenDays)
    };

    private async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> GetRolesAndPermissionsAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = (await userManager.GetRolesAsync(user)).ToList();
        var permissions = await (from ur in db.UserRoles
                                 join rc in db.RoleClaims on ur.RoleId equals rc.RoleId
                                 where ur.UserId == user.Id && rc.ClaimType == Permissions.ClaimType
                                 select rc.ClaimValue!).Distinct().ToListAsync(ct);
        return (roles, permissions);
    }

    private async Task<CurrentUserDto> ToDtoAsync(ApplicationUser user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        var citySlug = user.CityId is null ? null : await db.Cities.Where(c => c.Id == user.CityId).Select(c => c.Slug).FirstOrDefaultAsync(ct);
        return new CurrentUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, user.PhoneNumber, user.AvatarUrl, user.UserType,
            citySlug, roles, permissions);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        return digits.Length == 10 ? $"+91 {digits[..5]} {digits[5..]}" : phone;
    }
}
