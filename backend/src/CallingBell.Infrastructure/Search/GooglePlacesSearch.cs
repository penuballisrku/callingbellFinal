using System.Net.Http.Json;
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
                                     "places.primaryTypeDisplayName,places.businessStatus,places.photos";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => options.Value is { Enabled: true, ApiKey.Length: > 0 };

    public async Task<IReadOnlyList<ExternalPlace>?> SearchAsync(string text, double lat, double lng, int radiusM, CancellationToken ct)
    {
        if (!IsEnabled) return [];
        var o = options.Value;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, o.Url)
            {
                Content = JsonContent.Create(new
                {
                    textQuery = text,
                    pageSize = Math.Clamp(o.MaxResults, 1, 20),
                    languageCode = o.LanguageCode,
                    regionCode = o.RegionCode,
                    locationBias = new { circle = new { center = new { latitude = lat, longitude = lng }, radius = (double)radiusM } },
                }, options: Json),
            };
            request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", FieldMask);
            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Places search failed with {Status}: {Body}", (int)response.StatusCode,
                    (await response.Content.ReadAsStringAsync(ct)) is var body && body.Length > 300 ? body[..300] : body);
                return null;
            }
            var result = await response.Content.ReadFromJsonAsync<SearchTextResponse>(Json, ct);
            return (result?.Places ?? [])
                .Where(p => p.Location is not null && !string.IsNullOrWhiteSpace(p.DisplayName?.Text) && p.BusinessStatus is null or "OPERATIONAL")
                .Select(p => new ExternalPlace("google", p.Id, p.DisplayName!.Text!.Trim(), p.PrimaryTypeDisplayName?.Text, p.FormattedAddress,
                    p.Location!.Latitude, p.Location.Longitude, p.NationalPhoneNumber, p.WebsiteUri,
                    p.RegularOpeningHours?.WeekdayDescriptions is { Count: > 0 } days ? string.Join("; ", days) : null,
                    p.Rating is { } r ? Math.Round((decimal)r, 1) : null, p.UserRatingCount, p.GoogleMapsUri, "tag",
                    p.Photos?.Where(ph => !string.IsNullOrEmpty(ph.Name)).Select(ToPhoto).FirstOrDefault()))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Google Places search failed");
            return null;
        }
    }

    private const string TextSearchFieldMask = "places.displayName,places.formattedAddress,places.rating,places.userRatingCount,places.location," +
                                               "places.photos,nextPageToken";
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
                (p.Photos ?? []).Where(ph => !string.IsNullOrEmpty(ph.Name)).Select(ToPhoto).ToList()))
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
        List<TPhoto>? Photos);
    private sealed record TPhoto(string? Name, int? WidthPx, int? HeightPx, List<TAuthor>? AuthorAttributions);
    private sealed record TAuthor(string? DisplayName, string? Uri);
    private sealed record PhotoMediaResponse(string? PhotoUri);
    private sealed record GoogleErrorResponse(GoogleError? Error);
    private sealed record GoogleError(int? Code, string? Message, string? Status);

    private sealed record SearchTextResponse(List<GPlace>? Places);
    private sealed record GPlace(string Id, LocalizedText? DisplayName, string? FormattedAddress, LatLng? Location, double? Rating, int? UserRatingCount,
        string? NationalPhoneNumber, string? WebsiteUri, OpeningHours? RegularOpeningHours, string? GoogleMapsUri, LocalizedText? PrimaryTypeDisplayName,
        string? BusinessStatus, List<TPhoto>? Photos);
    private sealed record LocalizedText(string? Text);
    private sealed record LatLng(double Latitude, double Longitude);
    private sealed record OpeningHours(List<string>? WeekdayDescriptions);
}
