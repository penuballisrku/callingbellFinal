using System.Net.Http.Json;
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
                                     "places.primaryTypeDisplayName,places.businessStatus";
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
                    p.Rating is { } r ? Math.Round((decimal)r, 1) : null, p.UserRatingCount, p.GoogleMapsUri, "tag"))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Google Places search failed");
            return null;
        }
    }

    private sealed record SearchTextResponse(List<GPlace>? Places);
    private sealed record GPlace(string Id, LocalizedText? DisplayName, string? FormattedAddress, LatLng? Location, double? Rating, int? UserRatingCount,
        string? NationalPhoneNumber, string? WebsiteUri, OpeningHours? RegularOpeningHours, string? GoogleMapsUri, LocalizedText? PrimaryTypeDisplayName,
        string? BusinessStatus);
    private sealed record LocalizedText(string? Text);
    private sealed record LatLng(double Latitude, double Longitude);
    private sealed record OpeningHours(List<string>? WeekdayDescriptions);
}
