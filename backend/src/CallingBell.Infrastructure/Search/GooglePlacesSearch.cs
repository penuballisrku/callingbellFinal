using System.Net.Http.Json;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using System.Text.Json;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Search;

public sealed class GooglePlacesOptions
{
    public const string Section = "GooglePlaces";
    /// <summary>Google Maps Platform key with the Places API (New) enabled. Empty = Google Maps results are off.</summary>
    public string? ApiKey { get; set; }
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "https://places.googleapis.com/v1/places:searchText";
    public int MaxResults { get; set; } = 20;
    /// <summary>Location-bias radius in metres for the Text Search endpoint (<c>GET /api/places/search</c>).</summary>
    public double Radius { get; set; } = 1000.0;
    public string PhotoMediaUrl { get; set; } = "https://places.googleapis.com/v1/{0}/media";
    /// <summary>Place Details endpoint; {0} is the place id.</summary>
    public string DetailsUrl { get; set; } = "https://places.googleapis.com/v1/places/{0}";
    public string LanguageCode { get; set; } = "en";
    public string RegionCode { get; set; } = "IN";
}

/// <summary>
/// Google Maps businesses via the Google Places API (New) Text Search, biased to the selected area. Off until <see cref="GooglePlacesOptions.ApiKey"/>
/// is set. Google's terms don't allow storing its place content, so results are fetched for each request and not cached here.
/// </summary>
internal sealed class GooglePlacesSearch(IHttpClientFactory httpFactory, IOptions<GooglePlacesOptions> options, ILogger<GooglePlacesSearch> logger)
    : IGooglePlacesSearch
{
    public const string HttpClientName = "external-search-google";
    private const string FieldMask = "places.id,places.displayName,places.formattedAddress,places.location,places.rating,places.userRatingCount," +
                                     "places.nationalPhoneNumber,places.websiteUri,places.regularOpeningHours.weekdayDescriptions,places.googleMapsUri," +
                                     "places.primaryTypeDisplayName,places.businessStatus,places.photos,places.currentOpeningHours.openNow";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => options.Value is { Enabled: true, ApiKey.Length: > 0 };

    /// <summary>Google returns at most 20 places per request (and 60 per search, over three pages).</summary>
    private const int GooglePageSize = 20;

    /// <summary>
    /// One page of up to 20 places (<see cref="GooglePlacesOptions.MaxResults"/> when smaller). Pages used to be fetched one after another
    /// before anything was shown (about 2 s for three); now the first shows at once and the next is asked for with
    /// <paramref name="pageToken"/> only when the visitor wants more. Each page is a billed request.
    /// </summary>
    public async Task<GoogleSearchPage?> SearchAsync(string text, double lat, double lng, int radiusM, string? pageToken, CancellationToken ct)
    {
        if (!IsEnabled) return new GoogleSearchPage([], null);
        var o = options.Value;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, o.Url)
            {
                // The text, location and page size must be the same for every page of a search.
                Content = JsonContent.Create(new
                {
                    textQuery = text,
                    pageSize = Math.Clamp(o.MaxResults, 1, GooglePageSize),
                    pageToken = string.IsNullOrWhiteSpace(pageToken) ? null : pageToken,
                    languageCode = o.LanguageCode,
                    regionCode = o.RegionCode,
                    locationBias = new { circle = new { center = new { latitude = lat, longitude = lng }, radius = (double)radiusM } },
                }, options: JsonIgnoreNulls),
            };
            request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", FieldMask + ",nextPageToken");
            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Places search failed with {Status}: {Body}", (int)response.StatusCode,
                    (await response.Content.ReadAsStringAsync(ct)) is var body && body.Length > 300 ? body[..300] : body);
                return null;
            }
            var page = await response.Content.ReadFromJsonAsync<SearchTextResponse>(Json, ct);
            return new GoogleSearchPage(ToExternal(page?.Places ?? []), string.IsNullOrWhiteSpace(page?.NextPageToken) ? null : page.NextPageToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Google Places search failed");
            return null;
        }
    }

    private static List<ExternalPlace> ToExternal(IEnumerable<GPlace> places) => places
        .DistinctBy(p => p.Id)
        .Where(p => p.Location is not null && !string.IsNullOrWhiteSpace(p.DisplayName?.Text) && p.BusinessStatus is null or "OPERATIONAL")
        .Select(p => new ExternalPlace("google", p.Id, p.DisplayName!.Text!.Trim(), p.PrimaryTypeDisplayName?.Text, p.FormattedAddress,
            p.Location!.Latitude, p.Location.Longitude, p.NationalPhoneNumber, p.WebsiteUri,
            p.RegularOpeningHours?.WeekdayDescriptions is { Count: > 0 } days ? string.Join("; ", days) : null,
            p.Rating is { } r ? Math.Round((decimal)r, 1) : null, p.UserRatingCount, p.GoogleMapsUri, "tag",
            p.Photos?.Where(ph => !string.IsNullOrEmpty(ph.Name)).Select(ToPhoto).FirstOrDefault(), p.CurrentOpeningHours?.OpenNow))
        .ToList();

    private const string DetailsFieldMask = "id,displayName,primaryTypeDisplayName,types,editorialSummary,formattedAddress,addressComponents,location," +
                                            "nationalPhoneNumber,internationalPhoneNumber,websiteUri,regularOpeningHours.weekdayDescriptions," +
                                            "currentOpeningHours.openNow,rating,userRatingCount,googleMapsUri,businessStatus,priceLevel,photos";

    public async Task<GooglePlaceDetails?> GetDetailsAsync(string placeId, CancellationToken ct)
    {
        var o = EnsureConfigured();
        var url = string.Format(o.DetailsUrl, Uri.EscapeDataString(placeId)) +
                  $"?languageCode={Uri.EscapeDataString(o.LanguageCode)}&regionCode={Uri.EscapeDataString(o.RegionCode)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", DetailsFieldMask);
        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await ThrowIfFailedAsync(response, "Google Place Details", ct);

        var p = await response.Content.ReadFromJsonAsync<DetailsPlace>(Json, ct);
        if (p is null || string.IsNullOrWhiteSpace(p.DisplayName?.Text)) return null;
        string? Part(params string[] types) => p.AddressComponents?.FirstOrDefault(c => c.Types?.Any(types.Contains) == true)?.LongText;
        return new GooglePlaceDetails(p.Id ?? placeId, p.DisplayName.Text!.Trim(), p.PrimaryTypeDisplayName?.Text, p.Types ?? [], p.EditorialSummary?.Text,
            p.FormattedAddress, Part("locality", "postal_town", "administrative_area_level_3", "administrative_area_level_2"),
            Part("administrative_area_level_1"), Part("country"), Part("postal_code"), p.Location?.Latitude, p.Location?.Longitude,
            p.NationalPhoneNumber, p.InternationalPhoneNumber, p.WebsiteUri, p.RegularOpeningHours?.WeekdayDescriptions ?? [], p.CurrentOpeningHours?.OpenNow,
            p.Rating is { } r ? Math.Round((decimal)r, 1) : null, p.UserRatingCount, p.GoogleMapsUri, p.BusinessStatus, p.PriceLevel,
            (p.Photos ?? []).Where(ph => !string.IsNullOrEmpty(ph.Name)).Select(ToPhoto).ToList());
    }

    // Phone and Maps link are in the same (Enterprise) billing tier as rating, so asking for them costs nothing extra.
    private const string TextSearchFieldMask = "places.id,places.displayName,places.formattedAddress,places.rating,places.userRatingCount,places.location," +
                                               "places.photos,places.nationalPhoneNumber,places.internationalPhoneNumber,places.googleMapsUri," +
                                               "places.currentOpeningHours.openNow,nextPageToken";
    private static readonly JsonSerializerOptions JsonIgnoreNulls = new(JsonSerializerDefaults.Web)
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public async Task<GooglePlacesPage> TextSearchAsync(string textQuery, double lat, double lng, string? pageToken, CancellationToken ct)
    {
        var o = EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, o.Url)
        {
            Content = JsonContent.Create(new
            {
                textQuery,
                maxResultCount = Math.Clamp(o.MaxResults, 1, 20),
                pageToken = string.IsNullOrWhiteSpace(pageToken) ? null : pageToken,
                locationBias = new { circle = new { center = new { latitude = lat, longitude = lng }, radius = o.Radius } },
            }, options: JsonIgnoreNulls),
        };
        request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", TextSearchFieldMask);
        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "Google Places search", ct);

        var result = await response.Content.ReadFromJsonAsync<TextSearchResponse>(Json, ct);
        var places = (result?.Places ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName?.Text))
            .Select(p => new GooglePlace(p.DisplayName!.Text!.Trim(), p.FormattedAddress, p.Rating, p.UserRatingCount,
                p.Location?.Latitude, p.Location?.Longitude,
                (p.Photos ?? []).Where(ph => !string.IsNullOrEmpty(ph.Name)).Select(ToPhoto).ToList(),
                p.NationalPhoneNumber, p.InternationalPhoneNumber, p.GoogleMapsUri, p.CurrentOpeningHours?.OpenNow, p.Id))
            .ToList();
        return new GooglePlacesPage(places, string.IsNullOrWhiteSpace(result?.NextPageToken) ? null : result.NextPageToken);
    }

    public async Task<string> GetPhotoUriAsync(string photoName, int maxWidthPx, CancellationToken ct)
    {
        var o = EnsureConfigured();
        // skipHttpRedirect returns the image URL as JSON, so the API key never reaches the browser.
        var url = string.Format(o.PhotoMediaUrl, photoName) + $"?maxWidthPx={Math.Clamp(maxWidthPx, 1, 4800)}&skipHttpRedirect=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "Google Places photo", ct);
        var media = await response.Content.ReadFromJsonAsync<PhotoMediaResponse>(Json, ct);
        return media?.PhotoUri ?? throw new ExternalServiceException(502, "Google Places returned no photo.");
    }

    private const string ContactFieldMask = "places.id,places.displayName,places.location,places.nationalPhoneNumber,places.internationalPhoneNumber," +
                                            "places.websiteUri,places.googleMapsUri,places.businessStatus,places.rating,places.userRatingCount," +
                                            "places.currentOpeningHours.openNow,places.photos";
    /// <summary>How far a Google place may be from the given point and still count as the same place.</summary>
    private const double ContactMatchRadiusM = 300;

    public async Task<GooglePlaceContact?> FindContactAsync(string name, double lat, double lng, CancellationToken ct)
    {
        var o = EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, o.Url)
        {
            Content = JsonContent.Create(new
            {
                textQuery = name,
                pageSize = 5,
                languageCode = o.LanguageCode,
                regionCode = o.RegionCode,
                locationBias = new { circle = new { center = new { latitude = lat, longitude = lng }, radius = ContactMatchRadiusM } },
            }, options: Json),
        };
        request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", ContactFieldMask);
        using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "Google Places contact lookup", ct);

        // Only a nearby place with a matching name counts, so a neighbour's number is never shown for this place.
        var wanted = NameWords(name);
        var result = await response.Content.ReadFromJsonAsync<SearchTextResponse>(Json, ct);
        return (result?.Places ?? [])
            .Where(p => p.Location is not null && !string.IsNullOrWhiteSpace(p.DisplayName?.Text) && p.BusinessStatus is null or "OPERATIONAL")
            .Select(p => new { p, M = GeoMath.HaversineKm(lat, lng, p.Location!.Latitude, p.Location.Longitude) * 1000 })
            .Where(x => x.M <= ContactMatchRadiusM && NamesMatch(wanted, NameWords(x.p.DisplayName!.Text!)))
            .OrderBy(x => x.M)
            .Select(x => new GooglePlaceContact(x.p.DisplayName!.Text!.Trim(), x.p.NationalPhoneNumber, x.p.InternationalPhoneNumber, x.p.WebsiteUri,
                x.p.GoogleMapsUri, Math.Round(x.M), x.p.Rating is { } r ? Math.Round((decimal)r, 1) : null, x.p.UserRatingCount,
                x.p.CurrentOpeningHours?.OpenNow, x.p.Photos?.Where(ph => !string.IsNullOrEmpty(ph.Name)).Select(ToPhoto).FirstOrDefault(), x.p.Id))
            .FirstOrDefault();
    }

    /// <summary>Distinctive lower-case words of a business name (3+ letters, without generic words such as "shop" or "the").</summary>
    private static HashSet<string> NameWords(string name) =>
        System.Text.RegularExpressions.Regex.Split(name.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(w => w.Length >= 3 && !GenericWords.Contains(w))
            .ToHashSet();

    private static bool NamesMatch(HashSet<string> a, HashSet<string> b) => a.Count > 0 && b.Count > 0 && a.Overlaps(b);

    private static readonly HashSet<string> GenericWords =
    [
        "the", "and", "shop", "store", "stores", "centre", "center", "services", "service", "salon", "parlour", "parlor", "clinic", "hospital",
        "pvt", "ltd", "private", "limited", "india", "enterprises", "traders", "works", "agency", "agencies", "studio",
    ];

    private static GooglePlacePhoto ToPhoto(TPhoto ph) => new(ph.Name!, ph.WidthPx, ph.HeightPx,
        (ph.AuthorAttributions ?? []).Where(a => !string.IsNullOrWhiteSpace(a.DisplayName))
            .Select(a => new GooglePhotoAttribution(a.DisplayName!, a.Uri)).ToList());

    private GooglePlacesOptions EnsureConfigured() =>
        IsEnabled ? options.Value : throw new ExternalServiceException(503, "Google Places is not configured. Set GooglePlaces:ApiKey.");

    /// <summary>Passes Google's status code and error message through when the request was rejected.</summary>
    private async Task ThrowIfFailedAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("{What} failed with {Status}: {Body}", what, (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
        string? message = null;
        try { message = JsonSerializer.Deserialize<GoogleErrorResponse>(body, Json)?.Error?.Message; }
        catch (JsonException) { /* non-JSON error body */ }
        throw new ExternalServiceException((int)response.StatusCode,
            string.IsNullOrWhiteSpace(message) ? $"{what} failed ({(int)response.StatusCode} {response.ReasonPhrase})." : message);
    }

    private sealed record TextSearchResponse(List<TPlace>? Places, string? NextPageToken);
    private sealed record TPlace(LocalizedText? DisplayName, string? FormattedAddress, double? Rating, int? UserRatingCount, LatLng? Location,
        List<TPhoto>? Photos, string? NationalPhoneNumber, string? InternationalPhoneNumber, string? GoogleMapsUri, CurrentHours? CurrentOpeningHours,
        string? Id);
    private sealed record TPhoto(string? Name, int? WidthPx, int? HeightPx, List<TAuthor>? AuthorAttributions);
    private sealed record TAuthor(string? DisplayName, string? Uri);
    private sealed record PhotoMediaResponse(string? PhotoUri);
    private sealed record GoogleErrorResponse(GoogleError? Error);
    private sealed record GoogleError(int? Code, string? Message, string? Status);

    private sealed record SearchTextResponse(List<GPlace>? Places, string? NextPageToken);
    private sealed record GPlace(string Id, LocalizedText? DisplayName, string? FormattedAddress, LatLng? Location, double? Rating, int? UserRatingCount,
        string? NationalPhoneNumber, string? WebsiteUri, OpeningHours? RegularOpeningHours, string? InternationalPhoneNumber, string? GoogleMapsUri, LocalizedText? PrimaryTypeDisplayName,
        string? BusinessStatus, List<TPhoto>? Photos, CurrentHours? CurrentOpeningHours);
    private sealed record CurrentHours(bool? OpenNow);
    private sealed record DetailsPlace(string? Id, LocalizedText? DisplayName, LocalizedText? PrimaryTypeDisplayName, List<string>? Types,
        LocalizedText? EditorialSummary, string? FormattedAddress, List<AddressComponent>? AddressComponents, LatLng? Location,
        string? NationalPhoneNumber, string? InternationalPhoneNumber, string? WebsiteUri, OpeningHours? RegularOpeningHours,
        CurrentHours? CurrentOpeningHours, double? Rating, int? UserRatingCount, string? GoogleMapsUri, string? BusinessStatus, string? PriceLevel,
        List<TPhoto>? Photos);
    private sealed record AddressComponent(string? LongText, string? ShortText, List<string>? Types);
    private sealed record LocalizedText(string? Text);
    private sealed record LatLng(double Latitude, double Longitude);
    private sealed record OpeningHours(List<string>? WeekdayDescriptions);
}
