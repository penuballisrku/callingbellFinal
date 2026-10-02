using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using FluentValidation;
using MediatR;

namespace CallingBell.Application.Features.Auth;

public sealed record CurrentUserDto(
    string Id, string Email, string DisplayName, string? PhoneNumber, string? AvatarUrl, string UserType,
    string? CitySlug, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record AuthResultDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, CurrentUserDto User);

public sealed record RegisterRequest(string DisplayName, string Email, string PhoneNumber, string Password, string AccountType, string? CitySlug);

// ---------- Login ----------
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

// ---------- Register ----------
public sealed record RegisterCommand(string DisplayName, string Email, string PhoneNumber, string Password, string AccountType, string? CitySlug)
    : IRequest<AuthResultDto>;

public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$")
            .WithMessage("Enter a valid 10-digit Indian mobile number.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.");
        RuleFor(x => x.AccountType).Must(t => t is "Customer" or "BusinessOwner")
            .WithMessage("Account type must be Customer or BusinessOwner.");
    }
}

public sealed class RegisterHandler(IIdentityService identity) : IRequestHandler<RegisterCommand, AuthResultDto>
{
    public Task<AuthResultDto> Handle(RegisterCommand r, CancellationToken ct) =>
        identity.RegisterAsync(new RegisterRequest(r.DisplayName.Trim(), r.Email.Trim(), r.PhoneNumber.Trim(), r.Password, r.AccountType, r.CitySlug), ct);
}

// ---------- Google sign-in ----------
public sealed record GoogleSignInCommand(string IdToken, string? AccountType) : IRequest<AuthResultDto>;

public sealed class GoogleSignInValidator : AbstractValidator<GoogleSignInCommand>
{
    public GoogleSignInValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty().WithMessage("Google sign-in did not return a credential.");
        RuleFor(x => x.AccountType).Must(t => t is null or "Customer" or "BusinessOwner")
            .WithMessage("Account type must be Customer or BusinessOwner.");
    }
}

public sealed class GoogleSignInHandler(IIdentityService identity) : IRequestHandler<GoogleSignInCommand, AuthResultDto>
{
    public Task<AuthResultDto> Handle(GoogleSignInCommand request, CancellationToken ct) =>
        identity.ExternalGoogleAsync(request.IdToken, request.AccountType ?? "Customer", ct);
}

/// <summary>Public client configuration for sign-in providers (no secrets).</summary>
public sealed record AuthProvidersDto(string? GoogleClientId);

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
