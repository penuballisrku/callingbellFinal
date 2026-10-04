using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Geo;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Search;

/// <param name="DistanceKm">From the selected area (or the visitor's location / city centre).</param>
/// <param name="DirectionsUrl">Google Maps directions to the place.</param>
/// <param name="SourceUrl">The place's page at its source (OpenStreetMap or Google Maps).</param>
/// <param name="PhotoUrl">Google Maps only: the place's photo, served through <c>GET /api/places/photo</c>.</param>
/// <param name="PhotoCredit">The photo's author, which Google requires to be shown with it.</param>
public sealed record ExternalPlaceDto(string Id, string Name, string? Kind, string? Address, string? Phone, string? Website, string? OpeningHours,
    decimal? Rating, int? RatingCount, double DistanceKm, string DirectionsUrl, string? SourceUrl,
    string? PhotoUrl = null, string? PhotoCredit = null, string? PhotoCreditUrl = null);

/// <param name="Status">"ready", "off" (not configured), "unavailable" (source unreachable) or "skipped" (nothing to search for).</param>
/// <param name="AiStatus">AI tier only: "ranked" (picked and ordered by the AI), "pending" (being picked; poll) or "off" (nearest first).</param>
/// <param name="Total">Results after duplicates were removed.</param>
/// <param name="Duplicates">Results dropped because they are already listed above (registered businesses or an earlier source).</param>
/// <param name="Searching">AI tier only: the full OpenStreetMap search is still running, so these results are partial; poll for the rest.</param>
public sealed record ExternalTierDto(string Status, string? AiStatus, int Total, int Duplicates, IReadOnlyList<ExternalPlaceDto> Items,
    bool Searching = false);

/// <param name="Query">What was searched for, e.g. "Interior Designers".</param>
/// <param name="PlaceName">Selected area (or the visitor's locality / the city) the results are near.</param>
/// <param name="Origin">"area", "ip", "city" or "place" (an unlisted place named in the search): where <paramref name="PlaceName"/> came from.
/// "place-not-found" when that place could not be found.</param>
public sealed record ExternalSearchDto(string? Query, string? PlaceName, string? CityName, string? Origin, ExternalTierDto Ai, ExternalTierDto Google);

/// <param name="Place">A place that is not a listed city or area (e.g. "Nellore" from "lawyers in Nellore"): results are near it instead.</param>
public sealed record ExternalSearchQuery(string? ClientIp, string? Q, string? Category, string? Sub, string? City, Guid? AreaId, double RadiusKm,
    string? Place = null)
    : IRequest<ExternalSearchDto>;

/// <summary>
/// Search results from beyond the platform. Search priority is: registered businesses from the database (<c>GET /api/businesses</c>), then
/// <list type="number">
/// <item>"Google Maps": Google Places Text Search near the selected area, when an API key is configured.</item>
/// <item>"AI Recommended": real places near the selected area from OpenStreetMap, found with the sub-category's OpenStreetMap tags (the
/// free local AI works out the sub-category for free-text searches) and then picked and ordered by the AI. The AI never invents places.</item>
/// </list>
/// Each source leaves out places a higher-priority source already listed.
/// </summary>
public sealed class ExternalSearchHandler(IUnitOfWork uow, VisitorOriginResolver origins, IOsmPlaceSearch osm, IGooglePlacesSearch google,
    ISearchAssistant assistant, IPlaceGeocoder geocoder, ReferenceDataCache reference) : IRequestHandler<ExternalSearchQuery, ExternalSearchDto>
{
    /// <summary>Search radius around the selected area; results are listed nearest first.</summary>
    private const int RadiusM = 8_000;
    /// <summary>When the area finds fewer places than this, the search widens to the whole city.</summary>
    private const int MinNear = 6;
    private const int CityRadiusM = 25_000;
    private const int MaxAiResults = 40;
    private const int MaxSubCategories = 3;
    private const int PhotoWidthPx = 480;
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "near", "me", "nearby", "best", "top", "in", "at", "around", "for", "the", "a", "an", "good", "services", "service", "open", "now", "available", "online" };

    public async Task<ExternalSearchDto> Handle(ExternalSearchQuery r, CancellationToken ct)
    {
        var skipped = new ExternalTierDto("skipped", null, 0, 0, []);
        var googleOff = new ExternalTierDto(google.IsEnabled ? "skipped" : "off", null, 0, 0, []);

        // Where: the selected area first, then the visitor's location within the city, then the city centre.
        string? areaSlug = null, citySlug = r.City;
        if (r.AreaId is { } areaId)
        {
            var area = await uow.Repository<Area>().QueryNoTracking().Where(a => a.Id == areaId)
                .Select(a => new { a.Slug, CitySlug = a.City.Slug }).FirstOrDefaultAsync(ct);
            if (area is not null) { areaSlug = area.Slug; citySlug ??= area.CitySlug; }
        }
        // An unlisted place named in the search ("lawyers in Nellore") wins; there are no registered businesses there.
        var origin = !string.IsNullOrWhiteSpace(r.Place)
            ? await geocoder.GeocodeAsync(r.Place, ct) is { } g
                ? new VisitorOrigin(Guid.Empty, "", g.Name, g.State ?? "", g.Name, g.Latitude, g.Longitude, "place", $"place|{g.Name.ToLowerInvariant()}")
                : null
            : await origins.ResolveAsync(r.ClientIp, citySlug, areaSlug, r.RadiusKm, ct);

        // What: the sub-categories searched for, with their OpenStreetMap tags.
        var subs = (await reference.ActiveSubCategoriesAsync(ct)).Select(s => new Sub(s.Slug, s.Name, s.CategorySlug, s.CategoryName, s.OsmTags)).ToList();
        var q = (r.Q ?? "").Trim();
        var label = subs.FirstOrDefault(s => s.Slug == r.Sub)?.Name
                    ?? subs.FirstOrDefault(s => s.CategorySlug == r.Category)?.CategoryName
                    ?? (q.Length > 0 ? q : null);
        if (origin is null && !string.IsNullOrWhiteSpace(r.Place))
            return new ExternalSearchDto(label, r.Place.Trim(), null, "place-not-found", skipped, googleOff);
        if (origin is not { Lat: { } lat, Lng: { } lng } || label is null)
            return new ExternalSearchDto(label, origin?.PlaceName, origin?.CityName, origin?.Source, skipped, googleOff);

        List<Sub> wanted;
        string? intentKey = null;
        if (!string.IsNullOrWhiteSpace(r.Sub)) wanted = subs.Where(s => s.Slug == r.Sub).ToList();
        else if (q.Length > 0) wanted = MatchSubs(q, subs);
        else wanted = subs.Where(s => s.CategorySlug == r.Category).ToList();

        // A free-text search that names no sub-category: the AI works out which ones it means; until then, places whose name has the words.
        if (wanted.Count == 0 && q.Length > 0 && assistant.IsEnabled)
        {
            intentKey = "intent|" + string.Join(' ', Words(q));
            if (assistant.GetIntent(intentKey) is { } intent)
                wanted = subs.Where(s => intent.SubCategorySlugs.Contains(s.Slug)).Take(MaxSubCategories).ToList();
            else
                assistant.RequestIntent(intentKey, q, subs.Select(s => (s.Slug, $"{s.Name} ({s.CategoryName})")).ToList());
        }
        var selectors = wanted.Count > 0
            ? wanted.SelectMany(s => (s.OsmTags ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct().Take(40).ToList()
            : Words(q).Where(w => w.Length >= 4).Take(3).Select(w => "name~" + w).ToList();
        if (wanted.Count > 0) label = wanted.Count == 1 ? wanted[0].Name : label;

        // Registered businesses nearby, so places already listed above aren't repeated.
        var registered = await uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => b.CityId == origin.CityId && b.Latitude != null && b.Longitude != null)
            .Select(b => new Known(b.Name, b.PhoneNumber, (double)b.Latitude!, (double)b.Longitude!))
            .ToListAsync(ct);

        // Both sources are fetched at once; results are then de-duplicated in priority order (database, Google Maps, AI), so each tier
        // only shows places that no higher tier already listed.
        var googleTask = google.IsEnabled
            ? google.SearchAsync($"{label} in {Where(origin)}", lat, lng, RadiusM, ct)
            : Task.FromResult<IReadOnlyList<ExternalPlace>?>([]);
        var osmTask = selectors.Count == 0 ? Task.FromResult<IReadOnlyList<ExternalPlace>?>(null) : SearchOsmAsync(selectors, lat, lng, ct);
        await Task.WhenAll(googleTask, osmTask);
        var shown = new List<Known>(registered);

        // 2. Google Maps (after the registered businesses).
        var googleTier = googleOff;
        if (google.IsEnabled)
        {
            var places = await googleTask;
            if (places is null) googleTier = new ExternalTierDto("unavailable", null, 0, 0, []);
            else
            {
                var (unique, duplicates) = Dedupe(places, shown);
                var ordered = unique.OrderBy(p => Km(p, lat, lng)).ToList();
                shown.AddRange(ordered.Select(p => new Known(p.Name, p.Phone, p.Latitude, p.Longitude)));
                googleTier = new ExternalTierDto("ready", null, ordered.Count, duplicates, ordered.Select(p => ToDto(p, lat, lng)).ToList());
            }
        }

        // 3. AI recommended: real OpenStreetMap places, picked by the free local AI.
        ExternalTierDto aiTier;
        if (selectors.Count == 0) aiTier = skipped;
        else if (await osmTask is not { } osmPlaces) aiTier = new ExternalTierDto("unavailable", null, 0, 0, [], OsmSearching(selectors, lat, lng));
        else
        {
            var (unique, duplicates) = Dedupe(osmPlaces, shown);
            var nearest = unique.OrderBy(p => Km(p, lat, lng)).Take(MaxAiResults).ToList();
            var (picked, aiStatus) = Rank(nearest, label, origin, nameOnly: wanted.Count == 0);
            aiTier = new ExternalTierDto("ready", aiStatus, picked.Count, duplicates, picked.Select(p => ToDto(p, lat, lng)).ToList(),
                OsmSearching(selectors, lat, lng));
        }

        return new ExternalSearchDto(label, origin.PlaceName, origin.CityName, origin.Source, aiTier, googleTier);
    }

    /// <summary>OpenStreetMap places near the area, widening to the whole city when the area has few. Null when OpenStreetMap is unreachable.</summary>
    private async Task<IReadOnlyList<ExternalPlace>?> SearchOsmAsync(List<string> selectors, double lat, double lng, CancellationToken ct)
    {
        var places = await osm.SearchAsync(selectors, lat, lng, RadiusM, ct);
        if (places is { Count: < MinNear }) places = await osm.SearchAsync(selectors, lat, lng, CityRadiusM, ct) ?? places;
        return places;
    }

    /// <summary>Whether the full OpenStreetMap search (area or city-wide) is still finishing in the background.</summary>
    private bool OsmSearching(List<string> selectors, double lat, double lng) =>
        osm.IsSearching(selectors, lat, lng, RadiusM) || osm.IsSearching(selectors, lat, lng, CityRadiusM);

    /// <summary>
    /// Orders places by the AI's pick when it is ready (asking for it otherwise): its picks first, then the other places nearest first. Only
    /// when the search fell back to matching words in names (<paramref name="nameOnly"/>) are places it didn't pick left out.
    /// </summary>
    private (List<ExternalPlace> Places, string AiStatus) Rank(List<ExternalPlace> places, string label, VisitorOrigin origin, bool nameOnly)
    {
        if (!assistant.IsEnabled || places.Count < 2) return (places, "off");
        var key = $"rank|{string.Join(' ', Words(label))}|{origin.CitySlug}|{origin.PlaceKey}|{string.Join(',', places.Select(p => p.Id)).GetHashCode():x}";
        var ranking = assistant.GetRanking(key);
        if (ranking is null)
        {
            assistant.RequestRanking(key, label, Where(origin),
                places.Select(p => new AiPlaceCandidate(p.Id, p.Name, p.Kind)).ToList());
            return (places, assistant.IsRankingPending(key) ? "pending" : "off");
        }
        var byId = places.ToDictionary(p => p.Id);
        var picked = ranking.Relevant.Where(byId.ContainsKey).Distinct().Select(id => byId[id]).ToList();
        IEnumerable<ExternalPlace> rest = nameOnly ? [] : places.Where(p => !ranking.Relevant.Contains(p.Id));
        return ([.. picked, .. rest], "ranked");
    }

    private static (List<ExternalPlace> Unique, int Duplicates) Dedupe(IEnumerable<ExternalPlace> places, List<Known> shown)
    {
        var unique = new List<ExternalPlace>();
        var duplicates = 0;
        foreach (var p in places)
        {
            var dup = shown.Any(k => NameMatch.SameBusiness(p.Name, p.Phone, p.Latitude, p.Longitude, k.Name, k.Phone, k.Lat, k.Lng))
                      || unique.Any(u => NameMatch.SameBusiness(p.Name, p.Phone, p.Latitude, p.Longitude, u.Name, u.Phone, u.Latitude, u.Longitude));
            if (dup) duplicates++; else unique.Add(p);
        }
        return (unique, duplicates);
    }

    /// <summary>Sub-categories whose name contains every search word (word prefixes, simple plurals): "interior designers" -> Interior Designers.</summary>
    private static List<Sub> MatchSubs(string q, List<Sub> subs)
    {
        var words = Words(q).ToList();
        if (words.Count == 0) return [];
        static string Stem(string w) => w.Length > 4 && w.EndsWith('s') ? w[..^1] : w;
        bool Matches(string name)
        {
            var nameWords = Words(name).ToList();
            return words.All(w => nameWords.Any(n => n.StartsWith(Stem(w), StringComparison.Ordinal) || Stem(n) == Stem(w)));
        }
        var direct = subs.Where(s => Matches(s.Name)).ToList();
        return (direct.Count > 0 ? direct : subs.Where(s => Matches(s.CategoryName)).ToList()).Take(MaxSubCategories).ToList();
    }

    private static IEnumerable<string> Words(string text) =>
        text.ToLowerInvariant().Split([' ', ',', '.', '&', '/', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 1 && !StopWords.Contains(w));

    /// <summary>"Madhapur, Hyderabad", or just "Nellore" when the place is the town itself.</summary>
    private static string Where(VisitorOrigin o) =>
        string.IsNullOrWhiteSpace(o.CityName) || o.CityName.Equals(o.PlaceName, StringComparison.OrdinalIgnoreCase) ? o.PlaceName : $"{o.PlaceName}, {o.CityName}";

    private static double Km(ExternalPlace p, double lat, double lng) => GeoMath.HaversineKm(lat, lng, p.Latitude, p.Longitude);

    private static ExternalPlaceDto ToDto(ExternalPlace p, double lat, double lng)
    {
        var directions = p.Source == "google"
            ? $"https://www.google.com/maps/dir/?api=1&destination={Uri.EscapeDataString(p.Name)}&destination_place_id={Uri.EscapeDataString(p.Id)}"
            : FormattableString.Invariant($"https://www.google.com/maps/dir/?api=1&destination={p.Latitude:0.######},{p.Longitude:0.######}");
        var credit = p.Photo?.Attributions.FirstOrDefault();
        return new ExternalPlaceDto($"{p.Source}:{p.Id}", p.Name, p.Kind, p.Address, p.Phone, p.Website, p.OpeningHours, p.Rating, p.RatingCount,
            Math.Round(Km(p, lat, lng), 1), directions, p.SourceUrl,
            p.Photo is null ? null : $"/api/places/photo?name={Uri.EscapeDataString(p.Photo.Name)}&maxWidth={PhotoWidthPx}",
            credit?.DisplayName, credit?.Uri);
    }

    private sealed record Sub(string Slug, string Name, string CategorySlug, string CategoryName, string? OsmTags);

    private sealed record Known(string Name, string? Phone, double Lat, double Lng);
}
