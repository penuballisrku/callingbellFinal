using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Onboarding;
using CallingBell.Application.Features.Owner;
using CallingBell.Application.Features.Payments;
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

    /// <summary>Creates a business for a signed-in owner who has none yet (e.g. signed up with Google).</summary>
    [HttpPost("businesses")]
    public async Task<ActionResult<ApiResponse<CreatedBusinessDto>>> CreateBusiness(CreateOwnerBusinessCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Your business has been created");

    [HttpGet("businesses/{id:guid}/overview")]
    public async Task<ActionResult<ApiResponse<OwnerOverviewDto>>> Overview(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerOverviewQuery(id), ct));

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

    // ---------- Media ----------

    public sealed class MediaUploadForm
    {
        public string Kind { get; set; } = string.Empty;
        public IFormFile? File { get; set; }
        /// <summary>Smaller rendition for images, or the poster frame for videos.</summary>
        public IFormFile? Thumbnail { get; set; }
        public string? Title { get; set; }
        public int? DurationSeconds { get; set; }
    }

    private const long UploadLimit = MediaRules.MaxVideoBytes + MediaRules.MaxThumbnailBytes + 1024 * 1024;

    [HttpGet("businesses/{id:guid}/media")]
    public async Task<ActionResult<ApiResponse<OwnerMediaDto>>> Media(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetOwnerMediaQuery(id), ct));

    [HttpPost("businesses/{id:guid}/media"), RequestSizeLimit(UploadLimit), RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<ActionResult<ApiResponse<OwnerMediaItemDto>>> UploadMedia(Guid id, [FromForm] MediaUploadForm form, CancellationToken ct)
    {
        var data = form.File is null ? [] : await ReadAsync(form.File, ct);
        var thumbnail = form.Thumbnail is null ? null : await ReadAsync(form.Thumbnail, ct);
        var item = await Sender.Send(new UploadBusinessMediaCommand(id, form.Kind, form.File?.FileName ?? "upload", data, thumbnail, form.Title, form.DurationSeconds), ct);
        return Success(item, "Uploaded");
    }

    [HttpDelete("businesses/{id:guid}/media/{kind}/{itemId:guid?}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteMedia(Guid id, string kind, Guid? itemId, CancellationToken ct)
    {
        await Sender.Send(new DeleteBusinessMediaCommand(id, kind, itemId), ct);
        return Done("Removed");
    }

    [HttpPut("businesses/{id:guid}/media/photos/{photoId:guid}/primary")]
    public async Task<ActionResult<ApiResponse<object>>> SetPrimaryPhoto(Guid id, Guid photoId, CancellationToken ct)
    {
        await Sender.Send(new SetPrimaryPhotoCommand(id, photoId), ct);
        return Done("Main photo updated");
    }

    // ---------- Social links ----------

    public sealed record SocialLinksRequest(IReadOnlyList<OnboardingSocialLinkInput> Links);

    [HttpGet("businesses/{id:guid}/social-links")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SocialLinkDto>>>> SocialLinks(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetSocialLinksQuery(id), ct));

    [HttpPut("businesses/{id:guid}/social-links")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SocialLinkDto>>>> UpdateSocialLinks(Guid id, SocialLinksRequest request, CancellationToken ct) =>
        Success(await Sender.Send(new UpdateSocialLinksCommand(id, request.Links ?? []), ct), "Social links saved");

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    // ---------- Plan payments (Razorpay) ----------

    public sealed record PlanOrderRequest(string PlanCode, string BillingCycle, string? Gstin);
    public sealed record VerifyPaymentRequest(string GatewayOrderId, string GatewayPaymentId, string Signature);
    public sealed record CheckoutOutcomeRequest(string Outcome, string? Reason);

    [HttpPost("businesses/{id:guid}/payments/orders")]
    public async Task<ActionResult<ApiResponse<CheckoutOrderDto>>> CreatePlanOrder(Guid id, PlanOrderRequest request, CancellationToken ct) =>
        Success(await Sender.Send(new CreatePlanOrderCommand(id, request.PlanCode, request.BillingCycle, request.Gstin), ct));

    [HttpPost("businesses/{id:guid}/payments/orders/{orderId:guid}/verify")]
    public async Task<ActionResult<ApiResponse<PaymentResultDto>>> VerifyPayment(Guid id, Guid orderId, VerifyPaymentRequest request, CancellationToken ct) =>
        Success(await Sender.Send(new VerifyPlanPaymentCommand(id, orderId, request.GatewayOrderId, request.GatewayPaymentId, request.Signature), ct), "Payment successful");

    [HttpPost("businesses/{id:guid}/payments/orders/{orderId:guid}/outcome")]
    public async Task<ActionResult<ApiResponse<PaymentResultDto>>> CheckoutOutcome(Guid id, Guid orderId, CheckoutOutcomeRequest request, CancellationToken ct) =>
        Success(await Sender.Send(new RecordCheckoutOutcomeCommand(id, orderId, request.Outcome, request.Reason), ct));
}
