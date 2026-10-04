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
using CallingBell.Application.Features.Search;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure.Geo;
using CallingBell.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using CallingBell.Api.Infrastructure;
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
    [HttpGet("home"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<HomeDto>>> Home([FromQuery] string? city, CancellationToken ct) =>
        Success(await Sender.Send(new GetHomeQuery(city), ct));

    [HttpGet("categories"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryDto>>>> Categories(CancellationToken ct) =>
        Success(await Sender.Send(new GetCategoriesQuery(), ct));

    [HttpGet("categories/featured"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SubCategoryDto>>>> FeaturedCategories(CancellationToken ct) =>
        Success(await Sender.Send(new GetFeaturedSubCategoriesQuery(), ct));

    [HttpGet("categories/{slug}"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> Category(string slug, CancellationToken ct) =>
        Success(await Sender.Send(new GetCategoryBySlugQuery(slug), ct));

    /// <summary>
    /// Cities of a country (curated first, then largest first), filled by the city catalogue agent. Pass the visitor's country from
    /// <c>GET /api/geo/country-catalog</c>; without it the default country is used (so responses can be cached per country).
    /// </summary>
    [HttpGet("locations/cities"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CityDto>>>> Cities([FromQuery] string? country, CancellationToken ct) =>
        Success(await Sender.Send(new GetCitiesQuery(country), ct));

    /// <summary>All areas of a city with alternate names and sub-localities; queues the area-discovery agent when the data is missing or stale.</summary>
    [HttpGet("locations/cities/{slug}/areas")]
    public async Task<ActionResult<ApiResponse<CityAreasDto>>> CityAreas(string slug, CancellationToken ct) =>
        Success(await Sender.Send(new GetCityAreasQuery(slug), ct));

    [HttpGet("lookups"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyDictionary<string, IReadOnlyList<LookupDto>>>>> Lookups(CancellationToken ct) =>
        Success(await Sender.Send(new GetLookupsQuery(), ct));

    [HttpGet("plans"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PlanDto>>>> Plans(CancellationToken ct) =>
        Success(await Sender.Send(new GetPlansQuery(), ct));

    [HttpGet("banners"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BannerDto>>>> Banners([FromQuery] string placement = "HomeHero", CancellationToken ct = default) =>
        Success(await Sender.Send(new GetBannersQuery(placement), ct));

    /// <summary>Database-driven marketing page content, e.g. <c>/api/content/pages/ListYourBusiness</c>.</summary>
    [HttpGet("content/pages/{pageKey}"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<MarketingPageDto>>> MarketingPage(string pageKey, CancellationToken ct) =>
        Success(await Sender.Send(new GetMarketingPageQuery(pageKey), ct));

    /// <summary>Search-box autocomplete: categories, services and businesses matching <paramref name="q"/> (empty below 3 characters).</summary>
    [HttpGet("search/suggest"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<SearchSuggestionsDto>>> SearchSuggestions([FromQuery] string? q, [FromQuery] string? city, CancellationToken ct) =>
        Success(await Sender.Send(new GetSearchSuggestionsQuery(q, city), ct));

    /// <summary>
    /// Splits a typed search such as "lawyers in Nellore" into what and where, and matches the place to a listed city or area so the
    /// location dropdown can select it (and the query to a category or sub-category when it names one).
    /// </summary>
    /// <param name="text">The search as typed.</param>
    /// <param name="city">Selected city slug: an area name found in several cities resolves to this city first.</param>
    [HttpGet("search/parse"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<ParsedSearchDto>>> ParseSearch([FromQuery] string? text, [FromQuery] string? city, CancellationToken ct) =>
        Success(await Sender.Send(new ParseSearchQuery(text, city), ct));
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
public sealed class GeoController(IOptions<GeoIpOptions> options, IHostEnvironment env, DevelopmentPublicIp developmentIp) : ApiControllerBase
{
    /// <summary>The visitor's country (ISO code) from a CDN edge header or the local GeoIP database.</summary>
    [HttpGet("country")]
    public async Task<ActionResult<ApiResponse<VisitorCountryDto>>> Country(CancellationToken ct)
    {
        var cdn = Request.Headers["CF-IPCountry"].FirstOrDefault() ?? Request.Headers["CloudFront-Viewer-Country"].FirstOrDefault();
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.Vary = "CF-IPCountry, CloudFront-Viewer-Country, X-Forwarded-For";
        return Success(await Sender.Send(new GetVisitorCountryQuery(cdn, await ClientIpAsync()), ct));
    }

    /// <summary>
    /// The visitor's country (from the CDN header or IP) and its city catalogue: how many cities are listed and whether the city catalogue
    /// agent is still importing them. Queues the import when the country has none yet. Poll while <c>importing</c> is true, then reload
    /// <c>GET /api/locations/cities?country=…</c>.
    /// </summary>
    /// <param name="country">An ISO country code to use instead of the visitor's.</param>
    [HttpGet("country-catalog")]
    public async Task<ActionResult<ApiResponse<CountryCatalogDto>>> CountryCatalog([FromQuery] string? country, CancellationToken ct)
    {
        var cdn = Request.Headers["CF-IPCountry"].FirstOrDefault() ?? Request.Headers["CloudFront-Viewer-Country"].FirstOrDefault();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetCountryCatalogQuery(country, cdn, await ClientIpAsync()), ct));
    }

    /// <summary>
    /// The listed city (district) the visitor is browsing from, located with ip-api.com (local GeoIP city database as fallback), so the city and area dropdowns can
    /// pre-select it and list its areas. District is null when the IP can't be located or isn't near a listed city.
    /// </summary>
    /// <param name="ip">Development only: geolocate this IP instead of the caller's.</param>
    [HttpGet("district")]
    public async Task<ActionResult<ApiResponse<VisitorLocationDto>>> District([FromQuery] string? ip, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.Vary = "X-Forwarded-For";
        return Success(await Sender.Send(new GetVisitorDistrictQuery(clientIp, options.Value.DistrictRadiusKm), ct));
    }

    /// <summary>
    /// Popular services near the visitor's IP location (or the selected <paramref name="area"/>), ranked from recent bookings and, once ready,
    /// re-ranked with reasons by the local AI model. Poll while <c>aiPending</c> is true.
    /// </summary>
    /// <param name="city">City being browsed; when it differs from the IP's city its centre is used.</param>
    /// <param name="area">Area slug within <paramref name="city"/>; overrides the IP location.</param>
    /// <param name="ip">Development only: locate this IP instead of the caller's.</param>
    [HttpGet("nearby-services")]
    public async Task<ActionResult<ApiResponse<NearbyServicesDto>>> NearbyServices([FromQuery] string? city, [FromQuery] string? area, [FromQuery] string? ip,
        CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetNearbyServicesQuery(clientIp, city, area, options.Value.DistrictRadiusKm), ct));
    }

    /// <summary>
    /// Category list for "Top picks", specific to the city detected from the visitor's IP address (<paramref name="fallbackCity"/> only when the IP can't be located):
    /// database-ranked categories immediately, plus AI-suggested additions for that city once ready. Poll while <c>aiPending</c> is true.
    /// </summary>
    /// <param name="fallbackCity">City slug to use when the visitor's IP can't be placed near a listed city.</param>
    /// <param name="ip">Development only: locate this IP instead of the caller's.</param>
    [HttpGet("top-picks")]
    public async Task<ActionResult<ApiResponse<TopPicksDto>>> TopPicks([FromQuery] string? fallbackCity, [FromQuery] string? ip, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetTopPicksQuery(clientIp, fallbackCity, options.Value.DistrictRadiusKm), ct));
    }

    /// <summary>
    /// Real customer reviews of businesses near the visitor (IP area, or the chosen <paramref name="city"/>/<paramref name="area"/>), a
    /// different random set on each call, plus a short AI summary of them once ready. Pass recently shown ids in <paramref name="exclude"/>.
    /// </summary>
    [HttpGet("reviews")]
    public async Task<ActionResult<ApiResponse<LocalReviewsDto>>> Reviews([FromQuery] string? city, [FromQuery] string? area,
        [FromQuery] string? exclude, [FromQuery] string? ip, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        var seen = (exclude ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Take(60)
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetLocalReviewsQuery(clientIp, city, area, options.Value.DistrictRadiusKm, seen), ct));
    }

    /// <summary>
    /// Search results from beyond the platform, shown after the registered businesses from <c>GET /api/businesses</c> (the first priority):
    /// Google Maps businesses when a Places API key is configured, then "AI recommended" real places
    /// near the selected area from OpenStreetMap (picked by the free local AI; poll while <c>ai.aiStatus</c> is "pending").
    /// Each source leaves out places a higher-priority source already listed. Same filters as the business search.
    /// </summary>
    /// <param name="area">Area id (as in the business search); the results are near it and distances are measured from it.</param>
    /// <param name="place">A place named in the search that is not a listed city or area ("lawyers in Nellore"): results are near it instead.</param>
    [HttpGet("external-search"), EnableRateLimiting("public-search")]
    public async Task<ActionResult<ApiResponse<ExternalSearchDto>>> ExternalSearch([FromQuery] string? q, [FromQuery] string? category, [FromQuery] string? sub,
        [FromQuery] string? city, [FromQuery] Guid? area, [FromQuery] string? place, [FromQuery] string? ip, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new ExternalSearchQuery(clientIp, q, category, sub, city, area, options.Value.DistrictRadiusKm, place), ct));
    }

    private async Task<string?> ClientIpAsync()
    {
        var ip = options.Value.TrustForwardedFor
            ? Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? HttpContext.Connection.RemoteIpAddress?.ToString()
            : HttpContext.Connection.RemoteIpAddress?.ToString();
        // Locally the browser connects from 127.0.0.1; geolocate the machine's public IP so detection can be tried in development.
        if (env.IsDevelopment() && VisitorIp.Parse(ip) is null) return await developmentIp.GetAsync() ?? ip;
        return ip;
    }
}

[Route("api/places")]
public sealed class PlacesController : ApiControllerBase
{
    /// <summary>
    /// Google Places Text Search (location bias radius and result count from the GooglePlaces settings), centred on <c>lat</c>/<c>lon</c>,
    /// or on a city or area from the database. 400 for an empty query, invalid coordinates or an unknown city/area; Google's own status and
    /// message when Google rejects the request.
    /// </summary>
    /// <param name="textQuery">What to search for, e.g. a category ("Electricians"); with a city/area, "in {area}, {city}" is added.</param>
    /// <param name="city">City slug, used when <c>lat</c>/<c>lon</c> are not given.</param>
    /// <param name="area">Area id (as in the business search); takes precedence over <c>city</c>.</param>
    /// <param name="place">A place by name that is not a listed city or area (e.g. "Nellore"), used when no city or area is given.</param>
    /// <param name="pageToken">The previous page's <c>nextPageToken</c>; send the same query and location with it.</param>
    [HttpGet("search"), EnableRateLimiting("public-search")]
    public async Task<ActionResult<ApiResponse<GooglePlacesSearchDto>>> Search([FromQuery] string? textQuery, [FromQuery] double? lat, [FromQuery] double? lon,
        [FromQuery] string? city, [FromQuery] Guid? area, [FromQuery] string? place, [FromQuery] string? pageToken, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GooglePlacesSearchQuery(textQuery, lat, lon, city, area, pageToken, place), ct));
    }

    /// <summary>Redirects to a place photo from <c>GET /api/places/search</c> (photo URLs there point here, so the API key stays on the server).</summary>
    [HttpGet("photo")]
    public async Task<IActionResult> Photo([FromQuery] string? name, [FromQuery] int maxWidth = 640, CancellationToken ct = default)
    {
        var uri = await Sender.Send(new GooglePlacePhotoQuery(name, maxWidth), ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        return Redirect(uri);
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
