using System.Collections.Concurrent;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Common;

/// <summary>A listed place: a state / province / region, city or area.</summary>
public sealed record GeoPlaceRef(Guid Id, string Slug, string Name);

/// <summary>
/// What a coordinate falls in. <paramref name="Area"/> is the top-level area (what the location picker lists); when the nearest place was
/// one of its sub-localities, <paramref name="Locality"/> names it ("Hydernagar" in Kukatpally). <paramref name="MatchedBy"/> is "area"
/// (an area within the area radius; the city is the area's) or "city" (no area that close; the nearest city centre within the city radius).
/// </summary>
public sealed record ReverseGeocodeResult(string CountryCode, GeoPlaceRef? Region, GeoPlaceRef City, double CityKm, GeoPlaceRef? Area,
    double? AreaKm, string? Locality, string MatchedBy);

/// <summary>
/// Reverse geocoding against the places listed in SQL Server: a latitude/longitude (e.g. ip-api.com's for the visitor's IP) to region,
/// city and area, as our own records (ids and slugs), with no call to an outside service.
/// <para>
/// Performance: each country's cities and areas are loaded once (two projected, indexed queries) into spatial grids held in memory
/// (<see cref="ReferenceDataCache"/>, dropped whenever cities or areas change). A lookup reads only the grid cells within the search
/// radius (microseconds), and answers are remembered per ~100 m cell on the index itself, so they can never outlive the data they
/// came from.
/// </para>
/// </summary>
public sealed class ReverseGeocoder(IUnitOfWork uow, ReferenceDataCache reference)
{
    /// <summary>An area counts when its point is within this distance; IP coordinates are approximate, so it is kept small.</summary>
    public const double DefaultAreaKm = 3;
    /// <summary>Answers remembered per index (one per country); the oldest are dropped beyond this.</summary>
    private const int MaxRemembered = 20_000;

    public async Task<ReverseGeocodeResult?> ReverseAsync(double lat, double lon, string countryCode, double cityRadiusKm,
        CancellationToken ct, double areaRadiusKm = DefaultAreaKm)
    {
        if (!double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;
        var country = countryCode.Trim().ToUpperInvariant();
        if (country.Length != 2) return null;
        var index = await reference.GetDerivedAsync($"reverse-geocode|{country}", () => LoadAsync(country, ct), ct);

        // ~100 m cells: IP coordinates are rounded to that or coarser, so nearby visitors share an answer.
        var key = (Math.Round(lat, 3), Math.Round(lon, 3), cityRadiusKm, areaRadiusKm);
        if (index.Answers.TryGetValue(key, out var known)) return known;
        var answer = index.Find(lat, lon, cityRadiusKm, areaRadiusKm);
        if (index.Answers.Count < MaxRemembered) index.Answers.TryAdd(key, answer);
        return answer;
    }

    private async Task<CountryIndex> LoadAsync(string country, CancellationToken ct)
    {
        var cities = await uow.Repository<City>().QueryNoTracking()
            .Where(c => c.IsActive && c.State.CountryCode == country && c.Latitude != null && c.Longitude != null)
            .Select(c => new { c.Id, c.Slug, c.Name, c.Latitude, c.Longitude, StateId = c.State.Id, StateSlug = c.State.Slug, StateName = c.State.Name })
            .ToListAsync(ct);
        var areas = await uow.Repository<Area>().QueryNoTracking()
            .Where(a => a.IsActive && a.City.IsActive && a.City.State.CountryCode == country && a.Latitude != null && a.Longitude != null)
            .Select(a => new
            {
                a.Id, a.Slug, a.Name, a.CityId, a.Latitude, a.Longitude,
                // A sub-locality points at the top-level area it belongs to.
                TopId = a.ParentArea != null ? a.ParentArea.Id : a.Id,
                TopSlug = a.ParentArea != null ? a.ParentArea.Slug : a.Slug,
                TopName = a.ParentArea != null ? a.ParentArea.Name : a.Name,
                IsSub = a.ParentAreaId != null,
            })
            .ToListAsync(ct);

        var cityPoints = cities.ToDictionary(c => c.Id, c => new CityPoint(new GeoPlaceRef(c.Id, c.Slug, c.Name),
            new GeoPlaceRef(c.StateId, c.StateSlug, c.StateName), (double)c.Latitude!.Value, (double)c.Longitude!.Value));
        var cityGrid = new GeoGrid<CityPoint>(cityPoints.Values.Select(c => (c.Lat, c.Lon, c)), cellDegrees: 0.5);
        var areaGrid = new GeoGrid<AreaPoint>(areas
            .Where(a => cityPoints.ContainsKey(a.CityId))
            .Select(a => ((double)a.Latitude!.Value, (double)a.Longitude!.Value,
                new AreaPoint(new GeoPlaceRef(a.TopId, a.TopSlug, a.TopName), a.IsSub ? a.Name : null, cityPoints[a.CityId]))), cellDegrees: 0.05);
        return new CountryIndex(country, cityGrid, areaGrid);
    }

    private sealed record CityPoint(GeoPlaceRef City, GeoPlaceRef Region, double Lat, double Lon);

    private sealed record AreaPoint(GeoPlaceRef Area, string? Locality, CityPoint City);

    private sealed class CountryIndex(string country, GeoGrid<CityPoint> cities, GeoGrid<AreaPoint> areas)
    {
        public ConcurrentDictionary<(double, double, double, double), ReverseGeocodeResult?> Answers { get; } = new();

        public ReverseGeocodeResult? Find(double lat, double lon, double cityRadiusKm, double areaRadiusKm)
        {
            // 1. The nearest listed area (or sub-locality) close by: its top-level area, city and region.
            if (areas.Nearest(lat, lon, areaRadiusKm) is { } a)
            {
                var c = a.Item.City;
                return new ReverseGeocodeResult(country, c.Region, c.City, Math.Round(GeoMath.HaversineKm(lat, lon, c.Lat, c.Lon), 1),
                    a.Item.Area, Math.Round(a.Km, 2), a.Item.Locality, "area");
            }
            // 2. Otherwise the nearest city centre within the district radius (suburbs, villages, cities whose areas aren't listed yet).
            if (cities.Nearest(lat, lon, cityRadiusKm) is { } nearest)
                return new ReverseGeocodeResult(country, nearest.Item.Region, nearest.Item.City, Math.Round(nearest.Km, 1), null, null, null, "city");
            return null;
        }
    }
}
