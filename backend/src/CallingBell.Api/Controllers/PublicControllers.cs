using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Auth;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Catalog;
using CallingBell.Application.Features.Content;
using CallingBell.Application.Features.Geo;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Home;
using CallingBell.Application.Features.Media;
using CallingBell.Application.Features.Onboarding;
using CallingBell.Application.Features.Payments;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Geo;
using CallingBell.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace CallingBell.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    public sealed record RefreshRequest(string RefreshToken);

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Login(LoginCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Signed in successfully");

    [HttpPost("register"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Register(RegisterCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Account created");

    /// <summary>Business sign-up wizard: creates the owner account and the business profile in one transaction.</summary>
    [HttpPost("register-business"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<BusinessRegistrationResultDto>>> RegisterBusiness(RegisterBusinessCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Your business account has been created");

    [HttpGet("email-available"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<bool>>> EmailAvailable([FromQuery] string email, CancellationToken ct) =>
        Success(await Sender.Send(new EmailAvailabilityQuery(email ?? string.Empty), ct));

    [HttpPost("google"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Google(GoogleSignInCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Signed in with Google");

    /// <summary>Which external sign-in providers are enabled, with their public client ids.</summary>
    [HttpGet("providers")]
    public ActionResult<ApiResponse<AuthProvidersDto>> Providers([FromServices] IOptions<GoogleAuthOptions> google) =>
        Success(new AuthProvidersDto(string.IsNullOrWhiteSpace(google.Value.ClientId) ? null : google.Value.ClientId));

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Refresh(RefreshRequest request, CancellationToken ct) =>
        Success(await Sender.Send(new RefreshTokenCommand(request.RefreshToken), ct));

    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<object>>> Logout(RefreshRequest request, CancellationToken ct)
    {
        await Sender.Send(new LogoutCommand(request.RefreshToken), ct);
        return Done("Signed out");
    }

    [Authorize, HttpGet("me")]
    public async Task<ActionResult<ApiResponse<CurrentUserDto>>> Me(CancellationToken ct) => Success(await Sender.Send(new GetMeQuery(), ct));
}

[Route("api")]
public sealed class CatalogController : ApiControllerBase
{
    [HttpGet("home")]
    public async Task<ActionResult<ApiResponse<HomeDto>>> Home([FromQuery] string? city, CancellationToken ct) =>
        Success(await Sender.Send(new GetHomeQuery(city), ct));

    [HttpGet("categories")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryDto>>>> Categories(CancellationToken ct) =>
        Success(await Sender.Send(new GetCategoriesQuery(), ct));

    [HttpGet("categories/featured")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SubCategoryDto>>>> FeaturedCategories(CancellationToken ct) =>
        Success(await Sender.Send(new GetFeaturedSubCategoriesQuery(), ct));

    [HttpGet("categories/{slug}")]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> Category(string slug, CancellationToken ct) =>
        Success(await Sender.Send(new GetCategoryBySlugQuery(slug), ct));

    [HttpGet("locations/cities")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CityDto>>>> Cities(CancellationToken ct) =>
        Success(await Sender.Send(new GetCitiesQuery(), ct));

    [HttpGet("lookups")]
    public async Task<ActionResult<ApiResponse<IReadOnlyDictionary<string, IReadOnlyList<LookupDto>>>>> Lookups(CancellationToken ct) =>
        Success(await Sender.Send(new GetLookupsQuery(), ct));

    [HttpGet("plans")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PlanDto>>>> Plans(CancellationToken ct) =>
        Success(await Sender.Send(new GetPlansQuery(), ct));

    [HttpGet("banners")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BannerDto>>>> Banners([FromQuery] string placement = "HomeHero", CancellationToken ct = default) =>
        Success(await Sender.Send(new GetBannersQuery(placement), ct));

    /// <summary>Database-driven marketing page content, e.g. <c>/api/content/pages/ListYourBusiness</c>.</summary>
    [HttpGet("content/pages/{pageKey}")]
    public async Task<ActionResult<ApiResponse<MarketingPageDto>>> MarketingPage(string pageKey, CancellationToken ct) =>
        Success(await Sender.Send(new GetMarketingPageQuery(pageKey), ct));
}

[Route("api/businesses")]
public sealed class BusinessesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusinessCardDto>>>> Search([FromQuery] SearchBusinessesQuery query, CancellationToken ct)
    {
        var result = await Sender.Send(query, ct);
        return Ok(new SearchResponse { Success = true, Data = result.Items, Pagination = result.Pagination, Applied = result.Applied });
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<ApiResponse<BusinessDetailDto>>> Get(string slug, CancellationToken ct) =>
        Success(await Sender.Send(new GetBusinessBySlugQuery(slug), ct));

    [HttpGet("{slug}/reviews")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ReviewDto>>>> Reviews(string slug, [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] int? rating = null, [FromQuery] string? sort = null, CancellationToken ct = default) =>
        Paged(await Sender.Send(new GetBusinessReviewsQuery { Slug = slug, Page = page, PageSize = pageSize, Rating = rating, Sort = sort }, ct));

    [HttpGet("{id:guid}/slots")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SlotDto>>>> Slots(Guid id, [FromQuery] Guid serviceId, [FromQuery] DateTime date, CancellationToken ct) =>
        Success(await Sender.Send(new GetAvailableSlotsQuery(id, serviceId, date.Date), ct));

    [HttpPost("{id:guid}/enquiries"), EnableRateLimiting("submissions")]
    public async Task<ActionResult<ApiResponse<CreatedReferenceDto>>> Enquire(Guid id, CreateEnquiryCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command with { BusinessId = id }, ct), "Your request has been sent to the business");

    [Authorize(Policy = Permissions.ReviewsCreate), HttpPost("{id:guid}/reviews"), EnableRateLimiting("submissions")]
    public async Task<ActionResult<ApiResponse<ReviewDto>>> Review(Guid id, CreateReviewCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command with { BusinessId = id }, ct), "Thank you for your review");

    [Authorize, HttpPost("{id:guid}/favorite")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleFavorite(Guid id, CancellationToken ct)
    {
        var saved = await Sender.Send(new ToggleFavoriteCommand(id), ct);
        return Success(saved, saved ? "Saved to favourites" : "Removed from favourites");
    }

    public sealed class SearchResponse
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "Success";
        public IReadOnlyList<BusinessCardDto> Data { get; init; } = [];
        public PaginationMeta? Pagination { get; init; }
        public AppliedFiltersDto? Applied { get; init; }
    }
}

[Route("api/geo")]
public sealed class GeoController : ApiControllerBase
{
    /// <summary>The visitor's country (ISO code) from a CDN edge header or the local GeoIP database.</summary>
    [HttpGet("country")]
    public async Task<ActionResult<ApiResponse<VisitorCountryDto>>> Country([FromServices] IOptions<GeoIpOptions> options, CancellationToken ct)
    {
        var cdn = Request.Headers["CF-IPCountry"].FirstOrDefault() ?? Request.Headers["CloudFront-Viewer-Country"].FirstOrDefault();
        var ip = options.Value.TrustForwardedFor
            ? Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? HttpContext.Connection.RemoteIpAddress?.ToString()
            : HttpContext.Connection.RemoteIpAddress?.ToString();
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.Vary = "CF-IPCountry, CloudFront-Viewer-Country, X-Forwarded-For";
        return Success(await Sender.Send(new GetVisitorCountryQuery(cdn, ip), ct));
    }
}

[Route("api/payments")]
public sealed class PaymentsController : ApiControllerBase
{
    /// <summary>Whether online checkout is available, and the publishable key for the browser.</summary>
    [HttpGet("config")]
    public async Task<ActionResult<ApiResponse<PaymentConfigDto>>> Config(CancellationToken ct) => Success(await Sender.Send(new GetPaymentConfigQuery(), ct));

    /// <summary>
    /// Razorpay webhook (configure payment.captured, payment.failed and order.paid in the Razorpay dashboard).
    /// Confirms payments even if the customer closed the browser before the checkout callback ran.
    /// </summary>
    [AllowAnonymous, HttpPost("razorpay/webhook")]
    public async Task<IActionResult> RazorpayWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var handled = await Sender.Send(new HandlePaymentWebhookCommand(body, Request.Headers["X-Razorpay-Signature"].ToString()), ct);
        return handled ? Ok() : BadRequest();
    }
}

[Route("api/media")]
public sealed class MediaController : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Serve(id, false, ct);

    [HttpGet("{id:guid}/thumbnail")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> Thumbnail(Guid id, CancellationToken ct) => Serve(id, true, ct);

    // Videos are fetched in many small HTTP range requests; keep recently played large files in memory (bounded) so each
    // range request doesn't reload the whole file from SQL Server.
    private const long LargeFileBytes = 1024 * 1024;
    private static readonly MemoryCache LargeFiles = new(new MemoryCacheOptions { SizeLimit = 256L * 1024 * 1024 });

    private async Task<IActionResult> Serve(Guid id, bool thumbnail, CancellationToken ct)
    {
        var key = (id, thumbnail);
        if (!LargeFiles.TryGetValue(key, out MediaFileDto? file) || file is null)
        {
            file = await Sender.Send(new GetMediaFileQuery(id, thumbnail), ct);
            if (file.Data.LongLength >= LargeFileBytes)
                LargeFiles.Set(key, file, new MemoryCacheEntryOptions { Size = file.Data.LongLength, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        }
        // Stored SVGs are rendered as images only: forbid scripts and external loads.
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";
        Response.Headers.XContentTypeOptions = "nosniff";
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{id:N}-{file.LastModified.UtcTicks:x}{(thumbnail ? "-t" : "")}\"");
        return File(file.Data, file.ContentType, file.LastModified, etag, enableRangeProcessing: true);
    }
}
