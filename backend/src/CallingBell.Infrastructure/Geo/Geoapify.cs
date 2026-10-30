using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Infrastructure.Search;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

public sealed class GeoapifyOptions
{
    public const string Section = "Geoapify";
    public bool Enabled { get; set; } = true;
    /// <summary>A secret: keep it in user-secrets or an environment variable (Geoapify__ApiKey), never in appsettings.json.</summary>
    public string? ApiKey { get; set; }
    public string ApiUrl { get; set; } = "https://api.geoapify.com";
    public string StaticMapUrl { get; set; } = "https://maps.geoapify.com/v1/staticmap";
    public string MapStyle { get; set; } = "osm-bright";
    /// <summary>Credits the app may use per day (UTC); the free plan has 3,000. Past it, every feature falls back as if Geoapify were off.</summary>
    public int DailyCreditLimit { get; set; } = 2800;
    /// <summary>Share of the day's credits the background import may use, so visitors' autocomplete and maps always have some left.</summary>
    public double BackgroundShare { get; set; } = 0.5;
    public int TimeoutSeconds { get; set; } = 20;
    /// <summary>How long geocoding answers and map images are kept in memory.</summary>
    public int CacheHours { get; set; } = 24;
}

/// <summary>
/// Geoapify client: Places (OpenStreetMap places by category), address autocomplete, geocoding and static maps. Every call is counted
/// against a daily credit budget (Geoapify bills about one credit per request, and one per 20 places returned). The key never leaves the
/// server: map images are proxied through the API. Data © OpenStreetMap contributors; "Powered by Geoapify" is shown where it is used.
/// </summary>
internal sealed class GeoapifyService(IHttpClientFactory httpFactory, IOptions<GeoapifyOptions> options, IMemoryCache cache, ILogger<GeoapifyService> logger)
    : IGeoapify
{
    public const string HttpClientName = "geoapify";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private DateOnly _day;
    private int _used;

    public bool IsEnabled => Configured && Remaining(background: false) > 0;

    private bool Configured => options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    /// <summary>Credits left today for visitors (or for the background import, which may only use its share).</summary>
    private int Remaining(bool background)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _day) { _day = today; _used = 0; }
            var limit = background ? (int)(options.Value.DailyCreditLimit * options.Value.BackgroundShare) : options.Value.DailyCreditLimit;
            return limit - _used;
        }
    }

    private bool TryReserve(int credits, bool background)
    {
        if (!Configured) return false;
        lock (_gate)
        {
            if (Remaining(background) < credits) return false;
            _used += credits;
            return true;
        }
    }

    private void Charge(int credits) { lock (_gate) _used += Math.Max(0, credits); }

    private HttpClient Client()
    {
        var client = httpFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        return client;
    }

    private string Key => Uri.EscapeDataString(options.Value.ApiKey ?? "");

    // ---------------- Places ----------------

    public async Task<IReadOnlyList<ExternalPlace>?> PlacesAsync(string categories, double lat, double lng, int radiusM, int maxPlaces, bool background,
        CancellationToken ct)
    {
        var cats = string.Join(',', categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => c.All(ch => char.IsAsciiLetterLower(ch) || ch is '.' or '_')).Distinct());
        if (cats.Length == 0) return null;
        const int page = 500; // Geoapify's largest page
        var places = new Dictionary<string, ExternalPlace>();
        try
        {
            for (var offset = 0; offset < maxPlaces; offset += page)
            {
                var limit = Math.Min(page, maxPlaces - offset);
                if (!TryReserve(1, background)) return offset == 0 ? null : [.. places.Values];
                var url = string.Create(CultureInfo.InvariantCulture,
                    $"{options.Value.ApiUrl}/v2/places?categories={cats}&filter=circle:{lng:0.######},{lat:0.######},{radiusM}&bias=proximity:{lng:0.######},{lat:0.######}&limit={limit}&offset={offset}&lang=en&apiKey={Key}");
                using var response = await Client().GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // 400 usually means a category name in SubCategories.GeoapifyCategories that Geoapify doesn't know.
                    logger.LogWarning("Geoapify places ({Categories}) answered {Status}: {Body}", cats, (int)response.StatusCode,
                        Trim(await response.Content.ReadAsStringAsync(ct), 300));
                    return offset == 0 ? null : [.. places.Values];
                }
                var result = await response.Content.ReadFromJsonAsync<FeatureCollection>(Json, ct);
                var features = result?.Features ?? [];
                Charge((features.Count + 19) / 20 - 1); // one credit per 20 places, the first already reserved
                foreach (var f in features)
                    if (ToPlace(f.Properties) is { } p) places.TryAdd(p.Id, p);
                if (features.Count < limit) break;
            }
            return [.. places.Values];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Geoapify places ({Categories}) failed: {Error}", cats, ex.Message);
            return places.Count == 0 ? null : [.. places.Values];
        }
    }

    /// <summary>A Geoapify place as the OpenStreetMap element it comes from; places without an OpenStreetMap id or name are skipped.</summary>
    private static ExternalPlace? ToPlace(PlaceProperties? p)
    {
        if (p is null || string.IsNullOrWhiteSpace(p.Name) || p.Lat is not { } lat || p.Lon is not { } lon) return null;
        var raw = p.Datasource?.Raw;
        if (raw is null || !raw.TryGetValue("osm_id", out var idEl) || !raw.TryGetValue("osm_type", out var typeEl)) return null;
        var type = typeEl.ValueKind == JsonValueKind.String ? typeEl.GetString() switch { "n" or "node" => "node", "w" or "way" => "way", "r" or "relation" => "relation", _ => null } : null;
        if (type is null || !idEl.TryGetInt64(out var osmId)) return null;
        var id = $"{type}/{Math.Abs(osmId)}";
        string? Raw(string key) => raw.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Split(';')[0].Trim() : null;
        var phone = p.Contact?.Phone ?? Raw("phone") ?? Raw("contact:phone") ?? Raw("contact:mobile");
        var website = p.Website ?? Raw("website") ?? Raw("contact:website");
        if (website is not null && !website.StartsWith("http", StringComparison.OrdinalIgnoreCase)) website = "https://" + website;
        // The most specific category names the place ("healthcare.dentist" → "Dentist").
        var kind = p.Categories?.OrderByDescending(c => c.Count(ch => ch == '.')).FirstOrDefault()?.Split('.')[^1].Replace('_', ' ');
        if (kind is { Length: > 0 }) kind = char.ToUpperInvariant(kind[0]) + kind[1..];
        var address = p.AddressLine2 ?? p.Formatted;
        string? Tag(string key) => raw.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new ExternalPlace("osm", id, (Raw("name:en") ?? p.Name).Trim(), kind, address, lat, lon, phone, website,
            p.OpeningHours ?? Raw("opening_hours"), null, null, $"https://www.openstreetmap.org/{id}", "tag",
            Email: p.Contact?.Email ?? PlaceTags.Email(Tag), Description: PlaceTags.Description(Tag), Features: PlaceTags.Features(Tag));
    }

    // ---------------- Address autocomplete ----------------

    public async Task<IReadOnlyList<AddressSuggestion>?> AutocompleteAsync(string text, string countryCode, double? lat, double? lng, CancellationToken ct)
    {
        var q = text.Trim();
        if (q.Length < 3) return [];
        var cc = countryCode.Trim().ToLowerInvariant();
        var bias = lat is { } la && lng is { } lo ? string.Create(CultureInfo.InvariantCulture, $"&bias=proximity:{lo:0.####},{la:0.####}") : "";
        var key = $"geoapify-ac|{cc}|{q.ToLowerInvariant()}|{bias}";
        if (cache.TryGetValue(key, out IReadOnlyList<AddressSuggestion>? cached)) return cached;
        if (!TryReserve(1, background: false)) return null;
        try
        {
            var result = await Client().GetFromJsonAsync<GeocodeResults>(
                $"{options.Value.ApiUrl}/v1/geocode/autocomplete?text={Uri.EscapeDataString(q)}&format=json&limit=6&lang=en&filter=countrycode:{Uri.EscapeDataString(cc)}{bias}&apiKey={Key}",
                Json, ct);
            IReadOnlyList<AddressSuggestion> list = (result?.Results ?? [])
                .Where(r => r.Lat is not null && r.Lon is not null && !string.IsNullOrWhiteSpace(r.Formatted))
                .Select(r => new AddressSuggestion(r.Formatted!, r.AddressLine1, r.AddressLine2, r.Suburb, r.City ?? r.Town ?? r.Village, r.District ?? r.County,
                    r.State, r.Postcode, r.CountryCode, r.Lat!.Value, r.Lon!.Value))
                .ToList();
            cache.Set(key, list, TimeSpan.FromHours(options.Value.CacheHours));
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Geoapify autocomplete failed: {Error}", ex.Message);
            return null;
        }
    }

    // ---------------- Geocoding (place names) ----------------

    /// <summary>Settlement-type results first: a search names a town, not a street of the same name.</summary>
    private static readonly string[] PlaceRank = ["city", "county", "suburb", "district", "postcode", "street"];

    public async Task<GeocodedPlace?> GeocodeAsync(string place, string countryCode, CancellationToken ct)
    {
        var q = place.Trim();
        if (q.Length < 2) return null;
        var cc = countryCode.Trim().ToLowerInvariant();
        var key = $"geoapify-geocode|{cc}|{q.ToLowerInvariant()}";
        if (cache.TryGetValue(key, out GeocodedPlace? cached)) return cached;
        if (!TryReserve(1, background: false)) return null;
        try
        {
            var result = await Client().GetFromJsonAsync<GeocodeResults>(
                $"{options.Value.ApiUrl}/v1/geocode/search?text={Uri.EscapeDataString(q)}&format=json&limit=5&lang=en&filter=countrycode:{Uri.EscapeDataString(cc)}&apiKey={Key}",
                Json, ct);
            var best = (result?.Results ?? [])
                .Where(r => r.Lat is not null && r.Lon is not null)
                .OrderBy(r => string.Equals(r.City ?? r.Name, q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(r => Array.IndexOf(PlaceRank, r.ResultType) is var i and >= 0 ? i : PlaceRank.Length)
                .FirstOrDefault();
            var found = best is null ? null
                : new GeocodedPlace((best.City ?? best.Name ?? best.County ?? q).Trim(), best.State, best.Lat!.Value, best.Lon!.Value);
            cache.Set(key, found, found is null ? TimeSpan.FromHours(1) : TimeSpan.FromHours(options.Value.CacheHours));
            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Geoapify geocoding of {Place} failed: {Error}", q, ex.Message);
            return null;
        }
    }

    // ---------------- Static maps ----------------

    public async Task<StaticMapImage?> StaticMapAsync(double lat, double lng, int zoom, int width, int height, CancellationToken ct)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"geoapify-map|{lat:0.#####},{lng:0.#####}|{zoom}|{width}x{height}");
        if (cache.TryGetValue(key, out StaticMapImage? cached)) return cached;
        if (!TryReserve(1, background: false)) return null;
        try
        {
            var o = options.Value;
            var url = string.Create(CultureInfo.InvariantCulture,
                $"{o.StaticMapUrl}?style={Uri.EscapeDataString(o.MapStyle)}&width={width}&height={height}&scaleFactor=2&center=lonlat:{lng:0.######},{lat:0.######}&zoom={zoom}&marker=lonlat:{lng:0.######},{lat:0.######};type:awesome;color:%23f4a62c;size:large&apiKey={Key}");
            using var response = await Client().GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Geoapify static map answered {Status}", (int)response.StatusCode);
                return null;
            }
            var image = new StaticMapImage(await response.Content.ReadAsByteArrayAsync(ct), response.Content.Headers.ContentType?.MediaType ?? "image/png");
            cache.Set(key, image, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(o.CacheHours) });
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Geoapify static map failed: {Error}", ex.Message);
            return null;
        }
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private sealed record FeatureCollection(List<Feature>? Features);
    private sealed record Feature(PlaceProperties? Properties);
    private sealed record PlaceProperties(
        string? Name, string? Formatted, [property: JsonPropertyName("address_line2")] string? AddressLine2, double? Lat, double? Lon,
        List<string>? Categories, string? Website, [property: JsonPropertyName("opening_hours")] string? OpeningHours, Contact? Contact, Datasource? Datasource);
    private sealed record Contact(string? Phone, string? Email);
    private sealed record Datasource(Dictionary<string, JsonElement>? Raw);
    private sealed record GeocodeResults(List<GeocodeResult>? Results);
    private sealed record GeocodeResult(
        string? Name, string? Formatted, [property: JsonPropertyName("address_line1")] string? AddressLine1,
        [property: JsonPropertyName("address_line2")] string? AddressLine2, string? Suburb, string? City, string? Town, string? Village, string? District,
        string? County, string? State, string? Postcode, [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("result_type")] string? ResultType, double? Lat, double? Lon);
}

/// <summary>Place names by Geoapify geocoding when it's on and has credits, otherwise (or when it finds nothing) by Photon.</summary>
internal sealed class FallbackPlaceGeocoder(GeoapifyService geoapify, PhotonPlaceGeocoder photon, IGeoLocationService geo) : IPlaceGeocoder
{
    public async Task<GeocodedPlace?> GeocodeAsync(string place, CancellationToken ct) =>
        (geoapify.IsEnabled ? await geoapify.GeocodeAsync(place, geo.DefaultCountryCode, ct) : null)
        ?? await photon.GeocodeAsync(place, ct);
}
