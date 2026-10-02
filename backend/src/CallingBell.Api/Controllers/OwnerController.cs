using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Owner;
using CallingBell.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallingBell.Api.Controllers;

/// <summary>Business portal. Every handler verifies the caller owns the business.</summary>
[Authorize(Policy = Permissions.OwnBusinessManage), Route("api/owner")]
public sealed class OwnerController : ApiControllerBase
{
    public sealed record LeadUpdate(string Status, decimal? QuotedAmount);
    public sealed record BookingUpdate(string Status, string? Reason);
    public sealed record ReplyRequest(string Reply);
    public sealed record AvailabilityRequest(string Status);

    [HttpGet("businesses")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OwnerBusinessDto>>>> Businesses(CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerBusinessesQuery(), ct));

    [HttpGet("businesses/{id:guid}/dashboard")]
    public async Task<ActionResult<ApiResponse<OwnerDashboardDto>>> Dashboard(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerDashboardQuery(id), ct));

    [HttpGet("businesses/{id:guid}/leads")]
    public async Task<ActionResult<ApiResponse<OwnerListResult<OwnerLeadDto>>>> Leads(Guid id, [FromQuery] GetOwnerLeadsQuery query, CancellationToken ct) =>
        Success(await Sender.Send(query with { BusinessId = id }, ct));

    [HttpPatch("businesses/{id:guid}/leads/{leadId:guid}")]
    public async Task<ActionResult<ApiResponse<OwnerLeadDto>>> UpdateLead(Guid id, Guid leadId, LeadUpdate body, CancellationToken ct) =>
        Success(await Sender.Send(new UpdateLeadCommand(id, leadId, body.Status, body.QuotedAmount), ct), "Lead updated");

    [HttpGet("businesses/{id:guid}/bookings")]
    public async Task<ActionResult<ApiResponse<OwnerListResult<OwnerBookingDto>>>> Bookings(Guid id, [FromQuery] GetOwnerBookingsQuery query, CancellationToken ct) =>
        Success(await Sender.Send(query with { BusinessId = id }, ct));

    [HttpPatch("businesses/{id:guid}/bookings/{bookingId:guid}")]
    public async Task<ActionResult<ApiResponse<OwnerBookingDto>>> UpdateBooking(Guid id, Guid bookingId, BookingUpdate body, CancellationToken ct) =>
        Success(await Sender.Send(new UpdateBookingStatusCommand(id, bookingId, body.Status, body.Reason), ct), $"Booking {body.Status.ToLowerInvariant()}");

    [HttpGet("businesses/{id:guid}/reviews")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OwnerReviewDto>>>> Reviews(Guid id, [FromQuery] GetOwnerReviewsQuery query, CancellationToken ct) =>
        Paged(await Sender.Send(query with { BusinessId = id }, ct));

    [HttpPost("businesses/{id:guid}/reviews/{reviewId:guid}/reply")]
    public async Task<ActionResult<ApiResponse<object>>> Reply(Guid id, Guid reviewId, ReplyRequest body, CancellationToken ct)
    {
        await Sender.Send(new ReplyToReviewCommand(id, reviewId, body.Reply), ct);
        return Done("Reply posted");
    }

    [HttpPut("businesses/{id:guid}/availability")]
    public async Task<ActionResult<ApiResponse<string>>> Availability(Guid id, AvailabilityRequest body, CancellationToken ct) =>
        Success(await Sender.Send(new UpdateAvailabilityCommand(id, body.Status), ct), "Availability updated");

    [HttpGet("businesses/{id:guid}/services")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OwnerServiceDto>>>> Services(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerServicesQuery(id), ct));

    [HttpPost("businesses/{id:guid}/services")]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateService(Guid id, UpsertServiceCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command with { BusinessId = id, ServiceId = null }, ct), "Service added");

    [HttpPut("businesses/{id:guid}/services/{serviceId:guid}")]
    public async Task<ActionResult<ApiResponse<Guid>>> UpdateService(Guid id, Guid serviceId, UpsertServiceCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command with { BusinessId = id, ServiceId = serviceId }, ct), "Service updated");

    [HttpGet("businesses/{id:guid}/profile")]
    public async Task<ActionResult<ApiResponse<OwnerProfileDto>>> Profile(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerProfileQuery(id), ct));

    [HttpPut("businesses/{id:guid}/profile")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateProfile(Guid id, UpdateOwnerProfileCommand command, CancellationToken ct)
    {
        await Sender.Send(command with { BusinessId = id }, ct);
        return Done("Profile saved");
    }

    [HttpGet("businesses/{id:guid}/subscription")]
    public async Task<ActionResult<ApiResponse<OwnerSubscriptionDto>>> Subscription(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerSubscriptionQuery(id), ct));

    [HttpGet("businesses/{id:guid}/advertisements")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OwnerAdDto>>>> Ads(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerAdsQuery(id), ct));

    [HttpPost("businesses/{id:guid}/advertisements")]
    public async Task<ActionResult<ApiResponse<CreatedReferenceDto>>> CreateAd(Guid id, CreateAdCampaignCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command with { BusinessId = id }, ct), "Campaign submitted for approval");
}
