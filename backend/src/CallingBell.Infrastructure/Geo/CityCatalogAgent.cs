using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

public sealed class CityCatalogOptions
{
    public const string Section = "CityCatalog";
    public bool Enabled { get; set; } = true;
    /// <summary>GeoNames free data dumps (CC BY 4.0, no key needed).</summary>
    public string DumpBaseUrl { get; set; } = "https://download.geonames.org/export/dump/";
    /// <summary>Which GeoNames cities file: cities15000 (population ≥ 15,000), cities5000, cities1000 or cities500.</summary>
    public string CitiesFile { get; set; } = "cities15000";
    /// <summary>Cities below this population are skipped (on top of the file's own threshold).</summary>
    public int MinPopulation { get; set; } = 15000;
    /// <summary>Where downloaded files are kept, relative to the API's content root.</summary>
    public string DataDirectory { get; set; } = "App_Data/geonames";
    /// <summary>Downloaded files and imported countries are refreshed after this many days.</summary>
    public int RefreshDays { get; set; } = 30;
    /// <summary>A place this close to a curated city is the same city (or one of its areas) and is not added separately.</summary>
    public double CuratedMatchKm { get; set; } = 10;
    /// <summary>A place this close to a larger imported place is treated as part of it.</summary>
    public double ImportedMatchKm { get; set; } = 3;
    /// <summary>
    /// A place within this distance of a curated city that has the name of one of that city's areas (e.g. Kukatpally, Hyderabad) is part of
    /// the city, not a separate one. Metros spread further than <see cref="CuratedMatchKm"/>; the name check keeps separate towns separate.
    /// </summary>
    public double AreaNameMatchKm { get; set; } = 25;
    public string UserAgent { get; set; } = "CallingBell-city-catalog/1.0 (+https://callingbell.in)";
}

/// <summary>Queue of countries waiting for import; one run per country at a time.</summary>
internal sealed class CityCatalogQueue(IOptions<CityCatalogOptions> options) : ICountryCatalogService
{
    private readonly Channel<string> _countries = Channel.CreateBounded<string>(new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);

    internal ChannelReader<string> Reader => _countries.Reader;

    public void Request(string countryCode)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        if (!options.Value.Enabled || code.Length != 2 || !code.All(char.IsAsciiLetterUpper)) return;
        if (_pending.TryAdd(code, 0) && !_countries.Writer.TryWrite(code)) _pending.TryRemove(code, out _);
    }

    public bool IsRunning(string countryCode) => _pending.ContainsKey(countryCode.Trim());

    internal void Done(string countryCode) => _pending.TryRemove(countryCode, out _);
}

internal sealed class CityCatalogWorker(CityCatalogQueue queue, IServiceScopeFactory scopes, ILogger<CityCatalogWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var country in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<CityCatalogRun>().ExecuteAsync(country, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "City catalogue import failed for {Country}", country);
                }
                finally { queue.Done(country); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}

/// <summary>
/// One import of a country's cities:
/// <list type="number">
/// <item>download the GeoNames cities, first-level divisions and country files (kept on disk and refreshed monthly);</item>
/// <item>add the country's states that aren't listed yet (matched by name or GeoNames code);</item>
/// <item>add its cities and towns, largest first: a place within <see cref="CityCatalogOptions.CuratedMatchKm"/> of a curated city is that
/// city (or one of its areas) and only links to it; one within <see cref="CityCatalogOptions.ImportedMatchKm"/> of a larger imported place
/// is part of it;</item>
/// <item>record the import in <see cref="CountryCatalog"/>.</item>
/// </list>
/// Re-running updates populations and links; it never removes cities, nor changes curated ones beyond their GeoNames link. Every fact comes
/// from GeoNames; nothing is generated. Areas of each city come later from the area-discovery agent, when the city is first chosen.
/// </summary>
internal sealed partial class CityCatalogRun(
    ApplicationDbContext db, IHttpClientFactory httpFactory, IOptions<CityCatalogOptions> options, IHostEnvironment env, IMemoryCache cache,
    IPublicCache publicCache, ILogger<CityCatalogRun> logger)
{
    public const string HttpClientName = "city-catalog";
    private const string Agent = "agent:city-catalog";
    private const int BatchSize = 500;
    /// <summary>GeoNames populated-place codes kept; sections (PPLX), localities (PPLL), abandoned/historical/destroyed places are left out.</summary>
    private static readonly HashSet<string> CityCodes = ["PPL", "PPLA", "PPLA2", "PPLA3", "PPLA4", "PPLA5", "PPLC", "PPLG", "PPLS"];

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    private sealed record GeoCity(long Id, string Name, string AsciiName, double Lat, double Lng, string Admin1, int Population);

    public async Task ExecuteAsync(string countryCode, CancellationToken ct)
    {
        var o = options.Value;
        var cc = countryCode.ToUpperInvariant();
        var started = DateTimeOffset.UtcNow;
        logger.LogInformation("City catalogue import started for {Country}", cc);

        var dir = Path.Combine(env.ContentRootPath, o.DataDirectory);
        Directory.CreateDirectory(dir);
        var countryFile = await EnsureFileAsync(dir, "countryInfo.txt", ct);
        var admin1File = await EnsureFileAsync(dir, "admin1CodesASCII.txt", ct);
        var citiesZip = await EnsureFileAsync(dir, o.CitiesFile + ".zip", ct);

        var catalog = await db.CountryCatalogs.FirstOrDefaultAsync(c => c.CountryCode == cc, ct);
        var countryName = (await File.ReadAllLinesAsync(countryFile, ct))
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t'))
            .FirstOrDefault(f => f.Length > 4 && f[0] == cc)?[4];
        if (countryName is null)
        {
            logger.LogWarning("City catalogue: {Country} is not a GeoNames country", cc);
            return;
        }
        if (catalog is null)
        {
            catalog = new CountryCatalog { CountryCode = cc, CountryName = countryName, CreatedBy = Agent, CreatedOn = started };
            db.CountryCatalogs.Add(catalog);
        }

        // 1. States (first-level divisions).
        var admin1 = (await File.ReadAllLinesAsync(admin1File, ct))
            .Select(l => l.Split('\t')).Where(f => f.Length >= 2 && f[0].StartsWith(cc + ".", StringComparison.Ordinal))
            .ToDictionary(f => f[0][(cc.Length + 1)..], f => f[1]);
        var geoCities = ReadCities(citiesZip, o.CitiesFile + ".txt", cc, o.MinPopulation);

        db.SuppressAuditLog = true;
        var states = await db.States.Where(s => s.CountryCode == cc).ToListAsync(ct);
        var stateSlugs = (await db.States.Select(s => s.Slug).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stateByCode = new Dictionary<string, State>();
        var statesAdded = 0;
        foreach (var code in geoCities.Select(c => c.Admin1).Distinct())
        {
            var name = admin1.GetValueOrDefault(code);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var reference = $"{cc}.{code}";
            var state = states.FirstOrDefault(s => s.ExternalRef == reference)
                        ?? states.FirstOrDefault(s => string.Equals(Normalise(s.Name), Normalise(name), StringComparison.Ordinal));
            if (state is null)
            {
                var slug = Unique(Slugify(name), stateSlugs, Slugify($"{name} {cc}"));
                state = new State
                {
                    Name = name, Code = (cc + code)[..Math.Min(10, cc.Length + code.Length)], Slug = slug, CountryCode = cc, ExternalRef = reference,
                    SortOrder = 1000, IsActive = true, CreatedBy = Agent,
                };
                db.States.Add(state);
                states.Add(state);
                statesAdded++;
            }
            else state.ExternalRef ??= reference;
            stateByCode[code] = state;
        }
        await db.SaveChangesAsync(ct);

        // 2. Cities, largest first, so a town next to a bigger place is folded into it.
        var existing = await db.Cities.Where(c => c.State.CountryCode == cc)
            .Select(c => new Known(c.Id, c.Source, c.ExternalRef, (double?)c.Latitude, (double?)c.Longitude, c.IsActive)).ToListAsync(ct);
        var byRef = existing.Where(e => e.ExternalRef != null).GroupBy(e => e.ExternalRef!).ToDictionary(g => g.Key, g => g.First().Id);
        // Curated cities count even when inactive ("launching soon"); retired imported ones don't.
        var placed = existing.Where(e => (e.IsActive || e.Source == null) && e.Lat != null && e.Lng != null).Select(e => (e.Id, Curated: e.Source == null, Lat: e.Lat!.Value, Lng: e.Lng!.Value)).ToList();
        var citySlugs = (await db.Cities.Select(c => c.Slug).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var maxOrder = await db.Cities.Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
        // Area names (and alternate spellings) of the curated cities, to recognise their areas listed by GeoNames as towns.
        var curatedAreas = (await db.Areas.Where(a => a.IsActive && a.City.Source == null && a.City.State.CountryCode == cc)
                .Select(a => new { a.CityId, a.Name, a.AltNames }).ToListAsync(ct))
            .SelectMany(a => (a.AltNames ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Append(a.Name).Select(n => (a.CityId, Name: Normalise(n))))
            .ToHashSet();
        var curatedCentres = placed.Where(p => p.Curated).ToList();
        bool IsCuratedArea(GeoCity g) => curatedCentres.Any(c =>
            GeoMath.HaversineKm(g.Lat, g.Lng, c.Lat, c.Lng) <= o.AreaNameMatchKm
            && (curatedAreas.Contains((c.Id, Normalise(g.Name))) || curatedAreas.Contains((c.Id, Normalise(g.AsciiName)))));
        var cityIdsWithBusinesses = (await db.Businesses.Where(b => b.CityId != null).Select(b => b.CityId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        int added = 0, linked = 0, updated = 0, merged = 0, retired = 0, rank = 0, pending = 0;

        foreach (var g in geoCities.OrderByDescending(c => c.Population))
        {
            rank++;
            if (!stateByCode.TryGetValue(g.Admin1, out var state)) continue;
            var reference = $"geonames:{g.Id}";
            var curatedArea = IsCuratedArea(g);
            if (byRef.TryGetValue(reference, out var knownId))
            {
                // Imported or linked before: keep the population current, and retire an imported "city" now known to be a curated city's area.
                var known = await db.Cities.FirstAsync(c => c.Id == knownId, ct);
                if (curatedArea && known is { Source: "geonames", IsActive: true } && !cityIdsWithBusinesses.Contains(known.Id))
                {
                    known.IsActive = false; known.ModifiedBy = Agent; retired++; pending++;
                    placed.RemoveAll(p => p.Id == known.Id);
                    continue;
                }
                if (known.Population != g.Population) { known.Population = g.Population; known.ModifiedBy = Agent; updated++; pending++; }
                continue;
            }
            if (curatedArea) { merged++; continue; }

            var near = placed
                .Select(p => (p.Id, p.Curated, Km: GeoMath.HaversineKm(g.Lat, g.Lng, p.Lat, p.Lng)))
                .Where(p => p.Km <= (p.Curated ? o.CuratedMatchKm : o.ImportedMatchKm))
                .OrderBy(p => p.Km).FirstOrDefault();
            if (near.Id != Guid.Empty)
            {
                if (near.Curated)
                {
                    // The curated city itself (or a place inside it): link the first, largest match.
                    var curated = await db.Cities.FirstAsync(c => c.Id == near.Id, ct);
                    if (curated.ExternalRef is null) { curated.ExternalRef = reference; curated.Population ??= g.Population; curated.ModifiedBy = Agent; linked++; pending++; }
                    else merged++;
                }
                else merged++;
                continue;
            }

            var displayName = g.Name.Trim();
            var slug = Unique(Slugify(g.AsciiName), citySlugs, Slugify($"{g.AsciiName} {state.Name}"), Slugify($"{g.AsciiName} {g.Id}"));
            var city = new City
            {
                StateId = state.Id, Name = displayName, Slug = slug, Latitude = Math.Round((decimal)g.Lat, 6), Longitude = Math.Round((decimal)g.Lng, 6),
                Population = g.Population, Source = "geonames", ExternalRef = reference, IsActive = true, IsPopular = false,
                SortOrder = maxOrder + rank, CreatedBy = Agent,
                AltNames = string.Equals(g.AsciiName, displayName, StringComparison.OrdinalIgnoreCase) ? null : g.AsciiName,
            };
            db.Cities.Add(city);
            placed.Add((city.Id, false, g.Lat, g.Lng));
            byRef[reference] = city.Id;
            added++;
            if (++pending >= BatchSize) { await db.SaveChangesAsync(ct); db.ChangeTracker.Clear(); pending = 0; catalog = await db.CountryCatalogs.FirstAsync(c => c.CountryCode == cc, ct); }
        }
        if (pending > 0) await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // 3. Record the import (and one audit entry for the whole run).
        catalog = await db.CountryCatalogs.FirstAsync(c => c.CountryCode == cc, ct);
        catalog.CountryName = countryName;
        catalog.Source = "geonames";
        catalog.StateCount = await db.States.CountAsync(s => s.CountryCode == cc && s.IsActive, ct);
        catalog.CityCount = await db.Cities.CountAsync(c => c.State.CountryCode == cc && c.IsActive, ct);
        catalog.ImportedOn = DateTimeOffset.UtcNow;
        catalog.ModifiedBy = Agent;
        catalog.ModifiedOn = catalog.ImportedOn;
        catalog.Note = Trim($"GeoNames {o.CitiesFile}: {geoCities.Count} places (population ≥ {o.MinPopulation:N0}); states +{statesAdded}; cities +{added}, " +
                            $"linked {linked} curated, {merged} folded into nearby cities, {retired} retired as areas of curated cities, updated {updated}.", 400);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = null, Action = "Imported", EntityName = nameof(City), EntityId = cc, Changes = catalog.Note, CreatedOn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        ReferenceDataCache.Invalidate(cache); // the cached city lists now include the imported cities
        // Also the cached responses: a visitor who asked during the import was served the country's empty city list.
        await publicCache.InvalidateAsync(ct);
        logger.LogInformation("City catalogue import for {Country}: {Note} ({Seconds:0}s)", cc, catalog.Note, (DateTimeOffset.UtcNow - started).TotalSeconds);
    }

    private sealed record Known(Guid Id, string? Source, string? ExternalRef, double? Lat, double? Lng, bool IsActive);

    /// <summary>The country's populated places from the GeoNames cities file (tab-separated, inside the zip).</summary>
    private static List<GeoCity> ReadCities(string zipPath, string entryName, string cc, int minPopulation)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"{entryName} not found in {Path.GetFileName(zipPath)}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var list = new List<GeoCity>();
        while (reader.ReadLine() is { } line)
        {
            // 0 id, 1 name, 2 asciiname, 3 alternatenames, 4 lat, 5 lng, 6 feature class, 7 feature code, 8 country, 10 admin1, 14 population
            var f = line.Split('\t');
            if (f.Length < 15 || f[8] != cc || f[6] != "P" || !CityCodes.Contains(f[7])) continue;
            if (!int.TryParse(f[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population) || population < minPopulation) continue;
            if (!double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            if (!long.TryParse(f[0], out var id) || string.IsNullOrWhiteSpace(f[1])) continue;
            list.Add(new GeoCity(id, f[1], string.IsNullOrWhiteSpace(f[2]) ? f[1] : f[2], lat, lng, f[10], population));
        }
        return list;
    }

    /// <summary>The file from the local data folder, downloading it first when missing or older than <see cref="CityCatalogOptions.RefreshDays"/>.</summary>
    private async Task<string> EnsureFileAsync(string dir, string name, CancellationToken ct)
    {
        var path = Path.Combine(dir, name);
        var info = new FileInfo(path);
        if (info.Exists && info.Length > 0 && info.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-options.Value.RefreshDays)) return path;

        var client = httpFactory.CreateClient(HttpClientName);
        if (!client.DefaultRequestHeaders.UserAgent.Any()) client.DefaultRequestHeaders.UserAgent.ParseAdd(options.Value.UserAgent);
        var temp = path + ".download";
        try
        {
            await using (var source = await client.GetStreamAsync(options.Value.DumpBaseUrl + name, ct))
            await using (var target = File.Create(temp))
                await source.CopyToAsync(target, ct);
            File.Move(temp, path, overwrite: true);
            logger.LogInformation("City catalogue: downloaded {File}", name);
        }
        catch (Exception ex) when (info.Exists && ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Keep using the older copy when GeoNames can't be reached.
            logger.LogWarning(ex, "City catalogue: could not refresh {File}; using the existing copy", name);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return path;
    }

    private static string Unique(string slug, HashSet<string> taken, params string[] fallbacks)
    {
        foreach (var s in fallbacks.Prepend(slug))
        {
            var candidate = s.Length > 120 ? s[..120].TrimEnd('-') : s;
            if (candidate.Length > 0 && taken.Add(candidate)) return candidate;
        }
        for (var i = 2; ; i++)
            if (taken.Add($"{slug}-{i}")) return $"{slug}-{i}";
    }

    /// <summary>Lower-case ASCII slug; accents are dropped ("Bhāgalpur" gives "bhagalpur").</summary>
    private static string Slugify(string text)
    {
        var plain = new string(text.Normalize(NormalizationForm.FormKD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        return NonSlug().Replace(plain.ToLowerInvariant(), "-").Trim('-');
    }

    private static string Normalise(string text) => Slugify(text).Replace("-", "");

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
