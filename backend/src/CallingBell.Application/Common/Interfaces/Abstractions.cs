using CallingBell.Application.Features.Auth;
using CallingBell.Application.Features.Notifications;

namespace CallingBell.Application.Common.Interfaces;

/// <summary>Generic repository over an aggregate/entity set.</summary>
public interface IRepository<T> where T : class
{
    /// <summary>Tracked query - use when the result will be modified.</summary>
    IQueryable<T> Query();

    /// <summary>Untracked query - use for read models / projections.</summary>
    IQueryable<T> QueryNoTracking();

    void Add(T entity);
    void Remove(T entity);
}

/// <summary>Unit of work: one transactional boundary for all repositories in a request.</summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction (all-or-nothing). The whole unit is retried on transient
    /// SQL failures, so the delegate must be safe to re-run.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
    string? IpAddress { get; }
}

public interface IIdentityService
{
    Task<AuthResultDto> LoginAsync(string email, string password, CancellationToken ct);
    /// <summary>Creates an account whose mobile number has already been verified by OTP (no password).</summary>
    Task<AuthResultDto> RegisterAsync(RegisterRequest request, CancellationToken ct);
    /// <summary>Signs in the account registered with this (normalised) mobile number, after its OTP was verified.</summary>
    Task<AuthResultDto> SignInWithPhoneAsync(string phoneNumber, CancellationToken ct);
    Task<AuthResultDto> RefreshAsync(string refreshToken, CancellationToken ct);
    Task RevokeAsync(string refreshToken, CancellationToken ct);
    Task<CurrentUserDto> GetCurrentUserAsync(string userId, CancellationToken ct);
    Task<bool> IsEmailAvailableAsync(string email, CancellationToken ct);
    /// <summary>Whether an active account uses this (normalised) mobile number, optionally ignoring one user.</summary>
    Task<bool> IsPhoneRegisteredAsync(string phoneNumber, CancellationToken ct, string? exceptUserId = null);
    Task<AccountSettingsDto> GetAccountSettingsAsync(string userId, CancellationToken ct);
    Task<CurrentUserDto> UpdateAccountAsync(string userId, string displayName, string phoneNumber, CancellationToken ct);
    /// <summary>Changes the password (or sets one for OTP-only accounts, when <paramref name="currentPassword"/> is null).</summary>
    Task ChangePasswordAsync(string userId, string? currentPassword, string newPassword, CancellationToken ct);
    /// <summary>Revokes every refresh token of the user except <paramref name="keepRefreshToken"/>; returns how many were revoked.</summary>
    Task<int> RevokeOtherSessionsAsync(string userId, string? keepRefreshToken, CancellationToken ct);
}

/// <summary>A sub-category the semantic index matched to a search, with its cosine similarity (0..1, higher is closer).</summary>
public sealed record SemanticMatch(string SubCategorySlug, double Score);

/// <param name="Top">The closest sub-categories, best first.</param>
/// <param name="Confident">The best match clearly stands out from the rest of the catalogue: safe to apply directly.</param>
/// <param name="Plausible">The request probably means one of <paramref name="Top"/>, but which one is unclear (or it is unrelated text).</param>
/// <param name="Z">How far the best match stands out (standard deviations above the catalogue's mean similarity).</param>
public sealed record SemanticResult(IReadOnlyList<SemanticMatch> Top, bool Confident, bool Plausible, double Z);

/// <summary>
/// Fast meaning-based matching of free text to the catalogue ("water dripping from my ceiling" gives Plumbers) using a local Ollama
/// embedding model. The catalogue's vectors are built in the background; a query takes a few hundred milliseconds on a CPU.
/// Null while the index is being built or Ollama is unavailable, so callers fall back to their other methods.
/// </summary>
public interface ISemanticCatalog
{
    bool IsReady { get; }
    /// <param name="timeout">How long to wait for the model (default from settings); on timeout the result is null and the search goes on without it.</param>
    Task<SemanticResult?> MatchAsync(string text, int top, CancellationToken ct, TimeSpan? timeout = null);
}

/// <summary>One-time codes sent to a mobile number for sign-in and sign-up. Phone numbers are passed normalised.</summary>
public interface IPhoneOtpService
{
    /// <summary>Generates and texts a new code (invalidating earlier ones for the same purpose), enforcing resend limits.</summary>
    Task<OtpChallengeDto> SendAsync(string phoneNumber, OtpPurpose purpose, CancellationToken ct);
    /// <summary>Checks and consumes the latest code; throws a validation error on a wrong, expired or exhausted code.</summary>
    Task VerifyAsync(string phoneNumber, OtpPurpose purpose, string code, CancellationToken ct);
    /// <summary>Verifies a sign-up code and returns a short-lived token proving the number was verified.</summary>
    Task<PhoneVerificationDto> VerifyForSignUpAsync(string phoneNumber, string code, CancellationToken ct);
    /// <summary>Marks a sign-up verification token as used; throws if it is unknown, expired, used or for another number.</summary>
    Task ConsumeVerificationAsync(string phoneNumber, string verificationToken, CancellationToken ct);
}

/// <summary>Sends text messages (OTP codes). Implemented in Infrastructure.</summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken ct);
}

/// <summary>Pushes real-time events (SignalR). Implemented in the API layer.</summary>
public interface IRealtimeNotifier
{
    Task AvailabilityChangedAsync(Guid businessId, string status, DateTimeOffset lastSeenOn, CancellationToken ct = default);
    Task NotifyUserAsync(string userId, NotificationDto notification, CancellationToken ct = default);
}

/// <summary>An order created at the payment gateway (amounts in the smallest currency unit, i.e. paise).</summary>
public sealed record GatewayOrder(string Id, long Amount, string Currency, string Status);

/// <summary>A payment as reported by the gateway.</summary>
public sealed record GatewayPayment(string Id, string? OrderId, long Amount, string Currency, string Status, string? Method, string? ErrorDescription);

/// <summary>Online payment gateway (implemented with Razorpay in Infrastructure).</summary>
public interface IPaymentGateway
{
    string Name { get; }
    /// <summary>True when API keys are configured; checkout is disabled otherwise.</summary>
    bool IsConfigured { get; }
    /// <summary>Publishable key id used by the browser checkout.</summary>
    string? PublicKey { get; }
    Task<GatewayOrder> CreateOrderAsync(string receipt, long amountInPaise, string currency, IReadOnlyDictionary<string, string> notes, CancellationToken ct);
    Task<GatewayPayment> GetPaymentAsync(string paymentId, CancellationToken ct);
    /// <summary>Verifies the signature the checkout returns after a successful payment.</summary>
    bool VerifyPaymentSignature(string gatewayOrderId, string paymentId, string signature);
    /// <summary>Verifies a webhook body against its signature header. False when no webhook secret is configured.</summary>
    bool VerifyWebhookSignature(string body, string signature);
}

/// <summary>IP-to-country lookup (MaxMind GeoLite2 database on the server; visitor IPs never leave the server).</summary>
public interface IGeoLocationService
{
    /// <summary>ISO 3166-1 alpha-2 country used when the country can't be determined.</summary>
    string DefaultCountryCode { get; }
    /// <summary>Country code for a public IP address, or null when unknown (not in the database, or no database installed).</summary>
    string? CountryCodeFor(System.Net.IPAddress address);
    /// <summary>City-level location for a public IP address, or null when unknown (or no city database installed).</summary>
    IpLocation? LocationFor(System.Net.IPAddress address);
    /// <summary>Credit the database licence requires on the site (e.g. DB-IP Lite, CC BY 4.0); null when none is needed.</summary>
    GeoAttribution? Attribution { get; }
}

public sealed record GeoAttribution(string Text, string Url);

/// <summary>Approximate location of an IP address (city-level at best; typically accurate to a district, not a street).</summary>
/// <param name="District">Locality within the city, when the provider knows it (ip-api.com "district").</param>
/// <param name="Postcode">Postal code (PIN code in India), when known.</param>
public sealed record IpLocation(string? CountryCode, string? Region, string? City, double? Latitude, double? Longitude,
    string? District = null, string? Postcode = null);

/// <summary>
/// Visitor location lookup (ip-api.com, falling back to the local GeoIP city database). Sends the visitor's IP to ip-api.com.
/// </summary>
public interface IIpLocationService
{
    /// <summary>Location for a public IP address, or null when it can't be located.</summary>
    Task<IpLocation?> LocateAsync(System.Net.IPAddress address, CancellationToken cancellationToken = default);
}

/// <summary>The server's cache of public, non-personalised responses (implemented with ASP.NET Core output caching in the API).</summary>
public interface IPublicCache
{
    /// <summary>Drops every cached public response, so the next request reads fresh data.</summary>
    Task InvalidateAsync(CancellationToken ct);
}

/// <summary>A service shown to the AI ranker. <see cref="Key"/> identifies it across requests (sub-category slug + service name).</summary>
public sealed record AiServiceCandidate(string Key, string Name, string SubCategory, string Category, int RecentBookingsNearby,
    decimal Rating, double? NearestKm, decimal StartingPrice);

/// <summary>A sub-category the AI may suggest as related to the popular services. <see cref="Key"/> is the sub-category slug.</summary>
public sealed record AiCategoryCandidate(string Key, string Name, string Category, int BusinessesNearby, int RecentBookingsNearby, double? NearestKm);

/// <summary>
/// Context for ranking services near a place: where, when, the database-ranked service shortlist to choose from, and the
/// sub-categories available nearby from which to suggest <paramref name="RelatedCount"/> related categories.
/// </summary>
/// <param name="CatalogOnly">True when nothing is listed in the city yet: candidates come from the platform-wide catalogue and their
/// figures are national, so the model picks from what the place is likely to need rather than from local demand.</param>
public sealed record AiServiceRankingRequest(string Place, string City, string State, DateTimeOffset LocalTime, int PickCount,
    IReadOnlyList<AiServiceCandidate> Candidates, int RelatedCount, IReadOnlyList<AiCategoryCandidate> RelatedCandidates,
    bool CatalogOnly = false);

public sealed record AiServicePick(string Key, string Reason);

/// <param name="Related">Related sub-categories (keys are sub-category slugs) with the reason each complements the picks.</param>
public sealed record AiServiceRanking(IReadOnlyList<AiServicePick> Picks, IReadOnlyList<AiServicePick> Related, string Model, DateTimeOffset GeneratedOn);

/// <summary>
/// AI re-ranking of nearby services (implemented with a local Ollama model in Infrastructure). Generation is slow, so it never runs
/// inside a request: callers read the cached ranking and queue a background run when there is none.
/// </summary>
public interface IServiceRecommender
{
    /// <summary>False when AI ranking is disabled in configuration.</summary>
    bool IsEnabled { get; }
    string? Model { get; }
    /// <summary>The cached ranking for <paramref name="key"/>, or null when none is ready.</summary>
    AiServiceRanking? GetCached(string key);
    /// <summary>True while a ranking for <paramref name="key"/> is queued or running.</summary>
    bool IsPending(string key);
    /// <summary>Queues a background ranking; ignored when disabled, already cached, pending, or recently failed.</summary>
    void Enqueue(string key, AiServiceRankingRequest request);
}

/// <summary>A catalogue sub-category offered to the AI as a possible extra pick for a city. <see cref="Key"/> is the sub-category slug.</summary>
public sealed record AiCityCategoryCandidate(string Key, string Name, string Category, int BusinessesInCity, int BookingsInCity);

/// <summary>
/// Context for suggesting extra categories suited to a city: the categories the database already ranks highest there (shown to
/// the visitor as-is) and the rest of the catalogue to choose <paramref name="PickCount"/> additions from.
/// </summary>
public sealed record AiCityCategoriesRequest(string City, string State, string Place, DateTimeOffset LocalTime,
    IReadOnlyList<string> AlreadyShown, IReadOnlyList<AiCityCategoryCandidate> Candidates, int PickCount);

/// <param name="Picks">Suggested sub-categories (keys are sub-category slugs) with why each suits the city.</param>
public sealed record AiCityCategories(IReadOnlyList<AiServicePick> Picks, string Model, DateTimeOffset GeneratedOn);

/// <summary>
/// AI suggestions of additional categories relevant to a city (local Ollama model in Infrastructure). Same contract as
/// <see cref="IServiceRecommender"/>: read the cache, queue a background run when there is none; never runs inside a request.
/// </summary>
public interface ICityCategoryRecommender
{
    bool IsEnabled { get; }
    string? Model { get; }
    AiCityCategories? GetCached(string key);
    bool IsPending(string key);
    void Enqueue(string key, AiCityCategoriesRequest request);
}

/// <summary>
/// Background agent that discovers and maintains a city's areas and sub-localities (OpenStreetMap places, PIN codes from OSM /
/// India Post, duplicate and spelling clean-up by the local AI model). Requests are queued; work never runs inside an HTTP request.
/// </summary>
public interface IAreaDiscoveryService
{
    /// <summary>Queues discovery for the city unless it is already queued or running.</summary>
    void Request(Guid cityId);
    bool IsRunning(Guid cityId);
}

/// <summary>Context for summarising real reviews of a place: where, and review snippets (rating and text only, no names).</summary>
public sealed record AiReviewSummaryRequest(string Place, string City, IReadOnlyList<(byte Rating, string Text)> Reviews);

public sealed record AiReviewSummary(string Text, string Model, DateTimeOffset GeneratedOn);

/// <summary>
/// Short AI summary of what customers praise in real reviews of a place (local Ollama model in Infrastructure). Same contract as the
/// other recommenders: read the cache, queue a background run when there is none; never runs inside a request.
/// </summary>
public interface IReviewSummarizer
{
    bool IsEnabled { get; }
    AiReviewSummary? GetCached(string key);
    bool IsPending(string key);
    void Enqueue(string key, AiReviewSummaryRequest request);
}

// ===================== Search beyond the platform =====================

/// <summary>A real place found outside the platform (OpenStreetMap or Google Maps), as the source describes it.</summary>
/// <param name="Source">"osm" or "google".</param>
/// <param name="Id">Source id, e.g. "node/123" (OpenStreetMap) or a Google place id.</param>
/// <param name="Kind">What the source says the place is, e.g. "Interior designer".</param>
/// <param name="SourceUrl">The place's page at the source (also its attribution link).</param>
/// <param name="MatchedBy">"tag" (matched by a category tag) or "name" (its name contains a search word): name matches are less certain.</param>
/// <param name="Photo">Google Maps only: the place's first photo.</param>
public sealed record ExternalPlace(string Source, string Id, string Name, string? Kind, string? Address, double Latitude, double Longitude,
    string? Phone, string? Website, string? OpeningHours, decimal? Rating, int? RatingCount, string? SourceUrl, string MatchedBy,
    GooglePlacePhoto? Photo = null, bool? OpenNow = null);

/// <summary>Finds named places near a point in OpenStreetMap by "key=value" tag and "name~words" selectors.</summary>
public interface IOsmPlaceSearch
{
    /// <returns>Places found, or null when OpenStreetMap could not be reached.</returns>
    Task<IReadOnlyList<ExternalPlace>?> SearchAsync(IReadOnlyCollection<string> selectors, double lat, double lng, int radiusM, CancellationToken ct);

    /// <summary>One element with all its tags (OpenStreetMap API), cached in memory. Null when it doesn't exist or OSM can't be reached.</summary>
    /// <param name="id">"node/123", "way/456" or "relation/789".</param>
    Task<OsmElement?> GetElementAsync(string id, CancellationToken ct);

    /// <summary>
    /// True while the full OpenStreetMap search for these arguments is still running in the background (after <see cref="SearchAsync"/>
    /// answered with the faster, partial results). Searching again once it finishes returns the full results.
    /// </summary>
    bool IsSearching(IReadOnlyCollection<string> selectors, double lat, double lng, int radiusM);
}

/// <summary>
/// The city catalogue agent: imports every city and town of a country (from the free GeoNames data) into the location tables, so every
/// city dropdown can offer the visitor's whole country. Areas of a city then come from <see cref="IAreaDiscoveryService"/> on demand.
/// </summary>
public interface ICountryCatalogService
{
    /// <summary>Queues an import of the country's cities (ISO 3166-1 alpha-2 code); ignored while one is queued or running.</summary>
    void Request(string countryCode);
    bool IsRunning(string countryCode);
}

/// <summary>A town, city or locality found by name, e.g. a place typed in a search that isn't one of the listed cities.</summary>
public sealed record GeocodedPlace(string Name, string? State, double Latitude, double Longitude);

/// <summary>Finds a place (city, town, locality) by name in the default country, using free OpenStreetMap geocoding.</summary>
public interface IPlaceGeocoder
{
    /// <returns>The best match, or null when nothing matched or the geocoder could not be reached.</returns>
    Task<GeocodedPlace?> GeocodeAsync(string place, CancellationToken ct);
}

/// <param name="NextPageToken">Google's token for the next page of the same search; null on the last page.</param>
public sealed record GoogleSearchPage(IReadOnlyList<ExternalPlace> Places, string? NextPageToken);

/// <summary>Google Maps business results (Google Places API Text Search). Disabled until an API key is configured.</summary>
/// <summary>Counts searches made on Explore nearby against the "Popular searches" entries (dbo.PopularSearches) they match.</summary>
public interface IPopularSearchCounter
{
    /// <summary>Adds one search to every active entry whose SearchText is <paramref name="searchText"/> (case-insensitive); a single atomic update.</summary>
    Task RecordAsync(string searchText, CancellationToken ct);
}

public interface IGooglePlacesSearch
{
    bool IsEnabled { get; }

    /// <summary>
    /// One page (up to 20) of places for a search, so the first results show at once. Pass the returned <see cref="GoogleSearchPage.NextPageToken"/>
    /// with the same text and location for the next page (Google answers at most 60 per search).
    /// </summary>
    /// <returns>The page, or null when Google could not be reached or rejected the request.</returns>
    Task<GoogleSearchPage?> SearchAsync(string text, double lat, double lng, int radiusM, string? pageToken, CancellationToken ct);

    /// <summary>
    /// One page of Google Places Text Search results near a point, using the configured result count and radius.
    /// Throws <see cref="Exceptions.ExternalServiceException"/> with Google's status and message when Google rejects the request.
    /// </summary>
    Task<GooglePlacesPage> TextSearchAsync(string textQuery, double lat, double lng, string? pageToken, CancellationToken ct);

    /// <summary>Short-lived image URL for a place photo resource name ("places/{id}/photos/{ref}").</summary>
    Task<string> GetPhotoUriAsync(string photoName, int maxWidthPx, CancellationToken ct);

    /// <summary>
    /// Contact details Google Maps lists for a named place at a point (e.g. an OpenStreetMap place, which rarely has a phone): the closest
    /// Google place within a short distance whose name matches. Null when none matches. Throws like <see cref="TextSearchAsync"/>.
    /// </summary>
    Task<GooglePlaceContact?> FindContactAsync(string name, double lat, double lng, CancellationToken ct);

    /// <summary>
    /// Everything Google Maps lists for one place (Place Details), for the result detail window. Null when Google has no such place.
    /// Throws like <see cref="TextSearchAsync"/>. Fetched per request and not stored (Google's terms).
    /// </summary>
    Task<GooglePlaceDetails?> GetDetailsAsync(string placeId, CancellationToken ct);
}

/// <param name="Types">Google's place types, e.g. "electrician", "point_of_interest".</param>
/// <param name="Summary">Google's editorial summary, when it has one.</param>
/// <param name="OpeningHours">One line per day, e.g. "Monday: 9:00 AM – 7:00 PM".</param>
/// <param name="PriceLevel">E.g. "PRICE_LEVEL_MODERATE".</param>
public sealed record GooglePlaceDetails(string Id, string Name, string? PrimaryType, IReadOnlyList<string> Types, string? Summary, string? FormattedAddress,
    string? City, string? State, string? Country, string? PostalCode, double? Latitude, double? Longitude, string? Phone, string? InternationalPhone,
    string? Website, IReadOnlyList<string> OpeningHours, bool? OpenNow, decimal? Rating, int? RatingCount, string? MapsUrl, string? BusinessStatus,
    string? PriceLevel, IReadOnlyList<GooglePlacePhoto> Photos);

/// <summary>One OpenStreetMap element with all its tags (name, addr:*, phone, opening_hours, ...).</summary>
/// <param name="Id">"node/123", "way/456" or "relation/789".</param>
public sealed record OsmElement(string Id, double? Latitude, double? Longitude, IReadOnlyDictionary<string, string> Tags);

/// <param name="Phone">National format, e.g. "098480 12345".</param>
/// <param name="MapsUrl">The place on Google Maps.</param>
/// <param name="OpenNow">From Google's current opening hours; null when Google doesn't know them.</param>
/// <param name="Id">Google place id, for <see cref="IGooglePlacesSearch.GetDetailsAsync"/>.</param>
public sealed record GooglePlaceContact(string Name, string? Phone, string? InternationalPhone, string? Website, string? MapsUrl, double DistanceM,
    decimal? Rating = null, int? RatingCount = null, bool? OpenNow = null, GooglePlacePhoto? Photo = null, string? Id = null);

public sealed record GooglePlacesPage(IReadOnlyList<GooglePlace> Places, string? NextPageToken);

public sealed record GooglePlace(string Name, string? FormattedAddress, double? Rating, int? UserRatingCount, double? Latitude, double? Longitude,
    IReadOnlyList<GooglePlacePhoto> Photos, string? Phone = null, string? InternationalPhone = null, string? MapsUrl = null, bool? OpenNow = null, string? Id = null);

/// <param name="Name">Photo resource name, passed to <see cref="IGooglePlacesSearch.GetPhotoUriAsync"/>.</param>
/// <param name="Attributions">Photo authors, which Google requires to be shown with the photo.</param>
public sealed record GooglePlacePhoto(string Name, int? WidthPx, int? HeightPx, IReadOnlyList<GooglePhotoAttribution> Attributions);

public sealed record GooglePhotoAttribution(string DisplayName, string? Uri);

/// <summary>Which catalogue sub-categories (slugs) a free-text search is looking for, picked by the AI from the catalogue.</summary>
public sealed record AiSearchIntent(IReadOnlyList<string> SubCategorySlugs, string Model, DateTimeOffset GeneratedOn);

/// <summary>The AI's choice of the places that fit a search, best first (ids from the candidates given).</summary>
/// <param name="Reasons">Why each picked place fits, in plain words, by place id (from the details it was given only).</param>
public sealed record AiPlaceRanking(IReadOnlyList<string> Relevant, string Model, DateTimeOffset GeneratedOn,
    IReadOnlyDictionary<string, string>? Reasons = null);

/// <param name="Details">What else is known, in plain words, e.g. "1.2 km away, phone listed, hours Mo-Sa 09:00-20:00".</param>
public sealed record AiPlaceCandidate(string Id, string Name, string? Kind, string? Details = null);

/// <summary>The AI's plain-language answer to a visitor's question, written only from the platform facts it was given.</summary>
public sealed record AiAnswer(string Text, string Model, DateTimeOffset GeneratedOn);

/// <summary>
/// What the AI read from a message to the search assistant, in the platform's search terms. Flags are only set when the visitor asked
/// for them. <paramref name="ServiceSlug"/> is one of the candidate sub-categories it was given (or null).
/// </summary>
/// <param name="Place">A place named in the message (locality or city), as written.</param>
/// <param name="Urgent">Needed right now or today.</param>
/// <param name="Sort">"rating", "price", "reviews" or "distance".</param>
public sealed record AiRequestReading(string? ServiceSlug, string? Place, bool Verified, bool HomeVisit, bool Urgent, bool OpenNow, bool Video,
    bool Booking, decimal? MinRating, string? Sort, string Model, DateTimeOffset GeneratedOn);

/// <summary>
/// Local AI help for searches that go beyond the platform: understanding a free-text search, and choosing which nearby real places fit it.
/// Same pattern as the other recommenders: read the cache, queue a background run when there is none; never runs inside a request.
/// </summary>
public interface ISearchAssistant
{
    bool IsEnabled { get; }
    AiSearchIntent? GetIntent(string key);
    bool IsIntentPending(string key);
    void RequestIntent(string key, string query, IReadOnlyList<(string Slug, string Name)> subCategories);
    AiPlaceRanking? GetRanking(string key);
    bool IsRankingPending(string key);
    void RequestRanking(string key, string query, string place, IReadOnlyList<AiPlaceCandidate> candidates);
    AiAnswer? GetAnswer(string key);
    bool IsAnswerPending(string key);
    /// <summary>Queues a plain-language answer to <paramref name="question"/> using only <paramref name="facts"/> (live database figures).</summary>
    void RequestAnswer(string key, string question, string facts);
    /// <summary>
    /// Queues a short conversational reply to a search request, describing the results in <paramref name="facts"/>; read it with
    /// <see cref="GetAnswer"/>.
    /// </summary>
    void RequestReply(string key, string message, string facts);
    AiRequestReading? GetReading(string key);
    bool IsReadingPending(string key);
    /// <summary>
    /// Queues reading <paramref name="message"/> into search filters. <paramref name="current"/> describes the search it may refine;
    /// <paramref name="services"/> are the sub-categories it may name (empty when the service is already known).
    /// </summary>
    void RequestReading(string key, string message, string? current, IReadOnlyList<(string Slug, string Name)> services);
}
