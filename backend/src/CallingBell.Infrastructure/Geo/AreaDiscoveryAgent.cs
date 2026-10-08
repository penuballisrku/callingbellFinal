using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Ai;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

public sealed class AreaDiscoveryOptions
{
    public const string Section = "AreaDiscovery";
    public bool Enabled { get; set; } = true;
    /// <summary>OpenStreetMap places are collected within this radius of the city centre.</summary>
    public int RadiusKm { get; set; } = 20;
    public string OverpassUrl { get; set; } = "https://overpass-api.de/api/interpreter";
    /// <summary>Further public Overpass servers, tried in turn when one is overloaded (they often answer 504 when busy).</summary>
    public string[] FallbackOverpassUrls { get; set; } =
        ["https://overpass.private.coffee/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter", "https://overpass.kumi.systems/api/interpreter"];
    /// <summary>After a failed run, requests for the city are ignored for this long so the free sources aren't hammered.</summary>
    public int RetryAfterMinutes { get; set; } = 15;
    /// <summary>India Post post-office directory (free, no key).</summary>
    public string IndiaPostUrl { get; set; } = "https://api.postalpincode.in/postoffice/";
    /// <summary>OpenStreetMap Nominatim reverse geocoding, used for PIN codes India Post can't confirm (max 1 request/second).</summary>
    public string NominatimReverseUrl { get; set; } = "https://nominatim.openstreetmap.org/reverse";
    /// <summary>Upper bound on Nominatim lookups per run, to respect its usage policy.</summary>
    public int MaxNominatimLookups { get; set; } = 150;
    /// <summary>Sub-localities attach to the nearest area within this distance; farther ones are skipped.</summary>
    public double SubLocalityMaxKm { get; set; } = 4;
    /// <summary>
    /// Use the local AI model to merge duplicates, fix spellings and drop non-localities. Off by default: it is the heaviest AI job
    /// (several minutes of full CPU per city on a small machine); rule-based filters still remove junctions and transit points.
    /// </summary>
    public bool UseAi { get; set; }
    public string UserAgent { get; set; } = "CallingBell-area-discovery/1.0 (+https://callingbell.in)";
    /// <summary>
    /// Ask the Wikidata Query Service (free, no key) first: a city's neighbourhoods in a few seconds, listed while OpenStreetMap - whose
    /// public servers are often busy for minutes - is searched.
    /// </summary>
    public bool UseWikidata { get; set; } = true;
    public string WikidataSparqlUrl { get; set; } = "https://query.wikidata.org/sparql";
    /// <summary>Overpass servers are asked one after another, the next one this many seconds later if none has answered yet.</summary>
    public int OverpassStaggerSeconds { get; set; } = 10;
    /// <summary>Give up on OpenStreetMap after this long (the Wikidata areas stay; it is tried again later).</summary>
    public int OverpassTimeoutSeconds { get; set; } = 100;
    /// <summary>Cities discovered at the same time, so one slow city doesn't hold up the others' Wikidata areas.</summary>
    public int Concurrency { get; set; } = 8;
    /// <summary>Of those, how many may query OpenStreetMap at once (its public servers are shared and often busy).</summary>
    public int OverpassConcurrency { get; set; } = 3;
}

/// <summary>Queue of cities waiting for discovery; one run per city at a time.</summary>
internal sealed class AreaDiscoveryQueue(IOptions<AreaDiscoveryOptions> options) : IAreaDiscoveryService
{
    private readonly Channel<Guid> _cities = Channel.CreateBounded<Guid>(new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _failedUntil = new();

    internal ChannelReader<Guid> Reader => _cities.Reader;

    public void Request(Guid cityId)
    {
        if (!options.Value.Enabled) return;
        // A run for this city failed recently (sources unreachable): wait before trying again.
        if (_failedUntil.TryGetValue(cityId, out var until) && until > DateTimeOffset.UtcNow) return;
        if (_pending.TryAdd(cityId, 0) && !_cities.Writer.TryWrite(cityId)) _pending.TryRemove(cityId, out _);
    }

    public bool IsRunning(Guid cityId) => _pending.ContainsKey(cityId);

    internal void Done(Guid cityId) => _pending.TryRemove(cityId, out _);

    internal void Failed(Guid cityId) => _failedUntil[cityId] = DateTimeOffset.UtcNow.AddMinutes(options.Value.RetryAfterMinutes);

    /// <summary>Limits concurrent OpenStreetMap queries across all runs (the quick Wikidata stage isn't limited by it).</summary>
    internal SemaphoreSlim OverpassSlots { get; } = new(Math.Max(1, options.Value.OverpassConcurrency));
}

internal sealed class AreaDiscoveryWorker(AreaDiscoveryQueue queue, IServiceScopeFactory scopes, IOptions<AreaDiscoveryOptions> options,
    ILogger<AreaDiscoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Up to Concurrency cities at once: each run waits mostly on remote services, and one slow city shouldn't hold up the next.
        using var slots = new SemaphoreSlim(Math.Max(1, options.Value.Concurrency));
        var running = new List<Task>();
        try
        {
            await foreach (var cityId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await slots.WaitAsync(stoppingToken);
                running.RemoveAll(t => t.IsCompleted);
                running.Add(Task.Run(async () =>
                {
                    try { await RunAsync(cityId, stoppingToken); }
                    finally { slots.Release(); }
                }, CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        await Task.WhenAll(running);
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is { } inner) ex = inner;
        return ex;
    }

    private async Task RunAsync(Guid cityId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AreaDiscoveryRun>().ExecuteAsync(cityId, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            queue.Failed(cityId);
            // The free map servers being busy or unreachable is expected now and then: one line, not a stack trace. The areas already
            // found (Wikidata) stay listed and the city is tried again later.
            if (ex is HttpRequestException or TaskCanceledException)
                logger.LogWarning("Area discovery for city {CityId} paused: {Reason}. Areas found so far stay listed; retrying in {Minutes} min",
                    cityId, Innermost(ex).Message, options.Value.RetryAfterMinutes);
            else
                logger.LogWarning(ex, "Area discovery failed for city {CityId}", cityId);
        }
        finally
        {
            queue.Done(cityId);
        }
    }
}

/// <summary>
/// One discovery run for a city:
/// <list type="number">
/// <item>list the city's neighbourhoods from Wikidata (seconds) straight away, then collect named places (suburb, quarter, locality, town,
/// village, neighbourhood) around the city centre from OpenStreetMap (often minutes, its public servers are busy) and merge the two;</item>
/// <item>normalise names and merge obvious duplicates; the local AI model then merges alternate spellings, fixes names and drops
/// entries that are not localities (it never supplies facts such as PIN codes or coordinates);</item>
/// <item>in India, give each area a PIN code from OSM, India Post or OSM reverse geocoding, only inside the city's postal zone; elsewhere
/// the area keeps the postcode OpenStreetMap tags it with, if any (no postal directory to verify it against);</item>
/// <item>upsert top-level areas and attach neighbourhoods to their nearest area as sub-localities;</item>
/// <item>deactivate previously discovered areas the sources no longer list (never curated areas, nor areas businesses use).</item>
/// </list>
/// </summary>
internal sealed class AreaDiscoveryRun(
    ApplicationDbContext db, IHttpClientFactory httpFactory, OllamaChatClient ai, IOptions<AreaDiscoveryOptions> options,
    IOptions<AiOptions> aiOptions, IMemoryCache cache, AreaDiscoveryQueue queue, ILogger<AreaDiscoveryRun> logger)
{
    public const string HttpClientName = "area-discovery";
    private const string Agent = "agent:area-discovery";
    private static readonly string[] MajorPlaces = ["suburb", "quarter", "locality", "town", "village"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Transit points and junctions that OpenStreetMap sometimes tags as places; they are not localities.</summary>
    private static readonly Regex NotALocality = new(
        @"\b(jn|junction|x[\s-]?roads?|cross[\s-]?roads?|crossroads|bus\s(stop|stand|depot|station)|metro(\sstation)?|railway\sstation|signal|flyover|toll\splaza)\b\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Road features named first in other languages ("Échangeur Anjou", "Courbe Senna", "Autobahnkreuz Köln-Süd"); not localities.</summary>
    private static readonly Regex RoadFeature = new(
        @"^(échangeur|echangeur|courbe|carrefour|rond-point|interchange|intercambiador|enlace|autobahnkreuz|autobahndreieck|kreuz|dreieck)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Names that look like a landmark or building rather than a locality; the AI may drop these whatever their OSM type.</summary>
    private static readonly Regex LooksLikeLandmark = new(
        @"\b(hospital|complex|buildings?|heights|quarters|industrial\s(area|estate)|flyover|tower|mall|campus|university|college|school|temple|masjid|church|hotel|apartments?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly Dictionary<string, bool> _pinValid = new();
    private DateTime _lastNominatim = DateTime.MinValue;

    private sealed class Candidate
    {
        public required string Ref { get; init; }
        public required string Name { get; set; }
        public required string Place { get; init; }
        public required double Lat { get; init; }
        public required double Lng { get; init; }
        public string? Postcode { get; init; }
        /// <summary>"osm" or "wikidata".</summary>
        public string Source { get; init; } = "osm";
        public HashSet<string> AltNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Slug => Slugify(Name);
    }

    public async Task ExecuteAsync(Guid cityId, CancellationToken ct)
    {
        var o = options.Value;
        var started = DateTimeOffset.UtcNow;
        var city = await db.Cities.Include(c => c.State).FirstAsync(c => c.Id == cityId, ct);
        if (city.Latitude is null || city.Longitude is null) { await NoteAsync(city, "City has no coordinates.", ct); return; }
        var (lat0, lng0) = ((double)city.Latitude, (double)city.Longitude);
        logger.LogInformation("Area discovery started for {City}", city.Name);

        // 1. Wikidata first: a few seconds, so the city's areas are listed while OpenStreetMap is searched. Each answer is listed as
        //    soon as it arrives (the places recorded in the city first, then - when those are few - the slower search around it).
        var wiki = !o.UseWikidata ? [] : await WikidataAsync(city, lat0, lng0, o.RadiusKm, async found =>
        {
            var quick = await MergeAsync(city, found, started, retire: false, useAi: false, ct);
            city.AreaDiscoveryNote = Trim($"{found.Count} Wikidata places; {quick}; searching OpenStreetMap.", 400);
            await db.SaveChangesAsync(ct);
            ReferenceDataCache.Invalidate(cache);
            logger.LogInformation("Area discovery for {City}: {Note}", city.Name, city.AreaDiscoveryNote);
        }, ct);

        // 2. OpenStreetMap, merged with the Wikidata places. If its servers are unavailable the Wikidata areas stay listed and the run
        //    fails, so it is tried again later.
        List<Candidate> osm;
        try
        {
            await queue.OverpassSlots.WaitAsync(ct);
            try { osm = await OverpassAsync(lat0, lng0, o.RadiusKm * 1000, ct); }
            finally { queue.OverpassSlots.Release(); }
        }
        catch (Exception ex) when (wiki.Count > 0 && (ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            city.AreaDiscoveryNote = Trim($"{wiki.Count} Wikidata places listed; OpenStreetMap unavailable ({ex.Message}), retrying later.", 400);
            await db.SaveChangesAsync(ct);
            throw;
        }
        var summary = await MergeAsync(city, [.. wiki, .. osm], started, retire: true, useAi: o.UseAi, ct);
        city.AreasDiscoveredOn = DateTimeOffset.UtcNow;
        city.AreaDiscoveryNote = Trim($"{osm.Count} OSM + {wiki.Count} Wikidata places; {summary}.", 400);
        await db.SaveChangesAsync(ct);
        ReferenceDataCache.Invalidate(cache); // curated cities carry their areas in the cached city lists
        logger.LogInformation("Area discovery for {City}: {Note}", city.Name, city.AreaDiscoveryNote);
    }

    private static bool Discovered(string? source) => source is "osm" or "wikidata";

    /// <summary>
    /// Upserts the places as the city's areas (top-level areas, then neighbourhoods and unverified places as sub-localities of their
    /// nearest area). <paramref name="retire"/>: deactivate discovered areas these places no longer include (only with the full list).
    /// </summary>
    private async Task<string> MergeAsync(City city, List<Candidate> places, DateTimeOffset started, bool retire, bool useAi, CancellationToken ct)
    {
        var o = options.Value;
        var cityId = city.Id;
        // India Post verifies PIN codes; other countries have no such directory, so their areas are listed without that check.
        var india = string.Equals(city.State.CountryCode, "IN", StringComparison.OrdinalIgnoreCase);
        var countryName = CountryName(city.State.CountryCode);

        var existing = await db.Areas.Where(a => a.CityId == cityId).ToListAsync(ct);
        var usedByBusinesses = (await db.Businesses.Where(b => b.CityId == cityId && b.AreaId != null).Select(b => b.AreaId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var zones = india ? existing.Where(a => a.Source == null && a.Pincode.Length == 6).Select(a => a.Pincode[..3]).ToHashSet() : [];

        var majors = MergeNearDuplicates(places.Where(p => MajorPlaces.Contains(p.Place)).ToList());
        var neighbourhoods = places.Where(p => p.Place == "neighbourhood").ToList();

        // AI clean-up of the top-level list (optional; skipped when the model is unavailable).
        var aiNote = "AI clean-up skipped";
        if (useAi && aiOptions.Value.Enabled && majors.Count > 0)
        {
            try { aiNote = await AiCleanupAsync(city.Name, city.State.Name, countryName, majors, ct); }
            catch (AiUnavailableException) { aiNote = "AI clean-up unavailable"; }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AI clean-up unavailable for {City}; continuing without it", city.Name);
                aiNote = "AI clean-up unavailable";
            }
        }

        // 3. Upsert top-level areas.
        var bySlug = existing.GroupBy(a => a.Slug).ToDictionary(g => g.Key, g => g.First());
        var byRef = existing.Where(a => a.ExternalRef != null).GroupBy(a => a.ExternalRef!).ToDictionary(g => g.Key, g => g.First());
        int added = 0, updated = 0, noPin = 0, nominatimCalls = 0;
        // Areas whose PIN can't be verified are not listed with a PIN of their own; they become sub-localities of the nearest area.
        var unverified = new List<Candidate>();
        foreach (var c in majors)
        {
            var area = byRef.GetValueOrDefault(c.Ref) ?? bySlug.GetValueOrDefault(c.Slug)
                       ?? c.AltNames.Select(n => bySlug.GetValueOrDefault(Slugify(n))).FirstOrDefault(a => a is not null);
            var type = TypeFor(c.Place);
            if (india && area is not null && Discovered(area.Source) && area.ParentAreaId == null && !await PinExistsAsync(area.Pincode, city.State.Name, ct))
            {
                // A PIN stored by an earlier run that India Post doesn't recognise: fix it, or demote the area to a sub-locality.
                if (await ResolvePinAsync(c, city.State.Name, zones, () => nominatimCalls++ < o.MaxNominatimLookups, ct) is { } fixedPin)
                    area.Pincode = fixedPin;
                else { unverified.Add(c); noPin++; continue; }
            }
            if (area is not null)
            {
                // Curated areas keep their name, PIN and coordinates; only search aliases and links are added.
                area.AltNames = JoinAliases(area.AltNames, c.AltNames.Append(c.Name), area.Name);
                area.ExternalRef ??= c.Ref;
                area.AreaType ??= type;
                area.LastVerifiedOn = started;
                if (Discovered(area.Source))
                {
                    area.IsActive = true;
                    area.Latitude = (decimal)c.Lat;
                    area.Longitude = (decimal)c.Lng;
                    area.AreaType = type;
                    area.ParentAreaId = null;
                }
                updated++;
                continue;
            }
            if (bySlug.ContainsKey(c.Slug)) continue;

            var pin = india ? await ResolvePinAsync(c, city.State.Name, zones, () => nominatimCalls++ < o.MaxNominatimLookups, ct) : Postcode(c.Postcode);
            if (pin is null) { unverified.Add(c); noPin++; continue; }

            var created = new Area
            {
                CityId = cityId, Name = c.Name, Slug = c.Slug, Pincode = pin, Latitude = (decimal)c.Lat, Longitude = (decimal)c.Lng,
                AreaType = type, AltNames = JoinAliases(null, c.AltNames, c.Name), Source = c.Source, ExternalRef = c.Ref,
                LastVerifiedOn = started, CreatedBy = Agent,
            };
            db.Areas.Add(created);
            existing.Add(created);
            bySlug[created.Slug] = created;
            byRef[c.Ref] = created;
            added++;
        }

        // 4. Neighbourhoods (and areas without a verified PIN) become sub-localities of their nearest top-level area.
        var unverifiedRefs = unverified.Select(u => u.Ref).ToHashSet();
        var parents = existing.Where(a => a.IsActive && a.ParentAreaId == null && a.Latitude != null && a.Longitude != null
                                          && (a.ExternalRef == null || !unverifiedRefs.Contains(a.ExternalRef))).ToList();
        int subsAdded = 0, subsUpdated = 0;
        var seenUnderParent = new HashSet<string>();
        foreach (var n in neighbourhoods.Concat(unverified))
        {
            var nearest = parents
                .Select(p => new { p, Km = GeoMath.HaversineKm(n.Lat, n.Lng, (double)p.Latitude!.Value, (double)p.Longitude!.Value) })
                .MinBy(x => x.Km);
            if (nearest is null || nearest.Km > o.SubLocalityMaxKm) continue;
            if (Slugify(nearest.p.Name) == n.Slug || !seenUnderParent.Add($"{nearest.p.Id}|{n.Slug}")) continue;

            if (byRef.GetValueOrDefault(n.Ref) is { } sub)
            {
                if (!Discovered(sub.Source)) continue;
                sub.Name = n.Name; sub.ParentAreaId = nearest.p.Id; sub.Pincode = nearest.p.Pincode; sub.IsActive = true;
                sub.AreaType = n.Place == "neighbourhood" ? "Neighbourhood" : TypeFor(n.Place);
                sub.AltNames = JoinAliases(null, n.AltNames, n.Name); sub.LastVerifiedOn = started;
                subsUpdated++;
                continue;
            }
            var slug = bySlug.ContainsKey(n.Slug) ? $"{n.Slug}-{nearest.p.Slug}" : n.Slug;
            if (bySlug.ContainsKey(slug) || slug.Length > 140) continue;
            var created = new Area
            {
                CityId = cityId, Name = n.Name, Slug = slug, Pincode = nearest.p.Pincode, Latitude = (decimal)n.Lat, Longitude = (decimal)n.Lng,
                AreaType = n.Place == "neighbourhood" ? "Neighbourhood" : TypeFor(n.Place), ParentAreaId = nearest.p.Id,
                AltNames = JoinAliases(null, n.AltNames, n.Name),
                Source = n.Source, ExternalRef = n.Ref, LastVerifiedOn = started, CreatedBy = Agent,
            };
            db.Areas.Add(created);
            bySlug[slug] = created;
            byRef[n.Ref] = created;
            subsAdded++;
        }

        // 5. Previously discovered areas the sources no longer list are retired (never curated areas or areas businesses use).
        var retired = 0;
        if (retire)
        {
            foreach (var stale in existing.Where(a => Discovered(a.Source) && a.IsActive && (a.LastVerifiedOn ?? DateTimeOffset.MinValue) < started
                                                      && !usedByBusinesses.Contains(a.Id)))
            {
                stale.IsActive = false;
                retired++;
            }
        }

        return $"areas +{added} (updated {updated}, {noPin} without verified PIN listed as sub-localities); sub-localities +{subsAdded} " +
               $"(updated {subsUpdated}); retired {retired}; {aiNote}";
    }

    // ---------------- Sources ----------------

    private async Task<List<Candidate>> OverpassAsync(double lat, double lng, int radiusM, CancellationToken ct)
    {
        // A global bounding box is far cheaper for the public Overpass servers than "around" filters (which busy servers time out on);
        // places in the box's corners, outside the radius, are dropped below.
        var dLat = radiusM / 111_320.0;
        var dLng = dLat / Math.Cos(lat * Math.PI / 180);
        var bbox = string.Create(CultureInfo.InvariantCulture, $"{lat - dLat:0.#####},{lng - dLng:0.#####},{lat + dLat:0.#####},{lng + dLng:0.#####}");
        var query = $"[out:json][timeout:90][bbox:{bbox}];(" +
                    "node[\"place\"~\"^(suburb|quarter|neighbourhood|locality|town|village)$\"][\"name\"];" +
                    "way[\"place\"~\"^(suburb|quarter|neighbourhood|town|village)$\"][\"name\"];" +
                    "relation[\"place\"~\"^(suburb|quarter|neighbourhood|town|village)$\"][\"name\"];" +
                    ");out center tags;";
        // Public servers are often overloaded (504/429, a "runtime error" remark, or minutes without an answer). They are asked one after
        // another: the next as soon as one fails, or OverpassStaggerSeconds after the last one started; the first answer wins.
        var o = options.Value;
        // Each server is tried twice: busy servers often fail one request (a 504, or a connection dropped during the TLS handshake) and
        // answer the next.
        var servers = o.FallbackOverpassUrls.Prepend(o.OverpassUrl).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
        var urls = new Queue<string>(servers.Concat(servers));
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        race.CancelAfter(TimeSpan.FromSeconds(o.OverpassTimeoutSeconds));
        var asking = new List<Task<OverpassResult>>();
        OverpassResult? result = null;
        Exception? lastError = null;
        try
        {
            while (result is null && (asking.Count > 0 || urls.Count > 0))
            {
                if (urls.Count > 0 && (asking.Count == 0 || asking.All(t => !t.IsCompleted))) asking.Add(AskOverpassAsync(urls.Dequeue(), query, race.Token));
                var stagger = urls.Count > 0 ? Task.Delay(TimeSpan.FromSeconds(o.OverpassStaggerSeconds), race.Token) : Task.Delay(Timeout.Infinite, race.Token);
                var first = await Task.WhenAny([.. asking, stagger]);
                if (first == stagger)
                {
                    if (race.IsCancellationRequested) break;
                    continue; // nobody answered yet: ask the next server too
                }
                var done = (Task<OverpassResult>)first;
                asking.Remove(done);
                try { result = await done; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested) { lastError = ex; }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            lastError ??= new TaskCanceledException($"No Overpass server answered within {o.OverpassTimeoutSeconds}s.");
        }
        finally
        {
            race.Cancel(); // stop the requests still waiting
            foreach (var t in asking) _ = t.ContinueWith(x => _ = x.Exception, TaskScheduler.Default);
        }
        if (result is null) throw new HttpRequestException($"No Overpass server answered ({lastError?.Message ?? "no servers configured"}).", lastError);

        var list = new List<Candidate>();
        foreach (var e in result?.Elements ?? [])
        {
            if (e.Tags is null || !e.Tags.TryGetValue("place", out var place)) continue;
            var (elat, elng) = e.Lat is { } la && e.Lon is { } lo ? (la, lo) : e.Center is { } c ? (c.Lat, c.Lon) : (double.NaN, double.NaN);
            if (double.IsNaN(elat) || GeoMath.HaversineKm(lat, lng, elat, elng) * 1000 > radiusM) continue;
            var name = CleanName(Latin(e.Tags.GetValueOrDefault("name:en")) ?? Latin(e.Tags.GetValueOrDefault("name")));
            if (name is null || NotALocality.IsMatch(name) || RoadFeature.IsMatch(name)) continue;
            var cand = new Candidate
            {
                Ref = $"{e.Type}/{e.Id}", Name = name, Place = place, Lat = elat, Lng = elng,
                Postcode = e.Tags.GetValueOrDefault("addr:postcode") ?? e.Tags.GetValueOrDefault("postal_code"),
            };
            foreach (var key in new[] { "name", "name:en", "alt_name", "old_name", "official_name", "short_name" })
                foreach (var alt in (e.Tags.GetValueOrDefault(key) ?? "").Split(';'))
                    if (CleanName(Latin(alt)) is { } a && !a.Equals(name, StringComparison.OrdinalIgnoreCase)) cand.AltNames.Add(a);
            list.Add(cand);
        }
        return list;
    }

    private async Task<OverpassResult> AskOverpassAsync(string url, string query, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
        using var response = await Client().PostAsync(url, content, ct);
        if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) throw new HttpRequestException($"{url} answered {(int)response.StatusCode}");
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<OverpassResult>(Json, ct) ?? throw new HttpRequestException($"{url}: empty answer");
        if (answer.Remark?.Contains("error", StringComparison.OrdinalIgnoreCase) == true) throw new HttpRequestException($"{url}: {answer.Remark}");
        return answer;
    }

    /// <summary>Wikidata types of place listed as areas, and how each is treated (MajorPlaces are top-level areas).</summary>
    private static readonly Dictionary<string, string> WikidataTypes = new()
    {
        ["Q123705"] = "suburb",   // neighbourhood
        ["Q188509"] = "suburb",   // suburb
        ["Q2983893"] = "suburb",  // quarter
        ["Q4286337"] = "suburb",  // city district
        ["Q211690"] = "suburb",   // London borough
        ["Q3957"] = "town",
        ["Q532"] = "village",
        ["Q17343829"] = "locality", // unincorporated community (US)
        ["Q486972"] = "locality",   // human settlement
    };

    /// <summary>
    /// The city's neighbourhoods, districts and suburbs from Wikidata: those recorded as located in the city (matched by its GeoNames id),
    /// and - when that finds few - places of those types around the city centre. Empty when Wikidata is unavailable.
    /// </summary>
    private async Task<List<Candidate>> WikidataAsync(City city, double lat, double lng, int radiusKm, Func<List<Candidate>, Task> list,
        CancellationToken ct)
    {
        var types = string.Join(' ', WikidataTypes.Keys.Select(k => "wd:" + k));
        const string Select = "SELECT ?area ?areaLabel ?coord ?postal ?type WHERE {{ {0} ?area wdt:P31 ?type . VALUES ?type {{ {1} }} " +
                              "?area wdt:P625 ?coord . OPTIONAL {{ ?area wdt:P281 ?postal . }} FILTER NOT EXISTS {{ ?area wdt:P576 ?ended . }} " +
                              "SERVICE wikibase:label {{ bd:serviceParam wikibase:language \"en,mul,fr,es,de,pt,it,nl\". }} }}";
        var places = new Dictionary<string, Candidate>();
        try
        {
            if (city.ExternalRef is { } ext && ext.StartsWith("geonames:", StringComparison.Ordinal) && long.TryParse(ext[9..], out var geonamesId))
            {
                var inCity = $"?city wdt:P1566 \"{geonamesId}\" . {{ ?area wdt:P131 ?city . }} UNION {{ ?area wdt:P131/wdt:P131 ?city . }}";
                await SparqlAsync(string.Format(CultureInfo.InvariantCulture, Select, inCity, types), lat, lng, radiusKm, places, ct);
                if (places.Count > 0) await list(places.Values.ToList());
            }
            if (places.Count < 40)
            {
                var before = places.Count;
                var around = string.Create(CultureInfo.InvariantCulture,
                    $"SERVICE wikibase:around {{ ?area wdt:P625 ?c . bd:serviceParam wikibase:center \"Point({lng} {lat})\"^^geo:wktLiteral ; wikibase:radius \"{Math.Min(radiusKm, 12)}\" . }}");
                await SparqlAsync(string.Format(CultureInfo.InvariantCulture, Select, around, types), lat, lng, radiusKm, places, ct);
                if (places.Count > before) await list(places.Values.ToList());
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Wikidata unavailable for {City} ({Message}); continuing with OpenStreetMap", city.Name, ex.Message);
        }
        return places.Values.ToList();
    }

    private async Task SparqlAsync(string query, double lat, double lng, int radiusKm, Dictionary<string, Candidate> places, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.WikidataSparqlUrl + "?query=" + Uri.EscapeDataString(query));
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        using var response = await Client().SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SparqlResult>(Json, timeout.Token);
        foreach (var row in result?.Results?.Bindings ?? [])
        {
            var id = row.GetValueOrDefault("area")?.Value?.Split('/')[^1];
            var typeId = row.GetValueOrDefault("type")?.Value?.Split('/')[^1];
            if (id is null || typeId is null || !WikidataTypes.TryGetValue(typeId, out var place)) continue;
            var key = "wikidata/" + id;
            if (places.TryGetValue(key, out var seen))
            {
                // Several types: keep the most specific one (a neighbourhood that is also a human settlement is a neighbourhood).
                if (place == "suburb" && seen.Place != "suburb") places[key] = new Candidate
                    { Ref = key, Name = seen.Name, Place = place, Lat = seen.Lat, Lng = seen.Lng, Postcode = seen.Postcode, Source = "wikidata" };
                continue;
            }
            var label = row.GetValueOrDefault("areaLabel")?.Value;
            if (label is null || Regex.IsMatch(label, @"^Q\d+$")) continue; // no label in a Latin-script language
            var name = CleanName(Latin(label));
            // Heritage listings ("Bremond Block Historic District") are typed like neighbourhoods but aren't where people say they live.
            if (name is null || NotALocality.IsMatch(name) || RoadFeature.IsMatch(name) || name.Contains("Historic District", StringComparison.OrdinalIgnoreCase)) continue;
            // "Point(-79.38 43.65)"
            var point = row.GetValueOrDefault("coord")?.Value is { } wkt ? Regex.Match(wkt, @"Point\(([-\d.eE]+) ([-\d.eE]+)\)") : Match.Empty;
            if (!point.Success) continue;
            var (plng, plat) = (double.Parse(point.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(point.Groups[2].Value, CultureInfo.InvariantCulture));
            if (GeoMath.HaversineKm(lat, lng, plat, plng) > radiusKm) continue;
            places[key] = new Candidate
            {
                Ref = key, Name = name, Place = place, Lat = plat, Lng = plng, Postcode = row.GetValueOrDefault("postal")?.Value, Source = "wikidata",
            };
        }
    }

    private async Task<string?> IndiaPostPinAsync(string name, string state, HashSet<string> zones, CancellationToken ct)
    {
        try
        {
            var url = options.Value.IndiaPostUrl + Uri.EscapeDataString(name);
            var data = await Client().GetFromJsonAsync<List<IndiaPostResult>>(url, Json, ct);
            var key = name.ToLowerInvariant();
            return (data?.FirstOrDefault()?.PostOffice ?? [])
                .Where(p => string.Equals(p.State, state, StringComparison.OrdinalIgnoreCase) && ValidPin(p.Pincode, zones) is not null
                            && (p.Name ?? "").ToLowerInvariant() is var n && (n == key || n.StartsWith(key + " ", StringComparison.Ordinal)))
                .OrderBy(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => p.DeliveryStatus != "Delivery")
                .Select(p => p.Pincode)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<string?> NominatimPinAsync(double lat, double lng, HashSet<string> zones, CancellationToken ct)
    {
        var wait = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - _lastNominatim);
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct); // usage policy: at most 1 request per second
        _lastNominatim = DateTime.UtcNow;
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture, $"{options.Value.NominatimReverseUrl}?lat={lat}&lon={lng}&format=jsonv2&zoom=16&addressdetails=1");
            var r = await Client().GetFromJsonAsync<NominatimResult>(url, Json, ct);
            return ValidPin(r?.Address?.Postcode, zones);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// PIN for a new area: OSM tag, then an India Post office of the same name, then OSM reverse geocoding (budgeted). Every candidate
    /// must be in the city's postal zone and exist in India Post's directory for the city's state.
    /// </summary>
    private async Task<string?> ResolvePinAsync(Candidate c, string state, HashSet<string> zones, Func<bool> mayUseNominatim, CancellationToken ct)
    {
        if (ValidPin(c.Postcode, zones) is { } tagged && await PinExistsAsync(tagged, state, ct)) return tagged;
        if (await IndiaPostPinAsync(c.Name, state, zones, ct) is { } byName) return byName; // already an India Post office
        if (mayUseNominatim() && await NominatimPinAsync(c.Lat, c.Lng, zones, ct) is { } reverse && await PinExistsAsync(reverse, state, ct)) return reverse;
        return null;
    }

    /// <summary>True when India Post lists post offices for this PIN in the given state (cached per run).</summary>
    private async Task<bool> PinExistsAsync(string pin, string state, CancellationToken ct)
    {
        if (_pinValid.TryGetValue(pin, out var known)) return known;
        bool ok;
        try
        {
            var data = await Client().GetFromJsonAsync<List<IndiaPostResult>>("https://api.postalpincode.in/pincode/" + pin, Json, ct);
            ok = (data?.FirstOrDefault()?.PostOffice ?? []).Any(p => string.Equals(p.State, state, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return true; // directory unreachable: don't reject the PIN; the postal-zone check still applies
        }
        _pinValid[pin] = ok;
        return ok;
    }

    // ---------------- AI clean-up ----------------

    /// <summary>
    /// Asks the local model which entries to drop, which to merge and which spellings to fix. Only structural decisions are taken
    /// from the model, and renames must stay close to an existing spelling, so it cannot introduce a different place.
    /// </summary>
    private async Task<string> AiCleanupAsync(string cityName, string state, string country, List<Candidate> majors, CancellationToken ct)
    {
        int dropped = 0, merged = 0, renamed = 0;
        foreach (var batch in majors.ToList().Chunk(60))
        {
            var lines = string.Join('\n', batch.Select((c, i) =>
                $"{i + 1} | {c.Name} | {c.Place}{(c.AltNames.Count > 0 ? " | also: " + string.Join(", ", c.AltNames.Take(3)) : "")}"));
            var prompt =
                $"Places in {cityName}, {state}, {country} from OpenStreetMap (id | name | type | other names):\n{lines}\n\n" +
                "Clean this list of localities people use in addresses:\n" +
                "- drop: ids that are not residential or commercial localities (landmarks, campuses, single buildings, bus stops, junctions, water bodies), or are clearly misspelt duplicates you also merge;\n" +
                "- merge: groups of ids that are the same locality spelt differently, canonical id first;\n" +
                "- rename: ids whose name should use the common English spelling (keep it recognisably the same name); at most 20.\n" +
                "Only use the given ids. Reply as JSON: {\"drop\":[3],\"merge\":[[1,7]],\"rename\":[{\"id\":2,\"name\":\"...\"}]}";
            var content = await ai.ChatJsonAsync($"You know the cities of {country} and their localities well. Reply with JSON only.", prompt, ct, maxOutputTokens: 1600);
            if (content is null) continue;
            var reply = JsonSerializer.Deserialize<CleanupReply>(content, Json);
            Candidate? At(int id) => id >= 1 && id <= batch.Length ? batch[id - 1] : null;

            foreach (var group in reply?.Merge ?? [])
            {
                if (group.Count < 2 || At(group[0]) is not { } keep) continue;
                foreach (var dupId in group.Skip(1))
                {
                    if (At(dupId) is not { } dup || dup == keep || !majors.Contains(dup)) continue;
                    // Guardrails: the same locality must be close by and spelt alike (or a known alternate name).
                    if (GeoMath.HaversineKm(keep.Lat, keep.Lng, dup.Lat, dup.Lng) > 3) continue;
                    if (Similarity(keep.Name, dup.Name) < 0.5 && !keep.AltNames.Contains(dup.Name) && !dup.AltNames.Contains(keep.Name)) continue;
                    keep.AltNames.Add(dup.Name);
                    foreach (var a in dup.AltNames) keep.AltNames.Add(a);
                    majors.Remove(dup);
                    merged++;
                }
            }
            // Guardrail: OSM suburbs, towns and villages are well curated, so the model may only drop the least reliable place types
            // (locality, quarter) or names that look like a landmark or building. In testing it also discarded real localities.
            foreach (var id in reply?.Drop ?? [])
                if (At(id) is { } c && (c.Place is "locality" or "quarter" || LooksLikeLandmark.IsMatch(c.Name)) && majors.Remove(c)) dropped++;
            foreach (var r in reply?.Rename ?? [])
            {
                if (At(r.Id) is not { } c || CleanName(r.Name) is not { } name || name.Equals(c.Name, StringComparison.Ordinal)) continue;
                if (Similarity(name, c.Name) < 0.6) continue; // guard against the model substituting another place
                c.AltNames.Add(c.Name);
                c.Name = name;
                renamed++;
            }
        }
        return $"AI clean-up ({ai.Model}): dropped {dropped}, merged {merged}, renamed {renamed}";
    }

    // ---------------- Helpers ----------------

    private HttpClient Client()
    {
        var client = httpFactory.CreateClient(HttpClientName);
        if (!client.DefaultRequestHeaders.UserAgent.Any()) client.DefaultRequestHeaders.UserAgent.ParseAdd(options.Value.UserAgent);
        return client;
    }

    private async Task NoteAsync(City city, string note, CancellationToken ct)
    {
        city.AreaDiscoveryNote = note;
        city.AreasDiscoveredOn = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Same normalised name within 3 km: keep the higher-ranked place type and fold the other names in as aliases.</summary>
    private static List<Candidate> MergeNearDuplicates(List<Candidate> items)
    {
        var rank = new Dictionary<string, int> { ["suburb"] = 0, ["town"] = 1, ["quarter"] = 2, ["locality"] = 3, ["village"] = 4 };
        var kept = new List<Candidate>();
        foreach (var c in items.OrderBy(c => rank.GetValueOrDefault(c.Place, 9)))
        {
            var key = Key(c.Name);
            var twin = kept.FirstOrDefault(k => (Key(k.Name) == key || k.AltNames.Any(a => Key(a) == key))
                                               && GeoMath.HaversineKm(k.Lat, k.Lng, c.Lat, c.Lng) <= 3);
            if (twin is null) { kept.Add(c); continue; }
            foreach (var a in c.AltNames.Append(c.Name)) if (!a.Equals(twin.Name, StringComparison.OrdinalIgnoreCase)) twin.AltNames.Add(a);
        }
        return kept;
    }

    private static string TypeFor(string place) => place switch
    {
        "suburb" => "Area", "quarter" => "Locality", "locality" => "Locality", "town" => "Town", "village" => "Village", _ => "Area",
    };

    /// <summary>A postcode outside India as OpenStreetMap writes it ("H2T 1S4", "SW1A 1AA", "10001"), or "" when the place has none.</summary>
    private static string Postcode(string? raw)
    {
        var code = Regex.Replace((raw ?? "").Split(';')[0].Trim().ToUpperInvariant(), @"\s+", " ");
        return code.Length is > 0 and <= 10 && code.Any(char.IsLetterOrDigit) ? code : "";
    }

    private static string CountryName(string code)
    {
        try { return new RegionInfo(code).EnglishName; }
        catch (ArgumentException) { return code; }
    }

    private static string? ValidPin(string? raw, HashSet<string> zones)
    {
        var pin = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return pin.Length == 6 && pin[0] != '0' && (zones.Count == 0 || zones.Contains(pin[..3])) ? pin : null;
    }

    /// <summary>Only names written in Latin script (the site's language); others are ignored.</summary>
    private static string? Latin(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.Any(ch => char.IsLetter(ch) && ch > 'ɏ') ? null : s;

    private static string? CleanName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var name = Regex.Replace(s.Trim(), @"\s+", " ").Trim(' ', '-', ',', '.');
        if (name.Length is < 3 or > 60 || !name.Any(char.IsLetter)) return null;
        // Title-case names written entirely in upper or lower case ("MADHAPUR", "madhapur").
        if (name == name.ToUpperInvariant() || name == name.ToLowerInvariant())
            name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        return name;
    }

    private static string Key(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    internal static string Slugify(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static string? JoinAliases(string? current, IEnumerable<string> extra, string name)
    {
        var all = (current ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(extra)
            .Where(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var joined = string.Join('|', all);
        while (joined.Length > 600 && all.Count > 0) { all.RemoveAt(all.Count - 1); joined = string.Join('|', all); }
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>1 - normalised Levenshtein distance of the lower-cased letters.</summary>
    private static double Similarity(string a, string b)
    {
        string x = Key(a), y = Key(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        var d = new int[x.Length + 1, y.Length + 1];
        for (var i = 0; i <= x.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= y.Length; j++) d[0, j] = j;
        for (var i = 1; i <= x.Length; i++)
            for (var j = 1; j <= y.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
        return 1 - (double)d[x.Length, y.Length] / Math.Max(x.Length, y.Length);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private sealed record OverpassResult(List<OverpassElement>? Elements, string? Remark);
    private sealed record SparqlResult(SparqlResults? Results);
    private sealed record SparqlResults(List<Dictionary<string, SparqlValue>>? Bindings);
    private sealed record SparqlValue(string? Value);
    private sealed record OverpassElement(string Type, long Id, double? Lat, double? Lon, OverpassCenter? Center, Dictionary<string, string>? Tags);
    private sealed record OverpassCenter(double Lat, double Lon);
    private sealed record IndiaPostResult(string? Status, List<IndiaPostOffice>? PostOffice);
    private sealed record IndiaPostOffice(string? Name, string? State, string? Pincode, string? DeliveryStatus);
    private sealed record NominatimResult(NominatimAddress? Address);
    private sealed record NominatimAddress(string? Postcode);
    private sealed record CleanupReply(List<int>? Drop, List<List<int>>? Merge, List<RenameItem>? Rename);
    private sealed record RenameItem([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("name")] string? Name);
}
