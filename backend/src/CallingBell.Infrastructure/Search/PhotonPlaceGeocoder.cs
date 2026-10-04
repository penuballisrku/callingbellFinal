using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Search;

/// <summary>
/// Places by name (towns, cities, localities) from the free Photon geocoder, limited to India. Used for searches that name a place outside
/// the listed cities ("lawyers in Nellore"). Answers are cached: a day for matches, an hour for misses.
/// </summary>
internal sealed class PhotonPlaceGeocoder(IHttpClientFactory httpFactory, IOptions<ExternalSearchOptions> options, IMemoryCache cache,
    ILogger<PhotonPlaceGeocoder> logger) : IPlaceGeocoder
{
    public const string HttpClientName = "place-geocoder";
    /// <summary>India's bounding box (min lon, min lat, max lon, max lat).</summary>
    private const string IndiaBbox = "68.1,6.5,97.4,35.7";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Settlement-type results first: a search names a town, not a street or shop of the same name.</summary>
    private static readonly string[] PlaceRank = ["city", "town", "municipality", "district", "county", "suburb", "village", "neighbourhood", "locality", "hamlet"];

    public async Task<GeocodedPlace?> GeocodeAsync(string place, CancellationToken ct)
    {
        var o = options.Value;
        var text = place.Trim();
        if (text.Length < 2 || string.IsNullOrWhiteSpace(o.PhotonUrl)) return null;
        var key = "geocode|" + text.ToLowerInvariant();
        if (cache.TryGetValue(key, out GeocodedPlace? cached)) return cached;

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            if (!client.DefaultRequestHeaders.UserAgent.Any()) client.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
            var result = await client.GetFromJsonAsync<PhotonResult>(
                $"{o.PhotonUrl}?q={Uri.EscapeDataString(text)}&limit=10&lang=en&layer=city&layer=district&layer=locality&bbox={IndiaBbox}", Json, ct);
            var best = (result?.Features ?? [])
                .Where(f => f.Properties is { Name.Length: > 0, CountryCode: null or "IN" or "in" } && f.Geometry?.Coordinates is [_, _, ..])
                .OrderBy(f => f.Properties!.Name!.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => Array.IndexOf(PlaceRank, f.Properties!.OsmValue) is var i and >= 0 ? i : PlaceRank.Length)
                .FirstOrDefault();
            var found = best is null ? null
                : new GeocodedPlace(best.Properties!.Name!, best.Properties.State, best.Geometry!.Coordinates![1], best.Geometry.Coordinates[0]);
            cache.Set(key, found, found is null ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1));
            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Geocoding {Place} failed", text);
            return null;
        }
    }

    private sealed record PhotonResult(List<Feature>? Features);
    private sealed record Feature(Geometry? Geometry, Properties? Properties);
    private sealed record Geometry(double[]? Coordinates);
    private sealed record Properties(string? Name, string? State, [property: JsonPropertyName("countrycode")] string? CountryCode,
        [property: JsonPropertyName("osm_value")] string? OsmValue);
}
