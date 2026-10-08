using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Geo;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Search;

/// <param name="DistanceKm">From the selected area (or the visitor's location / city centre).</param>
/// <param name="DirectionsUrl">Google Maps directions to the place.</param>
/// <param name="SourceUrl">The place's page at its source (OpenStreetMap or Google Maps).</param>
/// <param name="PhotoUrl">Google Maps only: the place's photo, served through <c>GET /api/places/photo</c>.</param>
/// <param name="PhotoCredit">The photo's author, which Google requires to be shown with it.</param>
/// <param name="AiReason">AI tier only: why the AI picked this place, in plain words.</param>
public sealed record ExternalPlaceDto(string Id, string Name, string? Kind, string? Address, string? Phone, string? Website, string? OpeningHours,
    decimal? Rating, int? RatingCount, double DistanceKm, string DirectionsUrl, string? SourceUrl,
    string? PhotoUrl = null, string? PhotoCredit = null, string? PhotoCreditUrl = null, string? AiReason = null,
    double? Latitude = null, double? Longitude = null, bool? OpenNow = null);

/// <param name="Status">"ready", "off" (not configured), "unavailable" (source unreachable) or "skipped" (nothing to search for).</param>
/// <param name="AiStatus">AI tier only: "ranked" (picked and ordered by the AI), "pending" (being picked; poll) or "off" (nearest first).</param>
/// <param name="Total">Results after duplicates were removed.</param>
/// <param name="Duplicates">Results dropped because they are already listed above (registered businesses or an earlier source).</param>
/// <param name="Searching">AI tier only: the full OpenStreetMap search is still running, so these results are partial; poll for the rest.</param>
/// <param name="NextPageToken">Google tier: pass as googlePage to get the next page of the same search; null on the last page.</param>
public sealed record ExternalTierDto(string Status, string? AiStatus, int Total, int Duplicates, IReadOnlyList<ExternalPlaceDto> Items,
    bool Searching = false, string? NextPageToken = null);

/// <param name="Query">What was searched for, e.g. "Interior Designers".</param>
/// <param name="PlaceName">Selected area (or the visitor's locality / the city) the results are near.</param>
/// <param name="Origin">"area", "ip", "city" or "place" (an unlisted place named in the search): where <paramref name="PlaceName"/> came from.
/// "place-not-found" when that place could not be found.</param>
/// <param name="Insight">The AI's short overview of the results, written from live data (see <see cref="ExternalInsightDto"/>).</param>
public sealed record ExternalSearchDto(string? Query, string? PlaceName, string? CityName, string? Origin, ExternalTierDto Ai, ExternalTierDto Google,
    ExternalInsightDto? Insight = null);

/// <param name="Status">"ready" (<paramref name="Text"/> is set), "pending" (being written; poll) or "off" (AI disabled, unavailable or nothing to say).</param>
/// <param name="Text">A few sentences helping the visitor choose, using only the places found and Calling Bell's own figures.</param>
public sealed record ExternalInsightDto(string Status, string? Text);

/// <param name="Place">A place that is not a listed city or area (e.g. "Nellore" from "lawyers in Nellore"): results are near it instead.</param>
/// <param name="Tiers">
/// Which results to produce: <see cref="ExternalTiers.Google"/> (Google Maps only, fetched once per search), <see cref="ExternalTiers.Ai"/>
/// (the AI recommended places and overview, polled while the AI works, without calling Google again) or null for both.
/// </param>
/// <param name="GooglePlaces">AI tier only: the Google Maps results the page already shows, so the AI tier leaves out the same places and the
/// overview can mention them. Used for this request only; nothing from Google is stored on the server.</param>
/// <param name="GooglePageToken">The Google tier's <see cref="ExternalTierDto.NextPageToken"/> from the previous page of this search.</param>
public sealed record ExternalSearchQuery(string? ClientIp, string? Q, string? Category, string? Sub, string? City, Guid? AreaId, double RadiusKm,
    string? Place = null, string? Tiers = null, IReadOnlyList<ShownGooglePlace>? GooglePlaces = null, string? GooglePageToken = null)
    : IRequest<ExternalSearchDto>;

public static class ExternalTiers
{
    public const string Google = "google";
    public const string Ai = "ai";
}

/// <summary>A Google Maps result as shown on the page, sent back with AI tier requests (see <see cref="ExternalSearchQuery.GooglePlaces"/>).</summary>
public sealed record ShownGooglePlace(string Name, string? Kind, double? Latitude, double? Longitude, string? Phone, double DistanceKm,
    decimal? Rating, int? RatingCount, string? OpeningHours);

public sealed class ExternalSearchValidator : AbstractValidator<ExternalSearchQuery>
{
    public const int MaxShownGooglePlaces = 60;

    public ExternalSearchValidator()
    {
        RuleFor(x => x.Tiers).Must(t => t is null or ExternalTiers.Google or ExternalTiers.Ai).WithMessage("tiers must be \"google\" or \"ai\".");
        // Everything the page shows from Google: up to three pages of 20 (Google's limit per search).
        RuleFor(x => x.GooglePlaces).Must(g => g is null || g.Count <= MaxShownGooglePlaces).WithMessage($"At most {MaxShownGooglePlaces} Google places.");
        RuleForEach(x => x.GooglePlaces).ChildRules(p =>
        {
            p.RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
            p.RuleFor(x => x.Kind).MaximumLength(80);
            p.RuleFor(x => x.Phone).MaximumLength(40);
            p.RuleFor(x => x.OpeningHours).MaximumLength(400);
            p.RuleFor(x => x.DistanceKm).InclusiveBetween(0, 500);
            p.RuleFor(x => x.Rating).InclusiveBetween(0, 5);
        });
    }
}

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
    ISearchAssistant assistant, ISemanticCatalog semantic, IPlaceGeocoder geocoder, ReferenceDataCache reference, CountryPricingService pricing,
    IGeoLocationService geo) : IRequestHandler<ExternalSearchQuery, ExternalSearchDto>
{
    /// <summary>Search radius around the selected area; results are listed nearest first.</summary>
    private const int RadiusM = 8_000;
    /// <summary>When the area finds fewer places than this, the search widens to the whole city.</summary>
    private const int MinNear = 6;
    private const int CityRadiusM = 25_000;
    private const int MaxAiResults = 40;
    /// <summary>How many of the nearest places the AI judges; the rest follow nearest first.</summary>
    private const int MaxRanked = 20;
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

        var withGoogle = r.Tiers != ExternalTiers.Ai;
        var withAi = r.Tiers != ExternalTiers.Google;

        // A free-text search that names no sub-category: the fast local AI (embeddings) matches it by meaning first. Only the AI tier needs
        // that (for OpenStreetMap's tags); Google searches the words as typed, so a Google request doesn't wait for the model.
        if (withAi && wanted.Count == 0 && q.Length > 0 && await semantic.MatchAsync(q, MaxSubCategories, ct) is { Confident: true } match)
        {
            var top = match.Top[0].Score;
            var slugs = match.Top.Where(m => top - m.Score <= 0.04).Select(m => m.SubCategorySlug).ToList();
            wanted = subs.Where(s => slugs.Contains(s.Slug)).OrderBy(s => slugs.IndexOf(s.Slug)).ToList();
        }
        // Still nothing: the chat model works out which ones it means; until then, places whose name has the words.
        if (withAi && wanted.Count == 0 && q.Length > 0 && assistant.IsEnabled)
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
        // only shows places that no higher tier already listed. The page asks for Google once and polls only the AI tier (Tiers), sending
        // back the Google places it shows, so Google isn't called again on every poll.
        var googleTask = google.IsEnabled && withGoogle
            ? google.SearchAsync($"{label} in {Where(origin)}", lat, lng, RadiusM, r.GooglePageToken, ct)
            : Task.FromResult<GoogleSearchPage?>(new GoogleSearchPage([], null));
        var osmTask = !withAi || selectors.Count == 0 ? Task.FromResult<IReadOnlyList<ExternalPlace>?>(null) : SearchOsmAsync(selectors, lat, lng, ct);
        await Task.WhenAll(googleTask, osmTask);
        var shown = new List<Known>(registered);

        // 2. Google Maps (after the registered businesses).
        var googleTier = googleOff;
        if (!withGoogle)
        {
            // AI tier request: the Google results the page shows (for duplicates and the overview); not returned again.
            var shownGoogle = (r.GooglePlaces ?? []).Select(p => FromShown(p, lat, lng)).ToList();
            shown.AddRange((r.GooglePlaces ?? []).Where(p => p.Latitude is not null && p.Longitude is not null)
                .Select(p => new Known(p.Name, p.Phone, p.Latitude!.Value, p.Longitude!.Value)));
            googleTier = new ExternalTierDto(google.IsEnabled ? "skipped" : "off", null, shownGoogle.Count, 0, shownGoogle);
        }
        else if (google.IsEnabled)
        {
            var page = await googleTask;
            if (page is null) googleTier = new ExternalTierDto("unavailable", null, 0, 0, []);
            else
            {
                var (unique, duplicates) = Dedupe(page.Places, shown);
                var ordered = unique.OrderBy(p => Km(p, lat, lng)).ToList();
                shown.AddRange(ordered.Select(p => new Known(p.Name, p.Phone, p.Latitude, p.Longitude)));
                googleTier = new ExternalTierDto("ready", null, ordered.Count, duplicates, ordered.Select(p => ToDto(p, lat, lng)).ToList(),
                    NextPageToken: page.NextPageToken);
            }
        }

        // 3. AI recommended: real OpenStreetMap places, picked by the free local AI.
        ExternalTierDto aiTier;
        if (!withAi) return new ExternalSearchDto(label, origin.PlaceName, origin.CityName, origin.Source, skipped, googleTier);
        if (selectors.Count == 0) aiTier = skipped;
        else if (await osmTask is not { } osmPlaces) aiTier = new ExternalTierDto("unavailable", null, 0, 0, [], OsmSearching(selectors, lat, lng));
        else
        {
            var (unique, duplicates) = Dedupe(osmPlaces, shown);
            var nearest = unique.OrderBy(p => Km(p, lat, lng)).Take(MaxAiResults).ToList();
            var (picked, aiStatus, reasons) = Rank(nearest, label, origin, lat, lng, nameOnly: wanted.Count == 0);
            aiTier = new ExternalTierDto("ready", aiStatus, picked.Count, duplicates,
                picked.Select(p => ToDto(p, lat, lng) with { AiReason = reasons?.GetValueOrDefault(p.Id) }).ToList(),
                OsmSearching(selectors, lat, lng));
        }

        // Prices in the overview: in the currency of the country searched in (an unlisted place: the visitor's country).
        var money = origin.CityId != Guid.Empty
            ? await pricing.ForCityAsync(origin.CityId, ct)
            : await pricing.ForCountryAsync(VisitorIp.Parse(r.ClientIp) is { } ip ? geo.CountryCodeFor(ip) : null, ct);
        var insight = await InsightAsync(label, origin, wanted, googleTier, aiTier, money, ct);
        // The Google places sent with an AI tier request were only needed above; the page already has them.
        if (!withGoogle) googleTier = googleTier with { Total = 0, Items = [] };
        return new ExternalSearchDto(label, origin.PlaceName, origin.CityName, origin.Source, aiTier, googleTier, insight);
    }

    /// <summary>A Google place the page sent back, in the shape the overview's facts use. Text is made safe to quote in a prompt.</summary>
    private static ExternalPlaceDto FromShown(ShownGooglePlace p, double lat, double lng)
    {
        static string? Clean(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null
            : new string(s.ReplaceLineEndings(" ").Where(c => !char.IsControl(c) && c is not ('"' or '`' or '\\')).ToArray()).Trim() is var t && t.Length > max ? t[..max] : t;
        var km = p.Latitude is { } plat && p.Longitude is { } plng ? Math.Round(GeoMath.HaversineKm(lat, lng, plat, plng), 1) : p.DistanceKm;
        return new ExternalPlaceDto("google:shown", Clean(p.Name, 150) ?? "", Clean(p.Kind, 80), null, Clean(p.Phone, 40), null, Clean(p.OpeningHours, 400),
            p.Rating, p.RatingCount, km, "", null);
    }

    /// <summary>
    /// The AI's overview of the results. The facts are live: the places found (Google Maps and the AI's OpenStreetMap picks), the matching
    /// businesses registered on Calling Bell nearby, and Calling Bell's prices and most booked services for the searched service. It is
    /// written once the OpenStreetMap search has finished, so it covers the places shown. It doesn't wait for the AI's ranking (both
    /// are queued at once), and its cache key ignores the places themselves, so re-ranking or shifting map results don't start it over.
    /// </summary>
    private async Task<ExternalInsightDto> InsightAsync(string label, VisitorOrigin origin, List<Sub> wanted, ExternalTierDto googleTier,
        ExternalTierDto aiTier, CountryPrice money, CancellationToken ct)
    {
        if (!assistant.IsEnabled) return new("off", null);
        if (aiTier.Searching) return new("pending", null);

        var slugs = wanted.Select(s => s.Slug).ToList();
        var matching = uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => b.SubCategory != null && slugs.Contains(b.SubCategory.Slug));
        var nearby = matching.Where(b => b.CityId == origin.CityId);
        var registeredCount = slugs.Count == 0 ? 0 : await nearby.CountAsync(ct);
        var registered = registeredCount == 0 ? [] : await nearby
            .OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount).Take(3)
            .Select(b => new { b.Name, b.Area, b.AverageRating, b.ReviewCount, Verified = b.VerifiedOn != null,
                From = b.Services.Where(s => s.IsActive && s.Price > 0).Min(s => (decimal?)s.Price) })
            .ToListAsync(ct);
        // Calling Bell's prices and most booked services for this service across the platform: a guide to what it typically costs.
        var prices = slugs.Count == 0 ? [] : await matching.SelectMany(b => b.Services.Where(s => s.IsActive && s.Price > 0).Select(s => s.Price)).ToListAsync(ct);
        var since = DateTimeOffset.UtcNow.AddDays(-180);
        var booked = slugs.Count == 0 ? [] : await uow.Repository<Booking>().QueryNoTracking()
            .Where(k => k.CreatedOn >= since && k.Business.SubCategory != null && slugs.Contains(k.Business.SubCategory.Slug))
            .GroupBy(k => k.Service.Name).Select(g => new { Name = g.Key, Count = g.Count(), From = g.Min(k => k.Amount) })
            .OrderByDescending(g => g.Count).Take(4).ToListAsync(ct);

        var google = googleTier.Items.Take(4).ToList();
        // Before the ranking is ready these are the nearest; afterwards the AI's picks.
        var picks = aiTier.Items.Take(5).ToList();
        if (google.Count == 0 && picks.Count == 0 && registeredCount == 0) return new("off", null);

        // A registered business's own price is shown as it charges it; Calling Bell's platform-wide figures (in rupees) are converted
        // to fair local prices for the country searched in.
        string Own(decimal? p) => money.Format(p);
        string Typical(decimal? p) => money.Format(p is { } v ? money.Convert(v) : null);
        string Place(ExternalPlaceDto p) =>
            $"- {p.Name}{(p.Kind is null ? "" : $" ({p.Kind})")}: {p.DistanceKm:0.0} km away" +
            (p.Rating is { } r ? $", rated {r:0.0}{(p.RatingCount is { } n ? $" from {n} reviews" : "")}" : ", no rating") +
            (p.Phone is null ? "" : ", phone listed") + (p.OpeningHours is null ? "" : $", hours {p.OpeningHours}") + ". NOT on Calling Bell: cannot be booked here, contact them directly";
        var where = Where(origin);
        var facts = new System.Text.StringBuilder();
        facts.AppendLine($"Searched for: {label}, near {where}");
        facts.AppendLine(registeredCount == 0
            ? $"Registered on Calling Bell near {where}: none yet for this service (customers can't book them through Calling Bell)."
            : $"Registered on Calling Bell near {where}: {registeredCount} (bookable with verified reviews). Top rated:");
        foreach (var b in registered)
            facts.AppendLine($"- {b.Name}{(b.Area is null ? "" : $" ({b.Area})")}: " +
                $"{(b.ReviewCount > 0 ? $"rated {b.AverageRating:0.0} from {b.ReviewCount} reviews" : "no reviews yet")}, from {Own(b.From)}{(b.Verified ? ", verified" : "")}. On Calling Bell: can be booked here");
        if (prices.Count > 0)
            facts.AppendLine($"Typical prices for {label} on Calling Bell: {Typical(prices.Min())} to {Typical(prices.Max())}, average {Typical(Math.Round(prices.Average()))}");
        if (booked.Count > 0)
            facts.AppendLine($"Most booked {label} services on Calling Bell (last 6 months): " +
                string.Join("; ", booked.Select(b => $"{b.Name} ({b.Count} bookings, from {Typical(b.From)})")));
        if (google.Count > 0) { facts.AppendLine("On Google Maps nearby (not on Calling Bell):"); foreach (var p in google) facts.AppendLine(Place(p)); }
        if (picks.Count > 0)
        {
            facts.AppendLine(aiTier.AiStatus == "ranked" ? "AI-picked places from OpenStreetMap (not on Calling Bell), best match first:" : "Places from OpenStreetMap nearby (not on Calling Bell), nearest first:");
            foreach (var p in picks) facts.AppendLine(Place(p));
        }

        // Keyed by the search and the place only: map results shift a little between requests, which must not start a new answer.
        var key = $"insight|{string.Join(' ', Words(label))}|{origin.PlaceKey}|{registeredCount}";
        if (assistant.GetAnswer(key) is { } answer) return new("ready", answer.Text);
        assistant.RequestAnswer(key,
            $"I need {label} near {where}. Recommend the best 3 or 4 options from ALL the places in the facts (Calling Bell, Google Maps and " +
            "OpenStreetMap), naming each one and saying briefly why (rating, reviews, distance, price) and whether it can be booked on Calling Bell. " +
            "End with one short tip on choosing, using the typical prices if given.", facts.ToString());
        return new(assistant.IsAnswerPending(key) ? "pending" : "off", null);
    }

    /// <summary>OpenStreetMap places near the area, widening to the whole city when the area has few. Null when OpenStreetMap is unreachable.</summary>
    /// <remarks>
    /// When the area search alone is cached it answers at once. Otherwise both searches start together, so a sparse area costs one wait
    /// instead of two in a row; the city-wide result (also cached) is only used when the area has too few places.
    /// </remarks>
    private async Task<IReadOnlyList<ExternalPlace>?> SearchOsmAsync(List<string> selectors, double lat, double lng, CancellationToken ct)
    {
        var area = osm.SearchAsync(selectors, lat, lng, RadiusM, ct);
        if (area.IsCompletedSuccessfully && area.Result is { Count: >= MinNear } quick) return quick;
        var city = osm.SearchAsync(selectors, lat, lng, CityRadiusM, ct);
        var places = await area;
        return places is { Count: < MinNear } ? await city ?? places : places;
    }

    /// <summary>Whether the full OpenStreetMap search (area or city-wide) is still finishing in the background.</summary>
    private bool OsmSearching(List<string> selectors, double lat, double lng) =>
        osm.IsSearching(selectors, lat, lng, RadiusM) || osm.IsSearching(selectors, lat, lng, CityRadiusM);

    /// <summary>
    /// Orders places by the AI's pick when it is ready (asking for it otherwise): its picks first, then the other places nearest first. Only
    /// when the search fell back to matching words in names (<paramref name="nameOnly"/>) are places it didn't pick left out.
    /// </summary>
    private (List<ExternalPlace> Places, string AiStatus, IReadOnlyDictionary<string, string>? Reasons) Rank(List<ExternalPlace> places, string label,
        VisitorOrigin origin, double lat, double lng, bool nameOnly)
    {
        if (!assistant.IsEnabled || places.Count < 2) return (places, "off", null);
        // Only the nearest are judged: a shorter prompt answers in a fraction of the time on a CPU-only model.
        var candidates = places.Take(MaxRanked).ToList();
        var key = $"rank2|{string.Join(' ', Words(label))}|{origin.CitySlug}|{origin.PlaceKey}|{string.Join(',', candidates.Select(p => p.Id)).GetHashCode():x}";
        var ranking = assistant.GetRanking(key);
        if (ranking is null)
        {
            assistant.RequestRanking(key, label, Where(origin),
                candidates.Select(p => new AiPlaceCandidate(p.Id, p.Name, p.Kind, Details(p, lat, lng))).ToList());
            return (places, assistant.IsRankingPending(key) ? "pending" : "off", null);
        }
        var byId = candidates.ToDictionary(p => p.Id);
        var picked = ranking.Relevant.Where(byId.ContainsKey).Distinct().Select(id => byId[id]).ToList();
        IEnumerable<ExternalPlace> rest = nameOnly ? [] : places.Where(p => !ranking.Relevant.Contains(p.Id));
        return ([.. picked, .. rest], "ranked", ranking.Reasons);
    }

    /// <summary>What the AI is told about a place besides its name and type, so it can say why it fits.</summary>
    private static string Details(ExternalPlace p, double lat, double lng)
    {
        var parts = new List<string> { string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Km(p, lat, lng):0.0} km away") };
        if (p.Phone is not null) parts.Add("phone listed");
        if (p.Website is not null) parts.Add("has a website");
        if (p.OpeningHours is not null) parts.Add($"hours {p.OpeningHours}");
        if (p.Address is not null) parts.Add($"address {p.Address}");
        return string.Join(", ", parts);
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
            credit?.DisplayName, credit?.Uri, Latitude: Math.Round(p.Latitude, 6), Longitude: Math.Round(p.Longitude, 6),
            OpenNow: p.OpenNow);
    }

    private sealed record Sub(string Slug, string Name, string CategorySlug, string CategoryName, string? OsmTags);

    private sealed record Known(string Name, string? Phone, double Lat, double Lng);
}
