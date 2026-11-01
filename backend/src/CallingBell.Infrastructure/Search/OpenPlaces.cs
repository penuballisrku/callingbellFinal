using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Search;

public sealed class OpenPlacesOptions
{
    public const string Section = "OpenPlaces";
    public bool Enabled { get; set; } = true;
    /// <summary>A secret ("opa_live_…"): keep it in user-secrets or an environment variable (OpenPlaces__ApiKey), never in appsettings.json.</summary>
    public string? ApiKey { get; set; }
    public string ApiUrl { get; set; } = "https://api.openplacesapi.com";
    public int TimeoutSeconds { get; set; } = 8;
    /// <summary>How long a search is kept in memory: each one counts against the plan's monthly searches, and the data changes monthly.</summary>
    public int CacheHours { get; set; } = 24;
    /// <summary>
    /// Overture's confidence that a place exists (0 to 1); weaker records are left out. Indian listings score low (real shops often 0.3),
    /// so this only drops the weakest.
    /// </summary>
    public double MinConfidence { get; set; } = 0.2;
}

/// <summary>
/// Open Places API client (GET /v1/places): Overture Maps places near a point, by category slug or text. Every request is one search from
/// the plan's monthly quota, so answers are cached; once the quota is used up (402) it stays off until the reset the API gives, and a
/// rate limit (429) pauses it for the time asked. The key is sent as a bearer token from the server only.
/// </summary>
internal sealed class OpenPlacesService(IHttpClientFactory httpFactory, IOptions<OpenPlacesOptions> options, IMemoryCache cache,
    ILogger<OpenPlacesService> logger) : IOpenPlaces
{
    public const string HttpClientName = "open-places";
    private const double MetresPerMile = 1609.344;
    /// <summary>The API's largest radius.</summary>
    private const double MaxRadiusMi = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTimeOffset _pausedUntil;

    public bool IsEnabled => options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey) && DateTimeOffset.UtcNow >= _pausedUntil;

    public async Task<IReadOnlyList<ExternalPlace>?> SearchAsync(string? category, string? text, double lat, double lng, int radiusM, int limit,
        CancellationToken ct, bool namesOnly = false)
    {
        if (!IsEnabled) return null;
        category = category?.Trim().ToLowerInvariant();
        if (category is not null && (category.Length is 0 or > 80 || !category.All(c => char.IsAsciiLetterLower(c) || c == '_'))) return null;
        text = category is null ? text?.Trim() : null;
        if (category is null && (string.IsNullOrEmpty(text) || text.Length > 128)) return null;
        // Category browse returns up to 50 places, a text search 20.
        limit = Math.Clamp(limit, 1, category is null ? 20 : 50);
        var radiusMi = Math.Round(Math.Min(radiusM / MetresPerMile, MaxRadiusMi), 1);

        var query = string.Create(CultureInfo.InvariantCulture,
            $"lat={lat:0.####}&lon={lng:0.####}&radius_mi={radiusMi}&limit={limit}&sort=distance&min_confidence={options.Value.MinConfidence}")
            + (category is null ? "&q=" + Uri.EscapeDataString(text!) + (namesOnly ? "&mode=name" : "") : "&category=" + category);
        var key = "open-places|" + query;
        if (cache.TryGetValue(key, out IReadOnlyList<ExternalPlace>? cached)) return cached;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.ApiUrl}/v1/places?{query}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
            var client = httpFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Pause(response);
                logger.LogWarning("Open Places ({Search}) answered {Status}: {Body}", category ?? text, (int)response.StatusCode,
                    Trim(await response.Content.ReadAsStringAsync(ct), 300));
                return null;
            }
            var result = await response.Content.ReadFromJsonAsync<SearchResponse>(Json, ct);
            if (result?.Meta?.Warnings is { Count: > 0 } warnings && warnings.Any(w => w.Code == "unknown_category"))
                logger.LogWarning("Open Places doesn't know the category {Category} (SubCategories.OpenPlacesCategories)", category);
            IReadOnlyList<ExternalPlace> places = (result?.Results ?? []).Select(ToPlace).OfType<ExternalPlace>().ToList();
            cache.Set(key, places, TimeSpan.FromHours(options.Value.CacheHours));
            return places;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Open Places ({Search}) failed: {Error}", category ?? text, ex.Message);
            return null;
        }
    }

    /// <summary>Out of searches (402) until the monthly reset, or rate limited (429) for the time asked; other errors don't pause.</summary>
    private void Pause(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            var reset = response.Headers.TryGetValues("X-Quota-Reset", out var values)
                && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch)
                ? DateTimeOffset.FromUnixTimeSeconds(epoch) : DateTimeOffset.UtcNow.AddHours(6);
            _pausedUntil = reset;
            logger.LogWarning("Open Places: the plan's searches are used up; off until {Reset:u}", reset);
        }
        else if (response.StatusCode == HttpStatusCode.TooManyRequests)
            _pausedUntil = DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
    }

    private static ExternalPlace? ToPlace(Place p)
    {
        if (string.IsNullOrWhiteSpace(p.PlaceId) || string.IsNullOrWhiteSpace(p.Name) || p.Lat is not { } lat || p.Lon is not { } lon) return null;
        if (p.OperatingStatus is { } status && !status.Equals("operational", StringComparison.OrdinalIgnoreCase)) return null;
        var id = p.PlaceId.StartsWith("overture:", StringComparison.Ordinal) ? p.PlaceId["overture:".Length..] : p.PlaceId;
        if (id.Length is 0 or > 100) return null;
        var (address, country) = Address(p.Address);
        return new ExternalPlace("overture", id, p.Name.Trim(), OverturePlaceText.Label(p.Category), address, lat, lon,
            OverturePlaceText.Phone(Text(p.Phone)), Text(p.Website), null, null, null,
            null, p.Match == "name" ? "name" : "tag", CountryCode: country);
    }

    /// <summary>
    /// One line from the address fields, in the order a postal address reads ("12 MG Road, Kukatpally, Hyderabad, Telangana 500072"), and
    /// the country code. Only text fields are used; the API documents locality and country_code, the others are read when present.
    /// </summary>
    private static (string? Address, string? CountryCode) Address(JsonElement? address)
    {
        if (address is not { ValueKind: JsonValueKind.Object } a) return (null, null);
        string? Field(params string[] names)
        {
            foreach (var n in names)
                if (a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())) return v.GetString()!.Trim();
            return null;
        }
        var region = Field("region", "state");
        var postcode = Field("postcode", "postal_code");
        var parts = new[] { Field("freeform", "street", "address_line"), Field("neighborhood", "neighbourhood"), Field("locality", "city"),
            string.Join(' ', new[] { region, postcode }.Where(s => s is not null)) };
        var line = string.Join(", ", parts.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase));
        var country = Field("country_code", "country");
        return (line.Length == 0 ? null : Trim(line, 300), country is { Length: 2 } ? country.ToUpperInvariant() : null);
    }

    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : Trim(s.Trim(), 300);

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed record SearchResponse(List<Place>? Results, Meta? Meta);
    private sealed record Meta(List<Warning>? Warnings);
    private sealed record Warning(string? Code);
    private sealed record Place(
        [property: JsonPropertyName("place_id")] string? PlaceId, string? Name, double? Lat, double? Lon, string? Category, JsonElement? Address,
        string? Phone, string? Website, [property: JsonPropertyName("operating_status")] string? OperatingStatus, string? Match);
}

/// <summary>How Overture values are shown, the same for the API's results and the local copy's.</summary>
internal static class OverturePlaceText
{
    /// <summary>"electrician" → "Electrician", "dental_clinic" → "Dental clinic".</summary>
    public static string? Label(string? category) =>
        string.IsNullOrWhiteSpace(category) ? null : char.ToUpperInvariant(category[0]) + category[1..].Replace('_', ' ');

    /// <summary>Indian numbers as the rest of the site shows them ("+91 98490 12345"); others as given (E.164).</summary>
    public static string? Phone(string? phone) =>
        phone is not null && phone.StartsWith("+91", StringComparison.Ordinal) ? Phones.Normalize(phone) : phone;
}
