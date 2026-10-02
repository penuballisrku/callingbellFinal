using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Auth;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Notifications;
using CallingBell.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CallingBell.Api.Controllers;

/// <summary>Endpoints for the signed-in customer.</summary>
[Authorize, Route("api/me")]
public sealed class AccountController : ApiControllerBase
{
    public sealed record CancelRequest(string? Reason);

    [HttpGet("bookings")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MyBookingDto>>>> Bookings([FromQuery] GetMyBookingsQuery query, CancellationToken ct) =>
        Paged(await Sender.Send(query, ct));

    [Authorize(Policy = Permissions.BookingsCreate), HttpPost("bookings"), EnableRateLimiting("submissions")]
    public async Task<ActionResult<ApiResponse<CreatedReferenceDto>>> Book(CreateBookingCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Booking requested. The business will confirm shortly.");

    [HttpPost("bookings/{id:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<object>>> Cancel(Guid id, CancelRequest request, CancellationToken ct)
    {
        await Sender.Send(new CancelMyBookingCommand(id, request.Reason), ct);
        return Done("Booking cancelled");
    }

    [HttpGet("favorites")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusinessCardDto>>>> Favorites(CancellationToken ct) =>
        Success(await Sender.Send(new GetMyFavoritesQuery(), ct));

    [HttpGet("enquiries")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MyEnquiryDto>>>> Enquiries([FromQuery] GetMyEnquiriesQuery query, CancellationToken ct) =>
        Paged(await Sender.Send(query, ct));

    [HttpGet("notifications")]
    public async Task<ActionResult<ApiResponse<NotificationListDto>>> Notifications([FromQuery] int take = 20, CancellationToken ct = default) =>
        Success(await Sender.Send(new GetMyNotificationsQuery(take), ct));

    [HttpPost("notifications/read")]
    public async Task<ActionResult<ApiResponse<object>>> MarkRead(CancellationToken ct)
    {
        await Sender.Send(new MarkNotificationsReadCommand(), ct);
        return Done();
    }

    // ---------- Settings & account management ----------

    public sealed record SessionsRequest(string? CurrentRefreshToken);

    [HttpGet("account")]
    public async Task<ActionResult<ApiResponse<AccountSettingsDto>>> Account(CancellationToken ct) =>
        Success(await Sender.Send(new GetAccountSettingsQuery(), ct));

    [HttpPut("account")]
    public async Task<ActionResult<ApiResponse<CurrentUserDto>>> UpdateAccount(UpdateAccountCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Account details saved");

    [HttpPost("account/password"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(ChangePasswordCommand command, CancellationToken ct)
    {
        await Sender.Send(command, ct);
        return Done("Password updated");
    }

    [HttpPost("account/sessions/revoke")]
    public async Task<ActionResult<ApiResponse<int>>> RevokeOtherSessions(SessionsRequest request, CancellationToken ct)
    {
        var revoked = await Sender.Send(new RevokeOtherSessionsCommand(request.CurrentRefreshToken), ct);
        return Success(revoked, revoked == 0 ? "No other active sessions" : $"Signed out of {revoked} other session{(revoked == 1 ? "" : "s")}");
    }
}
