using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Search;

public sealed class ExternalSearchOptions
{
    public const string Section = "ExternalSearch";
    /// <summary>Overpass API servers (full OpenStreetMap data: tags, phone, website, hours), tried in order when one is overloaded.</summary>
    public string[] OverpassUrls { get; set; } =
        ["https://overpass-api.de/api/interpreter", "https://overpass.private.coffee/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter", "https://overpass.kumi.systems/api/interpreter"];
    /// <summary>Time budget for one Overpass search across all servers (it keeps running in the background after a request stops waiting).</summary>
    public int OverpassTimeoutSeconds { get; set; } = 25;
    /// <summary>How long a search request waits for Overpass before answering with the faster Photon results. The page keeps polling
    /// while the full search finishes in the background (ExternalTierDto.Searching), so this stays short.</summary>
    public int OverpassWaitSeconds { get; set; } = 2;
    /// <summary>Photon geocoder (fast OpenStreetMap name search). Empty = not used.</summary>
    public string? PhotonUrl { get; set; } = "https://photon.komoot.io/api/";
    public string UserAgent { get; set; } = "CallingBell-search/1.0 (+https://callingbell.in)";
    /// <summary>OpenStreetMap API for one element with all its tags (the result detail window); {0} is e.g. "node/123".</summary>
    public string OsmApiElementUrl { get; set; } = "https://api.openstreetmap.org/api/0.6/{0}.json";
    /// <summary>How long Overpass results are kept in memory per search and place (the data changes slowly).</summary>
    public int OsmCacheHours { get; set; } = 12;
    /// <summary>How long Photon-only results are kept (shorter, so fuller Overpass results replace them).</summary>
    public int PhotonCacheMinutes { get; set; } = 30;
    /// <summary>After a server fails, don't call it again for this long.</summary>
    public int OsmRetryAfterMinutes { get; set; } = 2;
}

/// <summary>
/// Named places near a point from OpenStreetMap, cached in memory per search. Data © OpenStreetMap contributors (ODbL).
/// Two free services run side by side: Overpass (complete tags: phone, website, opening hours; often slow when busy) and Photon (fast name
/// search). A request waits a few seconds for Overpass, otherwise answers with Photon's results while Overpass finishes into the cache.
/// Selectors are validated against a strict pattern before they are put into any query.
/// </summary>
internal sealed partial class OsmPlaceSearch(IHttpClientFactory httpFactory, IMemoryCache cache, IOptions<ExternalSearchOptions> options,
    ILogger<OsmPlaceSearch> logger) : IOsmPlaceSearch
{
    public const string HttpClientName = "external-search-osm";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, Task<List<ExternalPlace>?>> Running = new();

    [GeneratedRegex("^[a-z_:]{2,40}=[a-z0-9_:]{1,60}$")] private static partial Regex TagSelector();
    [GeneratedRegex("^name~[a-z0-9 ]{2,40}$")] private static partial Regex NameSelector();

    /// <summary>Tag keys whose value describes what the place is, in order of preference.</summary>
    private static readonly string[] KindKeys = ["craft", "office", "shop", "amenity", "healthcare", "tourism", "leisure", "sport", "emergency"];
    /// <summary>Photon also returns roads, localities and the like; those are never businesses.</summary>
    private static readonly HashSet<string> NotBusinessKeys =
        ["place", "highway", "boundary", "landuse", "natural", "waterway", "railway", "route", "public_transport", "bridge", "junction"];

    public bool IsSearching(IReadOnlyCollection<string> selectors, double lat, double lng, int radiusM) =>
        Valid(selectors) is { Count: > 0 } valid && Running.ContainsKey(Key(valid, lat, lng, radiusM));

    private static List<string> Valid(IReadOnlyCollection<string> selectors) =>
        selectors.Select(s => s.Trim().ToLowerInvariant()).Where(s => TagSelector().IsMatch(s) || NameSelector().IsMatch(s)).Distinct().ToList();

    private static string Key(List<string> valid, double lat, double lng, int radiusM) =>
        string.Create(CultureInfo.InvariantCulture, $"osm|{Math.Round(lat, 3)},{Math.Round(lng, 3)}|{radiusM}|{string.Join('|', valid.Order())}");

    public async Task<IReadOnlyList<ExternalPlace>?> SearchAsync(IReadOnlyCollection<string> selectors, double lat, double lng, int radiusM, CancellationToken ct)
    {
        var valid = Valid(selectors);
        if (valid.Count == 0) return [];
        var key = Key(valid, lat, lng, radiusM);
        if (cache.TryGetValue(key, out IReadOnlyList<ExternalPlace>? complete)) return complete;

        // Overpass runs to completion in the background (one run per search at a time) and caches what it finds.
        var overpass = cache.TryGetValue("down|overpass", out _)
            ? Task.FromResult<List<ExternalPlace>?>(null)
            : Running.GetOrAdd(key, k => Task.Run(async () =>
            {
                try
                {
                    var found = await OverpassAsync(valid, lat, lng, radiusM);
                    if (found is not null) cache.Set(k, (IReadOnlyList<ExternalPlace>)found, TimeSpan.FromHours(options.Value.OsmCacheHours));
                    return found;
                }
                finally { Running.TryRemove(k, out _); }
            }, CancellationToken.None));

        var photonKey = "photon|" + key;
        if (cache.TryGetValue(photonKey, out IReadOnlyList<ExternalPlace>? quick))
        {
            // Photon results from a moment ago: answer at once. The page keeps polling while Overpass runs (IsSearching), and its
            // fuller results are served from the cache as soon as they arrive, so waiting here would only slow every poll down.
            return overpass.IsCompletedSuccessfully && overpass.Result is { } done ? done : quick;
        }

        var photon = PhotonAsync(valid, lat, lng, radiusM, ct);
        await Task.WhenAny(overpass, Task.Delay(TimeSpan.FromSeconds(options.Value.OverpassWaitSeconds), ct));
        if (overpass.IsCompletedSuccessfully && overpass.Result is { } full) return full;

        var fast = await photon;
        if (fast is not null) cache.Set(photonKey, (IReadOnlyList<ExternalPlace>)fast, TimeSpan.FromMinutes(options.Value.PhotonCacheMinutes));
        return fast;
    }

    // ---------------- Overpass ----------------

    private async Task<List<ExternalPlace>?> OverpassAsync(List<string> valid, double lat, double lng, int radiusM)
    {
        // Tags use the server's tag index directly; name words are one case-insensitive regex over the named points. The area is a
        // bounding box (a global [bbox] is far cheaper for the servers than "around" filters, which busy servers time out on);
        // results outside the radius are dropped below. The name filter is a single statement: inside the union, a helper set
        // ("named points" then "filter by name") would add every named point in the area to the results.
        var o = options.Value;
        var dLat = radiusM / 111_320.0;
        var dLng = dLat / Math.Cos(lat * Math.PI / 180);
        var bbox = string.Create(CultureInfo.InvariantCulture, $"{lat - dLat:0.#####},{lng - dLng:0.#####},{lat + dLat:0.#####},{lng + dLng:0.#####}");
        var tags = valid.Where(s => !IsName(s)).Select(s => $"nwr[\"{TagKey(s)}\"=\"{TagValue(s)}\"][\"name\"];");
        var words = valid.Where(IsName).Select(s => s[5..]).ToList();
        var byName = words.Count == 0 ? "" : $"node[\"name\"~\"{string.Join('|', words)}\",i];";
        var query = $"[out:json][timeout:{o.OverpassTimeoutSeconds}][bbox:{bbox}];({string.Concat(tags)}{byName});out center tags 300;";
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(o.OverpassTimeoutSeconds + 5));
            var client = Client();
            OverpassResult? result = null;
            foreach (var url in o.OverpassUrls)
            {
                using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
                using var response = await client.PostAsync(url, content, budget.Token);
                if (response.IsSuccessStatusCode) { result = await response.Content.ReadFromJsonAsync<OverpassResult>(Json, budget.Token); break; }
                // 429 (rate limited) and 5xx (overloaded): try the next server; anything else is a bad query.
                if ((int)response.StatusCode != 429 && (int)response.StatusCode < 500) throw new HttpRequestException($"Overpass answered {(int)response.StatusCode}");
            }
            // A busy server can answer 200 with a "runtime error" remark and no elements: that is a failure, not "nothing here".
            if (result is null || result.Remark?.Contains("error", StringComparison.OrdinalIgnoreCase) == true)
                throw new HttpRequestException($"Overpass gave no usable answer {result?.Remark}".Trim());

            var places = new List<ExternalPlace>();
            foreach (var e in result.Elements ?? [])
            {
                if (e.Tags is null) continue;
                var (elat, elng) = e.Lat is { } la && e.Lon is { } lo ? (la, lo) : e.Center is { } c ? (c.Lat, c.Lon) : (double.NaN, double.NaN);
                var name = e.Tags.GetValueOrDefault("name:en") ?? e.Tags.GetValueOrDefault("name");
                if (double.IsNaN(elat) || string.IsNullOrWhiteSpace(name)) continue;
                if (GeoMath.HaversineKm(lat, lng, elat, elng) * 1000 > radiusM) continue; // box corners

                // A place counts as a tag match when one of the requested tags is on it; otherwise it was found by name.
                var byTag = valid.Any(s => !IsName(s) && e.Tags.TryGetValue(TagKey(s), out var v) && v == TagValue(s));
                places.Add(new ExternalPlace("osm", $"{e.Type}/{e.Id}", name.Trim(), Kind(e.Tags), Address(e.Tags), elat, elng,
                    Tag(e.Tags, "phone", "contact:phone", "contact:mobile", "mobile"), Website(Tag(e.Tags, "website", "contact:website", "url")),
                    Tag(e.Tags, "opening_hours"), null, null, $"https://www.openstreetmap.org/{e.Type}/{e.Id}", byTag ? "tag" : "name"));
            }
            return places;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning("OpenStreetMap (Overpass) search failed: {Error}", ex.Message);
            cache.Set("down|overpass", true, TimeSpan.FromMinutes(o.OsmRetryAfterMinutes));
            return null;
        }
    }

    // ---------------- Photon ----------------

    private async Task<List<ExternalPlace>?> PhotonAsync(List<string> valid, double lat, double lng, int radiusM, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.PhotonUrl) || cache.TryGetValue("down|photon", out _)) return null;
        var words = valid.Where(IsName).Select(s => s[5..]).Take(5).ToList();
        if (words.Count == 0) return [];
        var dLat = radiusM / 111_320.0;
        var dLng = dLat / Math.Cos(lat * Math.PI / 180);
        var bbox = string.Create(CultureInfo.InvariantCulture, $"{lng - dLng:0.#####},{lat - dLat:0.#####},{lng + dLng:0.#####},{lat + dLat:0.#####}");
        var tagSet = valid.Where(s => !IsName(s)).ToHashSet();
        try
        {
            var client = Client();
            var pages = await Task.WhenAll(words.Select(w => client.GetFromJsonAsync<PhotonResult>(string.Create(CultureInfo.InvariantCulture,
                $"{o.PhotonUrl}?q={Uri.EscapeDataString(w)}&lat={lat}&lon={lng}&bbox={bbox}&limit=50&lang=en"), Json, ct)));
            var places = new Dictionary<string, ExternalPlace>();
            foreach (var f in pages.SelectMany(p => p?.Features ?? []))
            {
                var pr = f.Properties;
                if (pr?.Name is not { Length: > 0 } name || f.Geometry?.Coordinates is not [var flng, var flat, ..]) continue;
                if (pr.OsmKey is null || NotBusinessKeys.Contains(pr.OsmKey)) continue;
                var byTag = tagSet.Contains($"{pr.OsmKey}={pr.OsmValue}");
                // Photon matches loosely (typos, prefixes); keep only real name matches unless the tag matches.
                if (!byTag && !words.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                var type = pr.OsmType switch { "N" => "node", "W" => "way", "R" => "relation", _ => null };
                if (type is null) continue;
                var id = $"{type}/{pr.OsmId}";
                var kind = KindKeys.Contains(pr.OsmKey) && pr.OsmValue is { } v && v != "yes" ? Humanize(v) : null;
                var street = string.Join(' ', new[] { pr.Housenumber, pr.Street }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var address = string.Join(", ", new[] { street, pr.District ?? pr.Locality, pr.City, pr.Postcode }
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
                places.TryAdd(id, new ExternalPlace("osm", id, name.Trim(), kind, address.Length == 0 ? null : address, flat, flng,
                    null, null, null, null, null, $"https://www.openstreetmap.org/{id}", byTag ? "tag" : "name"));
            }
            return [.. places.Values];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("OpenStreetMap (Photon) search failed: {Error}", ex.Message);
            cache.Set("down|photon", true, TimeSpan.FromMinutes(o.OsmRetryAfterMinutes));
            return null;
        }
    }

    // ---------------- One element (detail window) ----------------

    [GeneratedRegex("^(node|way|relation)/[0-9]{1,15}$")] private static partial Regex ElementId();

    public async Task<OsmElement?> GetElementAsync(string id, CancellationToken ct)
    {
        if (!ElementId().IsMatch(id)) return null;
        var key = "osm-element|" + id;
        if (cache.TryGetValue(key, out OsmElement? cached)) return cached;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await Client().GetAsync(string.Format(CultureInfo.InvariantCulture, options.Value.OsmApiElementUrl, id), budget.Token);
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return null;
            response.EnsureSuccessStatusCode();
            var e = (await response.Content.ReadFromJsonAsync<OverpassResult>(Json, budget.Token))?.Elements?.FirstOrDefault();
            if (e is null) return null;
            var element = new OsmElement(id, e.Lat ?? e.Center?.Lat, e.Lon ?? e.Center?.Lon,
                e.Tags ?? new Dictionary<string, string>());
            cache.Set(key, element, TimeSpan.FromHours(options.Value.OsmCacheHours));
            return element;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "OpenStreetMap element {Id} could not be loaded", id);
            return null;
        }
    }

    // ---------------- Helpers ----------------

    private HttpClient Client()
    {
        var client = httpFactory.CreateClient(HttpClientName);
        if (!client.DefaultRequestHeaders.UserAgent.Any()) client.DefaultRequestHeaders.UserAgent.ParseAdd(options.Value.UserAgent);
        return client;
    }

    private static bool IsName(string selector) => selector.StartsWith("name~", StringComparison.Ordinal);
    private static string TagKey(string selector) => selector[..selector.IndexOf('=')];
    private static string TagValue(string selector) => selector[(selector.IndexOf('=') + 1)..];

    /// <summary>"interior_designer" -> "Interior designer".</summary>
    private static string? Humanize(string value)
    {
        var text = value.Split(';')[0].Replace('_', ' ').Trim();
        return text.Length == 0 ? null : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string? Tag(Dictionary<string, string> tags, params string[] keys)
    {
        foreach (var k in keys)
            if (tags.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Split(';')[0].Trim();
        return null;
    }

    private static string? Website(string? url) =>
        url is null ? null : url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;

    private static string? Kind(Dictionary<string, string> tags)
    {
        foreach (var k in KindKeys)
            if (tags.TryGetValue(k, out var v) && v is not ("yes" or "no")) return Humanize(v);
        return null;
    }

    private static string? Address(Dictionary<string, string> tags)
    {
        if (Tag(tags, "addr:full") is { } full) return full;
        var line = string.Join(' ', new[] { Tag(tags, "addr:housenumber"), Tag(tags, "addr:street") }.Where(x => x is not null));
        var parts = new[] { line, Tag(tags, "addr:suburb", "addr:neighbourhood", "addr:place"), Tag(tags, "addr:city"), Tag(tags, "addr:postcode") }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private sealed record OverpassResult(List<OverpassElement>? Elements, string? Remark);
    private sealed record OverpassElement(string Type, long Id, double? Lat, double? Lon, OverpassCenter? Center, Dictionary<string, string>? Tags);
    private sealed record OverpassCenter(double Lat, double Lon);
    private sealed record PhotonResult(List<PhotonFeature>? Features);
    private sealed record PhotonFeature(PhotonGeometry? Geometry, PhotonProperties? Properties);
    private sealed record PhotonGeometry(double[]? Coordinates);
    private sealed record PhotonProperties(
        [property: JsonPropertyName("osm_type")] string? OsmType, [property: JsonPropertyName("osm_id")] long OsmId,
        [property: JsonPropertyName("osm_key")] string? OsmKey, [property: JsonPropertyName("osm_value")] string? OsmValue,
        string? Name, string? Housenumber, string? Street, string? District, string? Locality, string? City, string? Postcode);
}
