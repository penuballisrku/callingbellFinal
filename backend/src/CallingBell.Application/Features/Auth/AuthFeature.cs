using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Onboarding;
using CallingBell.Domain.Constants;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;

namespace CallingBell.Application.Features.Auth;

public sealed record CurrentUserDto(
    string Id, string Email, string DisplayName, string? PhoneNumber, string? AvatarUrl, string UserType,
    string? CitySlug, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record AccountSettingsDto(string DisplayName, string Email, string? PhoneNumber, bool HasPassword, bool PhoneVerified,
    DateTimeOffset CreatedOn, DateTimeOffset? LastLoginOn, int ActiveSessions);

public sealed record AuthResultDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, CurrentUserDto User);

/// <summary>A new account whose mobile number has already been verified by OTP, so no password is set.</summary>
public sealed record RegisterRequest(string DisplayName, string Email, string PhoneNumber, string AccountType, string? CitySlug);

public enum OtpPurpose { SignIn, SignUp }

/// <summary>A code was sent. <see cref="DevelopmentCode"/> is only filled when the server is configured to expose it (local development).</summary>
/// <param name="Channel">How it was sent: "WhatsApp" or "SMS".</param>
public sealed record OtpChallengeDto(string MaskedPhone, int ExpiresInSeconds, int ResendInSeconds, string? DevelopmentCode, string? Channel = null);

/// <summary>Which channel to send a code on. Auto = WhatsApp first, then SMS (dbo.NotificationRoutingRules, route OTP).</summary>
public enum OtpChannelPreference { Auto, Sms }

public sealed record PhoneVerificationDto(string VerificationToken, DateTimeOffset ExpiresAt);

internal static class AuthRules
{
    public const string IndianMobile = @"^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$";
    public const string IndianMobileMessage = "Enter a valid 10-digit Indian mobile number.";
    public const string OtpCode = @"^\d{4,8}$";
    public const string OtpCodeMessage = "Enter the code we sent to your mobile.";

    public static async Task EnsureCanRegisterAsync(IIdentityService identity, string email, string normalizedPhone, CancellationToken ct)
    {
        if (!await identity.IsEmailAvailableAsync(email, ct))
            throw new ConflictException("An account with this email already exists. Sign in instead.");
        if (await identity.IsPhoneRegisteredAsync(normalizedPhone, ct)) throw PhoneTaken();
    }

    /// <summary>The password rules for a new or changed password: 8+ characters with an uppercase letter, a lowercase letter and a number.</summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Enter a password.").MinimumLength(8).WithMessage("Use at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.");

    public static ValidationException PhoneTaken() =>
        new(new Dictionary<string, string[]> { ["phoneNumber"] = ["An account already uses this mobile number. Sign in instead."] });
}

// ---------- Login (email + password) ----------
public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResultDto>;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler(IIdentityService identity) : IRequestHandler<LoginCommand, AuthResultDto>
{
    public Task<AuthResultDto> Handle(LoginCommand request, CancellationToken ct) =>
        identity.LoginAsync(request.Email.Trim(), request.Password, ct);
}

// ---------- OTP: send a code ----------
/// <param name="Purpose">"SignIn" or "SignUp".</param>
/// <param name="Channel">"SMS" to skip WhatsApp ("Send by SMS instead"); omit for WhatsApp first, then SMS.</param>
public sealed record SendOtpCommand(string PhoneNumber, string Purpose, string? Channel = null) : IRequest<OtpChallengeDto>;

public sealed class SendOtpValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage(AuthRules.IndianMobileMessage)
            .Matches(AuthRules.IndianMobile).WithMessage(AuthRules.IndianMobileMessage);
        RuleFor(x => x.Purpose).Must(p => p is nameof(OtpPurpose.SignIn) or nameof(OtpPurpose.SignUp)).WithMessage("Purpose must be SignIn or SignUp.");
        RuleFor(x => x.Channel).Must(c => c is null or "SMS" or "AUTO").WithMessage("channel must be SMS or AUTO.");
    }
}

public sealed class SendOtpHandler(IIdentityService identity, IPhoneOtpService otp) : IRequestHandler<SendOtpCommand, OtpChallengeDto>
{
    public async Task<OtpChallengeDto> Handle(SendOtpCommand r, CancellationToken ct)
    {
        var phone = Phones.Normalize(r.PhoneNumber);
        var purpose = Enum.Parse<OtpPurpose>(r.Purpose);
        var registered = await identity.IsPhoneRegisteredAsync(phone, ct);
        if (purpose == OtpPurpose.SignIn && !registered)
            throw new ValidationException(new Dictionary<string, string[]> { ["phoneNumber"] = ["No account uses this mobile number. Create an account to get started."] });
        if (purpose == OtpPurpose.SignUp && registered) throw AuthRules.PhoneTaken();
        return await otp.SendAsync(phone, purpose, ct, r.Channel == "SMS" ? OtpChannelPreference.Sms : OtpChannelPreference.Auto);
    }
}

// ---------- OTP (generic): request a code ----------
/// <param name="PhoneNumber">E.164, e.g. "+919876543210" (a 10-digit Indian number is also accepted).</param>
/// <param name="Purpose">"LOGIN" or "SIGNUP".</param>
/// <param name="Channel">"SMS" to skip WhatsApp ("Send by SMS instead"); omit for WhatsApp first, then SMS.</param>
public sealed record RequestOtpCommand(string PhoneNumber, string Purpose, string? Channel = null) : IRequest<RequestOtpResultDto>;

/// <summary>The same answer whether or not a code was sent, so the response never reveals which numbers have accounts.</summary>
public sealed record RequestOtpResultDto(int ExpiresInSeconds, int ResendInSeconds, string? DevelopmentCode);

public sealed class RequestOtpValidator : AbstractValidator<RequestOtpCommand>
{
    public RequestOtpValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(20).Must(p => Phones.ToE164(p) is not null)
            .WithMessage("Enter a valid mobile number with its country code, e.g. +919876543210.");
        RuleFor(x => x.Purpose).Must(p => p is "LOGIN" or "SIGNUP").WithMessage("purpose must be LOGIN or SIGNUP.");
        RuleFor(x => x.Channel).Must(c => c is null or "SMS" or "AUTO").WithMessage("channel must be SMS or AUTO.");
    }
}

/// <summary>
/// Sends a code only when it can be used (LOGIN: the number has an account; SIGNUP: it has none) but answers the same either way. Sent on
/// WhatsApp first (authentication template), by SMS if WhatsApp can't deliver it; never both.
/// </summary>
public sealed class RequestOtpHandler(IIdentityService identity, IPhoneOtpService otp, ILogger<RequestOtpHandler> logger)
    : IRequestHandler<RequestOtpCommand, RequestOtpResultDto>
{
    public async Task<RequestOtpResultDto> Handle(RequestOtpCommand r, CancellationToken ct)
    {
        var phone = Phones.Normalize(Phones.ToE164(r.PhoneNumber)!);
        var purpose = r.Purpose == "LOGIN" ? OtpPurpose.SignIn : OtpPurpose.SignUp;
        var registered = await identity.IsPhoneRegisteredAsync(phone, ct);
        string? developmentCode = null;
        if (purpose == OtpPurpose.SignIn == registered)
        {
            try
            {
                var challenge = await otp.SendAsync(phone, purpose, ct, r.Channel == "SMS" ? OtpChannelPreference.Sms : OtpChannelPreference.Auto);
                developmentCode = challenge.DevelopmentCode;
            }
            catch (ExternalServiceException)
            {
                // Every channel failed. Still the generic answer (an error here would reveal the number has an account).
                logger.LogWarning("OTP request: no channel could deliver the code");
            }
            catch (BadRequestException)
            {
                // Per-number limits (resend wait, codes per 15 minutes): likewise not revealed; the IP limit still applies to everyone.
                logger.LogInformation("OTP request: limit reached for the number");
            }
        }
        return new RequestOtpResultDto(otp.ExpirySeconds, otp.ResendSeconds, developmentCode);
    }
}

// ---------- OTP (generic): verify a code ----------
/// <param name="Purpose">"LOGIN" (signs in) or "SIGNUP" (returns a verification token for registration).</param>
public sealed record VerifyOtpCommand(string PhoneNumber, string Code, string Purpose) : IRequest<VerifyOtpResultDto>;

public sealed record VerifyOtpResultDto(AuthResultDto? Auth, PhoneVerificationDto? Verification);

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(20).Must(p => Phones.ToE164(p) is not null).WithMessage("Enter a valid mobile number.");
        RuleFor(x => x.Code).NotEmpty().WithMessage(AuthRules.OtpCodeMessage).Matches(AuthRules.OtpCode).WithMessage(AuthRules.OtpCodeMessage);
        RuleFor(x => x.Purpose).Must(p => p is "LOGIN" or "SIGNUP").WithMessage("purpose must be LOGIN or SIGNUP.");
    }
}

/// <summary>Checks the latest code (5 tries, then a new code is needed) and signs in or issues the sign-up verification token.</summary>
public sealed class VerifyOtpHandler(IIdentityService identity, IPhoneOtpService otp) : IRequestHandler<VerifyOtpCommand, VerifyOtpResultDto>
{
    public async Task<VerifyOtpResultDto> Handle(VerifyOtpCommand r, CancellationToken ct)
    {
        var phone = Phones.Normalize(Phones.ToE164(r.PhoneNumber)!);
        if (r.Purpose == "SIGNUP") return new VerifyOtpResultDto(null, await otp.VerifyForSignUpAsync(phone, r.Code.Trim(), ct));
        await otp.VerifyAsync(phone, OtpPurpose.SignIn, r.Code.Trim(), ct);
        return new VerifyOtpResultDto(await identity.SignInWithPhoneAsync(phone, ct), null);
    }
}

// ---------- Google ----------
/// <summary>Sign-in options the sign-in screens offer; <paramref name="GoogleClientId"/> is null when Google sign-in is off.</summary>
public sealed record AuthProvidersDto(string? GoogleClientId);

public sealed record GetAuthProvidersQuery : IRequest<AuthProvidersDto>;

public sealed class GetAuthProvidersHandler(IGoogleTokenValidator google) : IRequestHandler<GetAuthProvidersQuery, AuthProvidersDto>
{
    public Task<AuthProvidersDto> Handle(GetAuthProvidersQuery r, CancellationToken ct) => Task.FromResult(new AuthProvidersDto(google.ClientId));
}

/// <summary>
/// "Sign in with Google": the ID token from Google's button, the city the visitor has selected (for a new account), and the kind of account
/// wanted: <c>BusinessOwner</c> creates an owner account (or adds the owner role to the person's customer account); default Customer.
/// </summary>
public sealed record GoogleSignInCommand(string Credential, string? CitySlug, string? AccountType = null) : IRequest<AuthResultDto>;

public sealed class GoogleSignInValidator : AbstractValidator<GoogleSignInCommand>
{
    public GoogleSignInValidator()
    {
        RuleFor(x => x.Credential).NotEmpty().MaximumLength(8192);
        RuleFor(x => x.CitySlug).MaximumLength(120);
        RuleFor(x => x.AccountType).Must(t => t is null or Roles.Customer or Roles.BusinessOwner).WithMessage("Choose a customer or business account.");
    }
}

public sealed class GoogleSignInHandler(IGoogleTokenValidator google, IIdentityService identity) : IRequestHandler<GoogleSignInCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(GoogleSignInCommand r, CancellationToken ct) =>
        await identity.SignInWithGoogleAsync(await google.ValidateAsync(r.Credential, ct), r.CitySlug, r.AccountType ?? Roles.Customer, ct);
}

// ---------- OTP: sign in ----------
public sealed record OtpSignInCommand(string PhoneNumber, string Code) : IRequest<AuthResultDto>;

public sealed class OtpSignInValidator : AbstractValidator<OtpSignInCommand>
{
    public OtpSignInValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(AuthRules.IndianMobile).WithMessage(AuthRules.IndianMobileMessage);
        RuleFor(x => x.Code).NotEmpty().WithMessage(AuthRules.OtpCodeMessage).Matches(AuthRules.OtpCode).WithMessage(AuthRules.OtpCodeMessage);
    }
}

public sealed class OtpSignInHandler(IIdentityService identity, IPhoneOtpService otp) : IRequestHandler<OtpSignInCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(OtpSignInCommand r, CancellationToken ct)
    {
        var phone = Phones.Normalize(r.PhoneNumber);
        await otp.VerifyAsync(phone, OtpPurpose.SignIn, r.Code.Trim(), ct);
        return await identity.SignInWithPhoneAsync(phone, ct);
    }
}

// ---------- OTP: verify a mobile number before sign-up ----------
public sealed record VerifySignUpPhoneCommand(string PhoneNumber, string Code) : IRequest<PhoneVerificationDto>;

public sealed class VerifySignUpPhoneValidator : AbstractValidator<VerifySignUpPhoneCommand>
{
    public VerifySignUpPhoneValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(AuthRules.IndianMobile).WithMessage(AuthRules.IndianMobileMessage);
        RuleFor(x => x.Code).NotEmpty().WithMessage(AuthRules.OtpCodeMessage).Matches(AuthRules.OtpCode).WithMessage(AuthRules.OtpCodeMessage);
    }
}

public sealed class VerifySignUpPhoneHandler(IPhoneOtpService otp) : IRequestHandler<VerifySignUpPhoneCommand, PhoneVerificationDto>
{
    public Task<PhoneVerificationDto> Handle(VerifySignUpPhoneCommand r, CancellationToken ct) =>
        otp.VerifyForSignUpAsync(Phones.Normalize(r.PhoneNumber), r.Code.Trim(), ct);
}

// ---------- Register (customer) ----------
public sealed record RegisterCommand(string DisplayName, string Email, string PhoneNumber, string PhoneVerificationToken, string AccountType, string? CitySlug)
    : IRequest<AuthResultDto>;

public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(AuthRules.IndianMobile).WithMessage(AuthRules.IndianMobileMessage);
        RuleFor(x => x.PhoneVerificationToken).NotEmpty().WithMessage("Verify your mobile number with the code we send you.")
            .OverridePropertyName("phoneNumber");
        RuleFor(x => x.AccountType).Must(t => t is "Customer" or "BusinessOwner")
            .WithMessage("Account type must be Customer or BusinessOwner.");
    }
}

public sealed class RegisterHandler(IIdentityService identity, IPhoneOtpService otp, IUnitOfWork uow) : IRequestHandler<RegisterCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(RegisterCommand r, CancellationToken ct)
    {
        var phone = Phones.Normalize(r.PhoneNumber);
        await AuthRules.EnsureCanRegisterAsync(identity, r.Email.Trim(), phone, ct);
        // The verification token is only spent when the account is actually created.
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            await otp.ConsumeVerificationAsync(phone, r.PhoneVerificationToken, token);
            return await identity.RegisterAsync(new RegisterRequest(r.DisplayName.Trim(), r.Email.Trim(), phone, r.AccountType, r.CitySlug), token);
        }, ct);
    }
}

// ---------- Register (customer, email + password) ----------
/// <summary>
/// An account with a password: the other way to sign up besides Google. <paramref name="AccountType"/> <c>BusinessOwner</c> creates an
/// owner account (who then sets up the business); default Customer. The email isn't verified by this.
/// </summary>
public sealed record RegisterWithPasswordCommand(string DisplayName, string Email, string Password, string ConfirmPassword, string? CitySlug,
    string? AccountType = null) : IRequest<AuthResultDto>;

public sealed class RegisterWithPasswordValidator : AbstractValidator<RegisterWithPasswordCommand>
{
    public RegisterWithPasswordValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter your full name.").MinimumLength(2).WithMessage("Enter your full name.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email address.").EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(256);
        RuleFor(x => x.Password).StrongPassword().MaximumLength(128);
        RuleFor(x => x.ConfirmPassword).NotEmpty().WithMessage("Confirm your password.")
            .Equal(x => x.Password).WithMessage("Passwords don't match.");
        RuleFor(x => x.CitySlug).MaximumLength(120);
        RuleFor(x => x.AccountType).Must(t => t is null or Roles.Customer or Roles.BusinessOwner).WithMessage("Choose a customer or business account.");
    }
}

public sealed class RegisterWithPasswordHandler(IIdentityService identity) : IRequestHandler<RegisterWithPasswordCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(RegisterWithPasswordCommand r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (!await identity.IsEmailAvailableAsync(email, ct))
            throw new ConflictException("An account with this email already exists. Sign in instead.");
        return await identity.RegisterWithPasswordAsync(r.DisplayName.Trim(), email, r.Password, r.CitySlug, r.AccountType ?? Roles.Customer, ct);
    }
}

// ---------- Refresh / logout / me ----------
public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResultDto>;

public sealed class RefreshTokenHandler(IIdentityService identity) : IRequestHandler<RefreshTokenCommand, AuthResultDto>
{
    public Task<AuthResultDto> Handle(RefreshTokenCommand request, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(request.RefreshToken)
            ? throw new BadRequestException("Refresh token is required.")
            : identity.RefreshAsync(request.RefreshToken, ct);
}

public sealed record LogoutCommand(string RefreshToken) : IRequest;

public sealed class LogoutHandler(IIdentityService identity) : IRequestHandler<LogoutCommand>
{
    public Task Handle(LogoutCommand request, CancellationToken ct) => identity.RevokeAsync(request.RefreshToken, ct);
}

public sealed record GetMeQuery : IRequest<CurrentUserDto>;

public sealed class GetMeHandler(IIdentityService identity, ICurrentUser currentUser) : IRequestHandler<GetMeQuery, CurrentUserDto>
{
    public Task<CurrentUserDto> Handle(GetMeQuery request, CancellationToken ct) =>
        identity.GetCurrentUserAsync(currentUser.UserId ?? throw new ForbiddenAccessException(), ct);
}
