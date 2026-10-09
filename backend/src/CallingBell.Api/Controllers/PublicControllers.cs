using CallingBell.Application.Common.Models;
using CallingBell.Application.Common.Interfaces;
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

    /// <summary>
    /// Sends a one-time code: on WhatsApp (authentication template) first, by SMS if WhatsApp can't deliver it. The answer is the same
    /// whether or not the number has an account. Limits: a few codes per number and per IP address every 15 minutes.
    /// </summary>
    [HttpPost("request-otp"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<RequestOtpResultDto>>> RequestOtp(RequestOtpCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "If the number can receive verification, an OTP has been sent.");

    /// <summary>Checks the code (5 attempts per code): LOGIN signs in; SIGNUP returns the verification token for registration.</summary>
    [HttpPost("verify-otp"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<VerifyOtpResultDto>>> VerifyOtpCode(VerifyOtpCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Code verified");

    /// <summary>Texts a one-time code to a mobile number. Purpose "SignIn" needs an existing account; "SignUp" needs an unused number.</summary>
    [HttpPost("otp/send"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<OtpChallengeDto>>> SendOtp(SendOtpCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Verification code sent");

    [HttpPost("otp/sign-in"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> OtpSignIn(OtpSignInCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Signed in successfully");

    /// <summary>Verifies a sign-up code; the returned token is sent with <c>register</c> or <c>register-business</c>.</summary>
    [HttpPost("otp/verify"), EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<PhoneVerificationDto>>> VerifyOtp(VerifySignUpPhoneCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct), "Mobile number verified");

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

    /// <summary>Services businesses in this sub-category commonly offer, with typical price and duration (to pre-fill business sign-up).</summary>
    [HttpGet("subcategories/{slug}/service-suggestions"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ServiceSuggestionDto>>>> ServiceSuggestions(string slug, CancellationToken ct) =>
        Success(await Sender.Send(new GetServiceSuggestionsQuery(slug), ct));

    [HttpGet("categories/{slug}"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> Category(string slug, CancellationToken ct) =>
        Success(await Sender.Send(new GetCategoryBySlugQuery(slug), ct));

    /// <summary>
    /// Cities of a country (curated first, then largest first), filled by the city catalogue agent. Pass the visitor's country from
    /// <c>GET /api/geo/country-catalog</c>; without it the default country is used (so responses can be cached per country).
    /// </summary>
    /// <param name="state">A state / province / region slug (GET /api/locations/states): only its cities.</param>
    [HttpGet("locations/cities"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CityDto>>>> Cities([FromQuery] string? country, [FromQuery] string? state, CancellationToken ct) =>
        Success(await Sender.Send(new GetCitiesQuery(country, state), ct));

    /// <summary>Countries a visitor can browse (every country plans are priced for), with how many cities each has listed so far.</summary>
    [HttpGet("locations/countries"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CountryDto>>>> Countries(CancellationToken ct) =>
        Success(await Sender.Send(new GetCountriesQuery(), ct));

    /// <summary>A country's states / provinces / regions that have listed cities.</summary>
    /// <param name="country">ISO 3166-1 alpha-2 code, e.g. "CA".</param>
    [HttpGet("locations/states"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StateDto>>>> States([FromQuery] string country, CancellationToken ct) =>
        Success(await Sender.Send(new GetStatesQuery(country), ct));

    /// <summary>All areas of a city with alternate names and sub-localities; queues the area-discovery agent when the data is missing or stale.</summary>
    [HttpGet("locations/cities/{slug}/areas"), OutputCache(PolicyName = CachePolicies.PublicListings)]
    public async Task<ActionResult<ApiResponse<CityAreasDto>>> CityAreas(string slug, CancellationToken ct)
    {
        var areas = await Sender.Send(new GetCityAreasQuery(slug), ct);
        // While the agent is still finding the city's areas the list changes every few seconds: callers poll, so don't cache it.
        if ((areas.Discovering || areas.Areas.Count == 0) && HttpContext.Features.Get<IOutputCacheFeature>() is { } cacheFeature)
            cacheFeature.Context.AllowCacheStorage = false;
        return Success(areas);
    }

    [HttpGet("lookups"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyDictionary<string, IReadOnlyList<LookupDto>>>>> Lookups(CancellationToken ct) =>
        Success(await Sender.Send(new GetLookupsQuery(), ct));

    /// <summary>Subscription plans, priced in a country's currency at fair local prices (CountryPricing).</summary>
    /// <param name="country">ISO country code, e.g. "US"; omit for rupees as set.</param>
    /// <param name="business">A business id: prices for the country it is in (the owner portal). Takes precedence over <paramref name="country"/>.</param>
    [HttpGet("plans"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PlanDto>>>> Plans([FromQuery] string? country, [FromQuery] Guid? business, CancellationToken ct) =>
        Success(await Sender.Send(new GetPlansQuery(country, business), ct));

    [HttpGet("banners"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BannerDto>>>> Banners([FromQuery] string placement = "HomeHero", CancellationToken ct = default) =>
        Success(await Sender.Send(new GetBannersQuery(placement), ct));

    /// <summary>Database-driven marketing page content, e.g. <c>/api/content/pages/ListYourBusiness</c>.</summary>
    /// <param name="country">ISO code of the country being browsed: its own photos replace the defaults where the page has them.</param>
    [HttpGet("content/pages/{pageKey}"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<MarketingPageDto>>> MarketingPage(string pageKey, [FromQuery] string? country, CancellationToken ct) =>
        Success(await Sender.Send(new GetMarketingPageQuery(pageKey, country), ct));

    /// <summary>Search-box autocomplete: categories, services and businesses matching <paramref name="q"/> (empty below 3 characters).</summary>
    [HttpGet("search/suggest"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<SearchSuggestionsDto>>> SearchSuggestions([FromQuery] string? q, [FromQuery] string? city, CancellationToken ct) =>
        Success(await Sender.Send(new GetSearchSuggestionsQuery(q, city), ct));

    /// <summary>
    /// "Suggested by AI": sub-categories a phrase means ("water dripping from ceiling" gives Plumbers), from the local embedding model.
    /// Not output-cached: while the model is busy the answer is empty, and a moment later it isn't (the server caches vectors itself).
    /// </summary>
    [HttpGet("search/suggest/ai")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SearchSuggestionDto>>>> MeaningSuggestions([FromQuery] string? q, CancellationToken ct) =>
        Success(await Sender.Send(new GetMeaningSuggestionsQuery(q), ct));

    /// <summary>
    /// Splits a typed search such as "lawyers in Nellore" into what and where, and matches the place to a listed city or area so the
    /// location dropdown can select it (and the query to a category or sub-category when it names one).
    /// </summary>
    /// <param name="text">The search as typed.</param>
    /// <param name="city">Selected city slug: an area name found in several cities resolves to this city first.</param>
    [HttpGet("search/parse"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<ParsedSearchDto>>> ParseSearch([FromQuery] string? text, [FromQuery] string? city, CancellationToken ct) =>
        Success(await Sender.Send(new ParseSearchQuery(text, city), ct));

    /// <summary>
    /// AI search assistant: answers a request in plain words ("AC not cooling, need someone today in Madhapur") with the matching
    /// businesses and the filters it applied. Send the previous answer's <c>filters</c> as <c>context</c> to refine it. When the answer
    /// says <c>aiPending</c>, send the same request again in a few seconds for the AI's interpretation.
    /// </summary>
    /// <summary>Example requests for the assistant, built from the services listed in <paramref name="city"/> (or elsewhere when it has none).</summary>
    [HttpGet("search/assistant/starters"), OutputCache(PolicyName = CachePolicies.PublicCatalog)]
    public async Task<ActionResult<ApiResponse<AssistantStartersDto>>> SearchAssistantStarters([FromQuery] string? city, CancellationToken ct) =>
        Success(await Sender.Send(new GetAssistantStartersQuery(city), ct));

    [HttpPost("search/assistant"), EnableRateLimiting("assistant")]
    public async Task<ActionResult<ApiResponse<AssistantReplyDto>>> SearchAssistant(AskSearchAssistantCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command, ct));
}

[Route("api/businesses")]
public sealed class BusinessesController : ApiControllerBase
{
    [HttpGet, OutputCache(PolicyName = CachePolicies.PublicListings)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusinessCardDto>>>> Search([FromQuery] SearchBusinessesQuery query, CancellationToken ct)
    {
        var result = await Sender.Send(query, ct);
        return Ok(new SearchResponse { Success = true, Data = result.Items, Pagination = result.Pagination, Applied = result.Applied });
    }

    [HttpGet("{slug}"), OutputCache(PolicyName = CachePolicies.PublicListings)]
    public async Task<ActionResult<ApiResponse<BusinessDetailDto>>> Get(string slug, CancellationToken ct) =>
        Success(await Sender.Send(new GetBusinessBySlugQuery(slug), ct));

    [HttpGet("{slug}/reviews"), OutputCache(PolicyName = CachePolicies.PublicListings)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ReviewDto>>>> Reviews(string slug, [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] int? rating = null, [FromQuery] string? sort = null, CancellationToken ct = default) =>
        Paged(await Sender.Send(new GetBusinessReviewsQuery { Slug = slug, Page = page, PageSize = pageSize, Rating = rating, Sort = sort }, ct));

    /// <param name="staffId">Only times when this team member is free.</param>
    [HttpGet("{id:guid}/slots")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SlotDto>>>> Slots(Guid id, [FromQuery] Guid serviceId, [FromQuery] DateTime date,
        [FromQuery] Guid? staffId, CancellationToken ct) =>
        Success(await Sender.Send(new GetAvailableSlotsQuery(id, serviceId, date.Date, staffId), ct));

    /// <summary>The business's team as customers see it (names, roles, experience and services; never contact details).</summary>
    [HttpGet("{id:guid}/team")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CallingBell.Application.Features.Staff.TeamMemberDto>>>> Team(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new CallingBell.Application.Features.Staff.GetBusinessTeamQuery(id), ct));

    /// <summary>
    /// "Claim business": asks to take over an existing listing (e.g. one found while joining Calling Bell). An administrator verifies the
    /// person before transferring it. Signed-in or not.
    /// </summary>
    [HttpPost("{id:guid}/claim-requests"), EnableRateLimiting("submissions")]
    public async Task<ActionResult<ApiResponse<CallingBell.Application.Features.Onboarding.BusinessClaimResultDto>>> Claim(Guid id,
        CallingBell.Application.Features.Onboarding.CreateBusinessClaimCommand command, CancellationToken ct) =>
        Success(await Sender.Send(command with { BusinessId = id }, ct), "Claim request sent. Our team will verify it and get in touch.");

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
public sealed class GeoController(IOptions<GeoIpOptions> options, IHostEnvironment env, DevelopmentPublicIp developmentIp, IGeoLocationService geo)
    : ApiControllerBase
{
    /// <summary>The visitor's country (ISO code) from a CDN edge header or the local GeoIP database.</summary>
    [HttpGet("country")]
    public async Task<ActionResult<ApiResponse<VisitorCountryDto>>> Country(CancellationToken ct)
    {
        var cdn = Request.Headers["CF-IPCountry"].FirstOrDefault() ?? Request.Headers["CloudFront-Viewer-Country"].FirstOrDefault();
        Response.Headers.CacheControl = GeoCacheControl;
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
    /// The visitor's location from their IP address in one request - country (and whether its cities are still being imported; poll while
    /// <c>importing</c>), state / province / region, city and area - located with ip-api.com (local GeoIP databases as fallback), so the
    /// location picker can fill itself in. District is null when the IP can't be located or isn't near a listed city. The IP itself is
    /// never returned.
    /// </summary>
    /// <param name="ip">Development only: geolocate this IP instead of the caller's.</param>
    [HttpGet("district")]
    public async Task<ActionResult<ApiResponse<VisitorLocationDto>>> District([FromQuery] string? ip, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        var cdn = Request.Headers["CF-IPCountry"].FirstOrDefault() ?? Request.Headers["CloudFront-Viewer-Country"].FirstOrDefault();
        // Never kept by the browser or a proxy: a new IP address (another network, a VPN) must give a new location. The server keeps the
        // IP service's answer per address, so asking again is cheap.
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetVisitorDistrictQuery(clientIp, options.Value.DistrictRadiusKm, cdn), ct));
    }

    /// <summary>
    /// Popular services near the visitor's IP location (or the selected <paramref name="area"/>), ranked from recent bookings and, once ready,
    /// re-ranked with reasons by the local AI model. Poll while <c>aiPending</c> is true.
    /// </summary>
    /// <param name="city">City being browsed; when it differs from the IP's city its centre is used.</param>
    /// <param name="area">Area slug within <paramref name="city"/>; overrides the IP location.</param>
    /// <param name="ip">Development only: locate this IP instead of the caller's.</param>
    /// <param name="country">The country being browsed (ISO code): with no city, the IP's location is used only when it is in this country.</param>
    [HttpGet("nearby-services")]
    public async Task<ActionResult<ApiResponse<NearbyServicesDto>>> NearbyServices([FromQuery] string? city, [FromQuery] string? area, [FromQuery] string? ip,
        [FromQuery] string? country, CancellationToken ct)
    {
        var clientIp = BrowsingIp(env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync(), city, country);
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetNearbyServicesQuery(clientIp, city, area, options.Value.DistrictRadiusKm), ct));
    }

    /// <summary>
    /// Category list for "Top picks", specific to the city detected from the visitor's IP address (<paramref name="fallbackCity"/> only when the IP can't be located):
    /// database-ranked categories immediately, plus AI-suggested additions for that city once ready. Poll while <c>aiPending</c> is true.
    /// </summary>
    /// <param name="fallbackCity">City slug to use when the visitor's IP can't be placed near a listed city.</param>
    /// <param name="city">The city chosen in the selector; takes precedence over the IP.</param>
    /// <param name="ip">Development only: locate this IP instead of the caller's.</param>
    /// <param name="country">The country being browsed (ISO code): with no city, the IP's location is used only when it is in this country.</param>
    [HttpGet("top-picks")]
    public async Task<ActionResult<ApiResponse<TopPicksDto>>> TopPicks([FromQuery] string? fallbackCity, [FromQuery] string? city, [FromQuery] string? ip,
        [FromQuery] string? country, CancellationToken ct)
    {
        var clientIp = BrowsingIp(env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync(), city, country);
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetTopPicksQuery(clientIp, fallbackCity, options.Value.DistrictRadiusKm, city), ct));
    }

    /// <summary>
    /// Real customer reviews of businesses near the visitor (IP area, or the chosen <paramref name="city"/>/<paramref name="area"/>), a
    /// different random set on each call, plus a short AI summary of them once ready. Pass recently shown ids in <paramref name="exclude"/>.
    /// Widens to the state, then the country, then everywhere when the place has too few reviews.
    /// </summary>
    /// <param name="country">The country being browsed (ISO code), used when no <paramref name="city"/> is chosen.</param>
    [HttpGet("reviews")]
    public async Task<ActionResult<ApiResponse<LocalReviewsDto>>> Reviews([FromQuery] string? city, [FromQuery] string? area,
        [FromQuery] string? exclude, [FromQuery] string? ip, [FromQuery] string? country, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        var seen = (exclude ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Take(60)
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetLocalReviewsQuery(clientIp, city, area, options.Value.DistrictRadiusKm, seen, country), ct));
    }

    /// <summary>
    /// Search results from beyond the platform, shown after the registered businesses from <c>GET /api/businesses</c> (the first priority):
    /// Google Maps businesses when a Places API key is configured, then "AI recommended" real places
    /// near the selected area from OpenStreetMap (picked by the free local AI; poll while <c>ai.aiStatus</c> is "pending").
    /// Each source leaves out places a higher-priority source already listed. Same filters as the business search.
    /// </summary>
    /// <param name="area">Area id (as in the business search); the results are near it and distances are measured from it.</param>
    /// <param name="place">A place named in the search that is not a listed city or area ("lawyers in Nellore"): results are near it instead.</param>
    /// <param name="tiers">"google" for the Google Maps results only (ask once per search); "ai" for the AI tier only (see the POST); omit for both.</param>
    /// <param name="googlePage">The Google tier's nextPageToken: the next 20 Google Maps results of the same search (with tiers=google).</param>
    [HttpGet("external-search"), EnableRateLimiting("public-search")]
    public async Task<ActionResult<ApiResponse<ExternalSearchDto>>> ExternalSearch([FromQuery] string? q, [FromQuery] string? category, [FromQuery] string? sub,
        [FromQuery] string? city, [FromQuery] Guid? area, [FromQuery] string? place, [FromQuery] string? ip, [FromQuery] string? tiers,
        [FromQuery] string? googlePage, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new ExternalSearchQuery(clientIp, q, category, sub, city, area, options.Value.DistrictRadiusKm, place, tiers,
            GooglePageToken: googlePage), ct));
    }

    /// <summary>
    /// The AI recommended tier on its own, for polling while the AI works: same search as the GET, plus the Google Maps results the page
    /// already shows (from <c>tiers=google</c>), so Google isn't called again on every poll. Those are used for this request only (to leave
    /// out duplicates and for the AI overview) and are not stored.
    /// </summary>
    [HttpPost("external-search/ai"), EnableRateLimiting("public-search")]
    public async Task<ActionResult<ApiResponse<ExternalSearchDto>>> ExternalSearchAi(ExternalSearchAiRequest body, [FromQuery] string? ip, CancellationToken ct)
    {
        var clientIp = env.IsDevelopment() && !string.IsNullOrWhiteSpace(ip) ? ip : await ClientIpAsync();
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new ExternalSearchQuery(clientIp, body.Q, body.Category, body.Sub, body.City, body.Area,
            options.Value.DistrictRadiusKm, body.Place, ExternalTiers.Ai, body.GooglePlaces), ct));
    }

    public sealed record ExternalSearchAiRequest(string? Q, string? Category, string? Sub, string? City, Guid? Area, string? Place,
        IReadOnlyList<ShownGooglePlace>? GooglePlaces);

    /// <summary>
    /// The IP to locate the visitor by, or null when they browse another country than the IP's with no city chosen: the IP's city
    /// says nothing about the place being browsed then.
    /// </summary>
    private string? BrowsingIp(string? clientIp, string? city, string? country)
    {
        if (!string.IsNullOrWhiteSpace(city) || country is not { Length: 2 } || VisitorIp.Parse(clientIp) is not { } ip) return clientIp;
        var ipCountry = geo.CountryCodeFor(ip);
        return ipCountry is null || string.Equals(ipCountry, country, StringComparison.OrdinalIgnoreCase) ? clientIp : null;
    }

    // An hour in the browser; not cached in development, so a changed GeoIp:DevelopmentClientIp shows on the next reload.
    private string GeoCacheControl => env.IsDevelopment() ? "private, no-store" : "private, max-age=3600";

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

    /// <summary>
    /// "Popular searches" for Explore nearby, from dbo.PopularSearches: the entries for every country plus those for <paramref name="country"/>,
    /// most searched first. Each names what to search for and, when it is a sub-category, which one.
    /// </summary>
    /// <param name="country">ISO country code of the country being browsed.</param>
    /// <param name="limit">How many to return (1-60).</param>
    [HttpGet("popular-searches")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PopularSearchDto>>>> PopularSearches([FromQuery] string? country, [FromQuery] int limit = 32,
        CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return Success(await Sender.Send(new GetPopularSearchesQuery(country, limit), ct));
    }

    /// <summary>
    /// "Join Calling Bell": everything known about a Google Maps or OpenStreetMap place, read from the source on the server, cleaned and
    /// matched to Calling Bell's categories, cities and areas, for the business sign-up form; its photos (shown with their credit); and
    /// Calling Bell businesses that may already be this one (<c>existingBusinesses</c>; a strong match can't be registered again).
    /// 404 when the place can't be found. Nothing is stored.
    /// </summary>
    /// <param name="sourceId">"google:{place id}" or "osm:node/123", as on the search and Explore nearby results.</param>
    /// <param name="hint">What the person was browsing (a category slug or the search text); used only when the source's category doesn't match.</param>
    [HttpGet("join-calling-bell"), EnableRateLimiting("public-search")]
    public async Task<ActionResult<ApiResponse<CallingBell.Application.Features.Onboarding.JoinCallingBellBusinessDto>>> JoinCallingBell(
        [FromQuery] string? sourceId, [FromQuery] string? hint, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new CallingBell.Application.Features.Onboarding.GetJoinCallingBellBusinessQuery(sourceId ?? "", hint), ct));
    }

    /// <summary>
    /// Phone number and website for a place from the AI-recommended results (OpenStreetMap rarely has them), looked up on Google Maps by
    /// name near <c>lat</c>/<c>lon</c>. <c>found</c> is false when Google has no matching place there.
    /// </summary>
    [HttpGet("contact"), EnableRateLimiting("public-search")]
    public async Task<ActionResult<ApiResponse<PlaceContactDto>>> Contact([FromQuery] string? name, [FromQuery] double? lat, [FromQuery] double? lon,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new PlaceContactQuery(name, lat, lon), ct));
    }

    /// <summary>
    /// Everything known about one Google Maps or AI recommended search result, for the result detail window: Google's Place Details, or an
    /// OpenStreetMap place's tags combined with the same place on Google Maps. Not cached or stored (Google's terms).
    /// </summary>
    /// <param name="source">"google" or "osm" (AI recommended places).</param>
    /// <param name="id">Google place id, or an OpenStreetMap element such as "node/123".</param>
    /// <param name="name">OpenStreetMap only: the place's name, to find it on Google Maps.</param>
    [HttpGet("details"), EnableRateLimiting("public-search")]
    public async Task<ActionResult<ApiResponse<PlaceDetailsDto>>> Details([FromQuery] string? source, [FromQuery] string? id, [FromQuery] string? name,
        [FromQuery] double? lat, [FromQuery] double? lon, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Success(await Sender.Send(new GetPlaceDetailsQuery(source, id, name, lat, lon), ct));
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
