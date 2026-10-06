using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Onboarding;
using FluentValidation;
using MediatR;
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
public sealed record OtpChallengeDto(string MaskedPhone, int ExpiresInSeconds, int ResendInSeconds, string? DevelopmentCode);

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
public sealed record SendOtpCommand(string PhoneNumber, string Purpose) : IRequest<OtpChallengeDto>;

public sealed class SendOtpValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage(AuthRules.IndianMobileMessage)
            .Matches(AuthRules.IndianMobile).WithMessage(AuthRules.IndianMobileMessage);
        RuleFor(x => x.Purpose).Must(p => p is nameof(OtpPurpose.SignIn) or nameof(OtpPurpose.SignUp)).WithMessage("Purpose must be SignIn or SignUp.");
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
        return await otp.SendAsync(phone, purpose, ct);
    }
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
