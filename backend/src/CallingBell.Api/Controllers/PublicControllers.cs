using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Auth;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Catalog;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Home;
using CallingBell.Application.Features.Media;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

[Route("api/media")]
public sealed class MediaController : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Serve(id, false, ct);

    [HttpGet("{id:guid}/thumbnail")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public Task<IActionResult> Thumbnail(Guid id, CancellationToken ct) => Serve(id, true, ct);

    private async Task<IActionResult> Serve(Guid id, bool thumbnail, CancellationToken ct)
    {
        var file = await Sender.Send(new GetMediaFileQuery(id, thumbnail), ct);
        // Stored SVGs are rendered as images only: forbid scripts and external loads.
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";
        Response.Headers.XContentTypeOptions = "nosniff";
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{id:N}-{file.LastModified.UtcTicks:x}{(thumbnail ? "-t" : "")}\"");
        return File(file.Data, file.ContentType, file.LastModified, etag);
    }
}
