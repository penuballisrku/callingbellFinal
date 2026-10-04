using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Geo;

/// <summary>Where the visitor is browsing from, resolved to a listed city.</summary>
/// <param name="PlaceName">Area or locality name for headings (e.g. "Kukatpally"), or the city name.</param>
/// <param name="Source">"area" (selected area), "ip" (visitor IP coordinates) or "city" (city centre).</param>
/// <param name="PlaceKey">Stable key for caching per place (area slug, rounded coordinates, or "centre").</param>
public sealed record VisitorOrigin(Guid CityId, string CitySlug, string CityName, string State, string PlaceName, double? Lat, double? Lng,
    string Source, string PlaceKey);

/// <summary>
/// Resolves the visitor's origin: an explicitly selected area first, then the IP's coordinates when they fall near a listed city
/// (and that is the city being browsed), then the centre of the city being browsed.
/// </summary>
public sealed class VisitorOriginResolver(IUnitOfWork uow, IIpLocationService locator, ReferenceDataCache reference)
{
    public async Task<VisitorOrigin?> ResolveAsync(string? clientIp, string? citySlug, string? areaSlug, double radiusKm, CancellationToken ct)
    {
        // Every active city, from memory: this runs on most location-aware requests (reviews, nearby services, search).
        var cities = await reference.ActiveCitiesAsync(ct);

        // 1. An explicit area wins.
        if (!string.IsNullOrWhiteSpace(citySlug) && !string.IsNullOrWhiteSpace(areaSlug))
        {
            var area = await uow.Repository<Area>().QueryNoTracking()
                .Where(a => a.IsActive && a.Slug == areaSlug && a.City.Slug == citySlug)
                .Select(a => new { a.Slug, a.Name, a.Latitude, a.Longitude, a.CityId })
                .FirstOrDefaultAsync(ct);
            var city = cities.FirstOrDefault(c => c.Id == area?.CityId);
            if (area is not null && city is not null)
                return new VisitorOrigin(city.Id, city.Slug, city.Name, city.State, area.Name, (double?)area.Latitude, (double?)area.Longitude, "area", area.Slug);
        }

        // 2. The visitor's IP coordinates, when they fall within range of a listed city (and it's the city being browsed).
        if (VisitorIp.Parse(clientIp) is { } ip && await locator.LocateAsync(ip, ct) is { Latitude: { } lat, Longitude: { } lng } loc)
        {
            var nearest = cities
                .Where(c => c.Latitude is not null && c.Longitude is not null)
                .Select(c => new { c, Km = GeoMath.HaversineKm(lat, lng, (double)c.Latitude!.Value, (double)c.Longitude!.Value) })
                .Where(x => x.Km <= radiusKm)
                .OrderBy(x => x.Km)
                .Select(x => x.c)
                .FirstOrDefault();
            if (nearest is not null && (string.IsNullOrWhiteSpace(citySlug) || citySlug == nearest.Slug))
            {
                // ip-api names the locality as "district"; DB-IP style names put it in brackets: "New Delhi (Okhla Phase III)".
                var place = loc.District ?? loc.City;
                if (place is not null && place.IndexOf('(') is var open and > 0) place = place[(open + 1)..].TrimEnd(')', ' ');
                return new VisitorOrigin(nearest.Id, nearest.Slug, nearest.Name, nearest.State, string.IsNullOrWhiteSpace(place) ? nearest.Name : place.Trim(),
                    lat, lng, "ip", $"{Math.Round(lat, 2)},{Math.Round(lng, 2)}");
            }
        }

        // 3. The centre of the city being browsed.
        var browsing = cities.FirstOrDefault(c => c.Slug == citySlug);
        return browsing is null ? null
            : new VisitorOrigin(browsing.Id, browsing.Slug, browsing.Name, browsing.State, browsing.Name,
                (double?)browsing.Latitude, (double?)browsing.Longitude, "city", "centre");
    }
}
