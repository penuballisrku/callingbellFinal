using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using FluentValidation;
using MediatR;

namespace CallingBell.Application.Features.Auth;

// Settings & account management for the signed-in user.

public sealed record GetAccountSettingsQuery : IRequest<AccountSettingsDto>;

public sealed class GetAccountSettingsHandler(IIdentityService identity, ICurrentUser user) : IRequestHandler<GetAccountSettingsQuery, AccountSettingsDto>
{
    public Task<AccountSettingsDto> Handle(GetAccountSettingsQuery request, CancellationToken ct) =>
        identity.GetAccountSettingsAsync(user.UserId ?? throw new ForbiddenAccessException(), ct);
}

public sealed record UpdateAccountCommand(string DisplayName, string PhoneNumber) : IRequest<CurrentUserDto>;

public sealed class UpdateAccountValidator : AbstractValidator<UpdateAccountCommand>
{
    public UpdateAccountValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter your full name.").Length(2, 120);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$").WithMessage("Enter a valid 10-digit Indian mobile number.");
    }
}

public sealed class UpdateAccountHandler(IIdentityService identity, ICurrentUser user) : IRequestHandler<UpdateAccountCommand, CurrentUserDto>
{
    public Task<CurrentUserDto> Handle(UpdateAccountCommand r, CancellationToken ct) =>
        identity.UpdateAccountAsync(user.UserId ?? throw new ForbiddenAccessException(), r.DisplayName, r.PhoneNumber, ct);
}

public sealed record ChangePasswordCommand(string? CurrentPassword, string NewPassword) : IRequest;

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.");
        RuleFor(x => x.NewPassword).NotEqual(x => x.CurrentPassword).When(x => !string.IsNullOrEmpty(x.CurrentPassword))
            .WithMessage("Choose a password different from your current one.");
    }
}

public sealed class ChangePasswordHandler(IIdentityService identity, ICurrentUser user) : IRequestHandler<ChangePasswordCommand>
{
    public Task Handle(ChangePasswordCommand r, CancellationToken ct) =>
        identity.ChangePasswordAsync(user.UserId ?? throw new ForbiddenAccessException(), r.CurrentPassword, r.NewPassword, ct);
}

/// <summary>Signs the user out everywhere except the current device (identified by its refresh token).</summary>
public sealed record RevokeOtherSessionsCommand(string? CurrentRefreshToken) : IRequest<int>;

public sealed class RevokeOtherSessionsHandler(IIdentityService identity, ICurrentUser user) : IRequestHandler<RevokeOtherSessionsCommand, int>
{
    public Task<int> Handle(RevokeOtherSessionsCommand r, CancellationToken ct) =>
        identity.RevokeOtherSessionsAsync(user.UserId ?? throw new ForbiddenAccessException(), r.CurrentRefreshToken, ct);
}
