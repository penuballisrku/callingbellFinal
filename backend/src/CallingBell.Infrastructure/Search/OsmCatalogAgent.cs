using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Search;

public sealed class OsmCatalogOptions
{
    public const string Section = "OsmCatalog";
    public bool Enabled { get; set; } = true;
    /// <summary>A city's places for a sub-category are fetched again after this many days.</summary>
    public int RefreshDays { get; set; } = 30;
    /// <summary>
    /// Pause between two Overpass queries. Overpass is a free shared service (fair use: well under 10,000 queries a day, one at a time),
    /// so the agent stays slow on purpose: at 20 s a city with ~100 sub-categories takes about half an hour.
    /// </summary>
    public int DelaySeconds { get; set; } = 20;
    /// <summary>After a failed query (servers busy), wait this long before the next one.</summary>
    public int RetryAfterMinutes { get; set; } = 10;
    /// <summary>How far around the city centre places are fetched.</summary>
    public int RadiusKm { get; set; } = 25;
    public int MaxPlacesPerQuery { get; set; } = 2000;
    /// <summary>Most places taken from Geoapify per city and sub-category (one credit per 20 places).</summary>
    public int MaxGeoapifyPlaces { get; set; } = 500;
    /// <summary>Server-side time limit of one city-wide query (a live search allows far less).</summary>
    public int QueryTimeoutSeconds { get; set; } = 180;
}

/// <summary>
/// Stores OpenStreetMap places per city and sub-category (<c>dbo.OsmPlaces</c>), using each sub-category's OpenStreetMap tags, so the
/// "AI recommended" search reads them from the database instead of waiting on Overpass. Cities covered: curated and popular cities and
/// any city with listed businesses. It works through the (city, sub-category) pairs never imported first, then the oldest, one query at
/// a time. Places keep their OpenStreetMap id, so a refresh updates them in place. Data © OpenStreetMap contributors (ODbL).
/// </summary>
internal sealed class OsmCatalogWorker(IServiceScopeFactory scopes, IOptions<OsmCatalogOptions> options, ILogger<OsmCatalogWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled) return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = TimeSpan.FromSeconds(o.DelaySeconds);
                try
                {
                    using var scope = scopes.CreateScope();
                    var outcome = await scope.ServiceProvider.GetRequiredService<OsmCatalogRun>().ImportNextAsync(stoppingToken);
                    wait = outcome switch
                    {
                        OsmImportOutcome.Nothing => TimeSpan.FromHours(1),
                        OsmImportOutcome.Failed => TimeSpan.FromMinutes(o.RetryAfterMinutes),
                        _ => wait,
                    };
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "OpenStreetMap catalogue: import failed");
                    wait = TimeSpan.FromMinutes(o.RetryAfterMinutes);
                }
                await Task.Delay(wait, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}

internal enum OsmImportOutcome { Imported, Nothing, Failed }

internal sealed class OsmCatalogRun(ApplicationDbContext db, IOsmPlaceSearch osm, IGeoapify geoapify, IOptions<OsmCatalogOptions> options,
    ILogger<OsmCatalogRun> logger)
{
    private const string Agent = "agent:osm-catalog";

    /// <summary>Imports the next (city, sub-category) pair that is due: never imported first, then the oldest.</summary>
    public async Task<OsmImportOutcome> ImportNextAsync(CancellationToken ct)
    {
        var o = options.Value;
        var due = DateTimeOffset.UtcNow.AddDays(-o.RefreshDays);
        var cities = db.Cities.Where(c => c.IsActive && c.Latitude != null && c.Longitude != null
                                          && (c.Source == null || c.IsPopular || db.Businesses.Any(b => b.CityId == c.Id)));
        var subs = db.SubCategories.Where(s => s.IsActive && s.Category.IsActive && s.OsmTags != null && s.OsmTags != "");
        var next = await (
                from c in cities
                from s in subs
                join i in db.OsmCityImports on new { CityId = c.Id, SubCategoryId = s.Id } equals new { i.CityId, i.SubCategoryId } into done
                from i in done.DefaultIfEmpty()
                where i == null || i.ImportedOn < due
                orderby i == null ? 0 : 1, i.ImportedOn, c.Source == null ? 0 : 1, c.SortOrder, s.SortOrder, c.Id, s.Id
                select new { CityId = c.Id, CityName = c.Name, Lat = (double)c.Latitude!, Lng = (double)c.Longitude!, SubId = s.Id, SubName = s.Name, s.OsmTags,
                    s.GeoapifyCategories })
            .FirstOrDefaultAsync(ct);
        if (next is null) return OsmImportOutcome.Nothing;

        var selectors = next.OsmTags!.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Geoapify (fast, reliable) when the sub-category maps to its categories and the day's background credits allow; else Overpass.
        var places = !string.IsNullOrWhiteSpace(next.GeoapifyCategories) && geoapify.IsEnabled
            ? await geoapify.PlacesAsync(next.GeoapifyCategories, next.Lat, next.Lng, o.RadiusKm * 1000, o.MaxGeoapifyPlaces, background: true, ct)
            : null;
        places ??= await osm.FetchAsync(selectors, next.Lat, next.Lng, o.RadiusKm * 1000, o.MaxPlacesPerQuery, o.QueryTimeoutSeconds, ct);
        if (places is null)
        {
            await RecordAsync(next.CityId, next.SubId, null, "Overpass could not be reached", ct);
            return OsmImportOutcome.Failed;
        }

        var count = await StoreAsync(places, next.CityId, next.SubId, ct);
        await RecordAsync(next.CityId, next.SubId, count, null, ct);
        logger.LogInformation("OpenStreetMap catalogue: {Count} {Sub} in {City}", count, next.SubName, next.CityName);
        return OsmImportOutcome.Imported;
    }

    /// <summary>Adds new places, updates known ones (by OpenStreetMap id) and links them to the sub-category.</summary>
    private async Task<int> StoreAsync(IReadOnlyList<ExternalPlace> places, Guid cityId, Guid subId, CancellationToken ct)
    {
        db.SuppressAuditLog = true;
        var now = DateTimeOffset.UtcNow;
        var unique = places.Where(p => p.Source == "osm" && !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Id).Select(g => g.First()).ToList();
        foreach (var batch in unique.Chunk(500))
        {
            var refs = batch.Select(p => p.Id).ToList();
            var known = await db.OsmPlaces.IgnoreQueryFilters().Include(p => p.SubCategories.Where(s => s.SubCategoryId == subId))
                .Where(p => refs.Contains(p.OsmRef)).ToDictionaryAsync(p => p.OsmRef, ct);
            foreach (var p in batch)
            {
                if (!known.TryGetValue(p.Id, out var place))
                {
                    place = new OsmPlace { OsmRef = p.Id, CityId = cityId, CreatedBy = Agent };
                    db.OsmPlaces.Add(place);
                }
                else place.ModifiedBy = Agent;
                place.Name = Clip(p.Name, 200)!;
                place.Kind = Clip(p.Kind, 80);
                place.Address = Clip(p.Address, 400);
                place.Phone = Clip(p.Phone, 60);
                place.Website = Clip(p.Website, 400);
                place.OpeningHours = Clip(p.OpeningHours, 400);
                place.Email = Clip(p.Email, 200);
                place.Description = Clip(p.Description, 1000);
                place.Features = p.Features is { Count: > 0 } f ? Clip(string.Join('|', f), 1000) : null;
                place.Latitude = Math.Round((decimal)p.Latitude, 6);
                place.Longitude = Math.Round((decimal)p.Longitude, 6);
                place.LastSeenOn = now;
                place.IsDeleted = false;
                if (place.SubCategories.All(s => s.SubCategoryId != subId))
                    place.SubCategories.Add(new OsmPlaceSubCategory { OsmPlace = place, SubCategoryId = subId });
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return unique.Count;
    }

    private async Task RecordAsync(Guid cityId, Guid subId, int? count, string? note, CancellationToken ct)
    {
        var row = await db.OsmCityImports.FirstOrDefaultAsync(i => i.CityId == cityId && i.SubCategoryId == subId, ct);
        if (row is null) db.OsmCityImports.Add(row = new OsmCityImport { CityId = cityId, SubCategoryId = subId });
        // A failed attempt is recorded with its error, dated so the pair is due again a day later; meanwhile the agent moves on, and
        // searches don't trust a pair with an error (they ask OpenStreetMap directly).
        row.ImportedOn = count is null ? DateTimeOffset.UtcNow.AddDays(1 - options.Value.RefreshDays) : DateTimeOffset.UtcNow;
        if (count is { } n) row.PlaceCount = n;
        row.Note = note;
        await db.SaveChangesAsync(ct);
    }

    private static string? Clip(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];
}
