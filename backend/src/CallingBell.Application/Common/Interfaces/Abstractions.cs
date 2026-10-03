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
    Task<AuthResultDto> RegisterAsync(RegisterRequest request, CancellationToken ct);
    /// <summary>Signs in (or signs up) with a Google ID token issued to this application's client id.</summary>
    Task<AuthResultDto> ExternalGoogleAsync(string idToken, string accountType, CancellationToken ct);
    Task<AuthResultDto> RefreshAsync(string refreshToken, CancellationToken ct);
    Task RevokeAsync(string refreshToken, CancellationToken ct);
    Task<CurrentUserDto> GetCurrentUserAsync(string userId, CancellationToken ct);
    Task<bool> IsEmailAvailableAsync(string email, CancellationToken ct);
    Task<AccountSettingsDto> GetAccountSettingsAsync(string userId, CancellationToken ct);
    Task<CurrentUserDto> UpdateAccountAsync(string userId, string displayName, string phoneNumber, CancellationToken ct);
    /// <summary>Changes the password (or sets one for Google-only accounts, when <paramref name="currentPassword"/> is null).</summary>
    Task ChangePasswordAsync(string userId, string? currentPassword, string newPassword, CancellationToken ct);
    /// <summary>Revokes every refresh token of the user except <paramref name="keepRefreshToken"/>; returns how many were revoked.</summary>
    Task<int> RevokeOtherSessionsAsync(string userId, string? keepRefreshToken, CancellationToken ct);
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

/// <summary>A service shown to the AI ranker. <see cref="Key"/> identifies it across requests (sub-category slug + service name).</summary>
public sealed record AiServiceCandidate(string Key, string Name, string SubCategory, string Category, int RecentBookingsNearby,
    decimal Rating, double? NearestKm, decimal StartingPrice);

/// <summary>A sub-category the AI may suggest as related to the popular services. <see cref="Key"/> is the sub-category slug.</summary>
public sealed record AiCategoryCandidate(string Key, string Name, string Category, int BusinessesNearby, int RecentBookingsNearby, double? NearestKm);

/// <summary>
/// Context for ranking services near a place: where, when, the database-ranked service shortlist to choose from, and the
/// sub-categories available nearby from which to suggest <paramref name="RelatedCount"/> related categories.
/// </summary>
public sealed record AiServiceRankingRequest(string Place, string City, string State, DateTimeOffset LocalTime, int PickCount,
    IReadOnlyList<AiServiceCandidate> Candidates, int RelatedCount, IReadOnlyList<AiCategoryCandidate> RelatedCandidates);

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
public sealed record ExternalPlace(string Source, string Id, string Name, string? Kind, string? Address, double Latitude, double Longitude,
    string? Phone, string? Website, string? OpeningHours, decimal? Rating, int? RatingCount, string? SourceUrl, string MatchedBy);

/// <summary>Finds named places near a point in OpenStreetMap by "key=value" tag and "name~words" selectors.</summary>
public interface IOsmPlaceSearch
{
    /// <returns>Places found, or null when OpenStreetMap could not be reached.</returns>
    Task<IReadOnlyList<ExternalPlace>?> SearchAsync(IReadOnlyCollection<string> selectors, double lat, double lng, int radiusM, CancellationToken ct);
}

/// <summary>Google Maps business results (Google Places API Text Search). Disabled until an API key is configured.</summary>
public interface IGooglePlacesSearch
{
    bool IsEnabled { get; }

    /// <returns>Places found, or null when Google could not be reached or rejected the request.</returns>
    Task<IReadOnlyList<ExternalPlace>?> SearchAsync(string text, double lat, double lng, int radiusM, CancellationToken ct);
}

/// <summary>Which catalogue sub-categories (slugs) a free-text search is looking for, picked by the AI from the catalogue.</summary>
public sealed record AiSearchIntent(IReadOnlyList<string> SubCategorySlugs, string Model, DateTimeOffset GeneratedOn);

/// <summary>The AI's choice of the places that fit a search, best first (ids from the candidates given).</summary>
public sealed record AiPlaceRanking(IReadOnlyList<string> Relevant, string Model, DateTimeOffset GeneratedOn);

public sealed record AiPlaceCandidate(string Id, string Name, string? Kind);

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
}
