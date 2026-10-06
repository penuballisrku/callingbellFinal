using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Geo;

/// <summary>
/// The listed city whose district the visitor is browsing from. <paramref name="MatchedBy"/> is "area" (the IP's place name is one of
/// the city's areas), "city" (it is the city itself) or "distance" (within the district radius of the city centre).
/// <paramref name="AreaSlug"/> (and its <paramref name="AreaId"/>) is set when an area matched, so the client can select it without
/// loading the city list first.
/// </summary>
public sealed record VisitorDistrictDto(string CitySlug, string CityName, string State, string? AreaSlug, string MatchedBy, double? DistanceKm,
    Guid? AreaId = null);

/// <summary>
/// Approximate visitor location from the IP address (the IP itself is never returned).
/// </summary>
/// <param name="Place">The IP's place name (e.g. "Secunderabad"), when known.</param>
/// <param name="Region">State or region.</param>
/// <param name="Country">ISO country code.</param>
/// <param name="Postcode">PIN code, when the IP service knows it.</param>
/// <param name="Latitude">Approximate latitude (rounded to ~1 km).</param>
/// <param name="Longitude">Approximate longitude (rounded to ~1 km).</param>
/// <param name="District">Null when the IP can't be located or isn't near any listed city.</param>
public sealed record VisitorLocationDto(string? Place, string? Region, VisitorDistrictDto? District,
    string? Country = null, string? Postcode = null, double? Latitude = null, double? Longitude = null);

public sealed record GetVisitorDistrictQuery(string? ClientIp, double RadiusKm) : IRequest<VisitorLocationDto>;

public sealed class GetVisitorDistrictHandler(IIpLocationService locator, IUnitOfWork uow, IAreaDiscoveryService discovery, ReferenceDataCache reference)
    : IRequestHandler<GetVisitorDistrictQuery, VisitorLocationDto>
{
    /// <summary>When nothing matches by name, the nearest area within this distance of the IP's coordinates is pre-selected.</summary>
    private const double NearestAreaKm = 3;

    public async Task<VisitorLocationDto> Handle(GetVisitorDistrictQuery r, CancellationToken ct)
    {
        if (VisitorIp.Parse(r.ClientIp) is not { } ip || await locator.LocateAsync(ip, ct) is not { } loc) return new VisitorLocationDto(null, null, null);

        // Every city with its areas (thousands of rows), from memory: built once and cleared when cities or areas change.
        var cities = await reference.GetDerivedAsync("district-index", () => LoadIndexAsync(ct), ct);

        VisitorDistrictDto? district = null;
        if (!string.IsNullOrWhiteSpace(loc.City))
        {
            // DB-IP names the locality in brackets, e.g. "New Delhi (Okhla Phase III)".
            var place = loc.City.Trim();
            var open = place.IndexOf('(');
            var town = open > 0 ? place[..open].Trim() : place;
            var locality = open > 0 ? place[(open + 1)..].TrimEnd(')', ' ') : null;
            // ip-api.com may also name the locality separately as the district (e.g. "Kukatpally").
            string[] names = [.. new[] { loc.District, locality, town }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim())];
            bool NameMatch(string name, string? alt) =>
                names.Any(n => IsPlace(n, name) || (alt ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Any(x => IsPlace(n, x)));

            // 1. A locality we list (or one of its alternate names / sub-localities) pins both the district and the area;
            // 2. otherwise the IP's PIN code among top-level areas; 3. otherwise the city itself.
            district = cities
                .SelectMany(c => c.Areas.Where(a => NameMatch(a.Name, a.AltNames))
                    .Select(a => new { Dto = new VisitorDistrictDto(c.Slug, c.Name, c.State, a.AreaSlug, "area", null), SameState = Same(c.State, loc.Region ?? ""), a.IsTop }))
                .OrderByDescending(x => x.SameState).ThenByDescending(x => x.IsTop)
                .Select(x => x.Dto)
                .FirstOrDefault()
                ?? cities
                .SelectMany(c => c.Areas.Where(a => a.IsTop && loc.Postcode is { } pin && a.Pincode == pin)
                    .Select(a => new { Dto = new VisitorDistrictDto(c.Slug, c.Name, c.State, a.Slug, "area", null), SameState = Same(c.State, loc.Region ?? "") }))
                .OrderByDescending(x => x.SameState)
                .Select(x => x.Dto)
                .FirstOrDefault()
                ?? cities.Where(c => Same(c.Name, town)).Select(c => new VisitorDistrictDto(c.Slug, c.Name, c.State, null, "city", null)).FirstOrDefault();
        }
        // Otherwise the nearest listed city whose centre is within district range (covers suburbs and alternate names, e.g. Bangalore, Cochin).
        if (district is null && loc is { Latitude: { } lat, Longitude: { } lng })
        {
            district = cities
                .Where(c => c.Latitude is not null && c.Longitude is not null)
                .Select(c => new { c, Km = HaversineKm(lat, lng, (double)c.Latitude!.Value, (double)c.Longitude!.Value) })
                .Where(x => x.Km <= r.RadiusKm)
                .OrderBy(x => x.Km)
                .Select(x => new VisitorDistrictDto(x.c.Slug, x.c.Name, x.c.State, null, "distance", Math.Round(x.Km, 1)))
                .FirstOrDefault();
        }

        if (district is not null && cities.FirstOrDefault(c => c.Slug == district.CitySlug) is { } detected)
        {
            // City known but no area matched by name or PIN: pre-select the nearest area to the IP's coordinates.
            if (district.AreaSlug is null && loc is { Latitude: { } la, Longitude: { } lo })
            {
                var nearest = detected.Areas
                    .Where(a => a.IsTop && a.Latitude != null && a.Longitude != null)
                    .Select(a => new { a.Slug, Km = HaversineKm(la, lo, (double)a.Latitude!.Value, (double)a.Longitude!.Value) })
                    .MinBy(x => x.Km);
                if (nearest is not null && nearest.Km <= NearestAreaKm) district = district with { AreaSlug = nearest.Slug };
            }
            if (district.AreaSlug is { } areaSlug)
                district = district with { AreaId = detected.Areas.FirstOrDefault(a => a.IsTop && a.Slug == areaSlug)?.Id };
            // Load the detected city's areas: discover them if never done or stale.
            if (detected.AreasDiscoveredOn is null || detected.AreasDiscoveredOn < DateTimeOffset.UtcNow - GetCityAreasHandler.RefreshAfter)
                discovery.Request(detected.Id);
        }

        return new VisitorLocationDto(loc.City, loc.Region, district, loc.CountryCode, loc.Postcode,
            loc.Latitude is { } y ? Math.Round(y, 2) : null, loc.Longitude is { } x ? Math.Round(x, 2) : null);
    }

    private async Task<List<DistrictCity>> LoadIndexAsync(CancellationToken ct)
    {
        var cities = await uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Slug, c.Name, State = c.State.Name, c.Latitude, c.Longitude, c.AreasDiscoveredOn })
            .ToListAsync(ct);
        var areas = (await uow.Repository<Area>().QueryNoTracking().Where(a => a.IsActive && a.City.IsActive)
                .Select(a => new
                {
                    a.CityId,
                    Area = new DistrictArea(a.Id, a.Slug, a.Name, a.Pincode, a.AltNames, a.Latitude, a.Longitude, a.ParentAreaId == null,
                        // A sub-locality resolves to the area it belongs to.
                        a.ParentArea != null ? a.ParentArea.Slug : a.Slug),
                })
                .ToListAsync(ct))
            .ToLookup(x => x.CityId, x => x.Area);
        return cities.Select(c => new DistrictCity(c.Id, c.Slug, c.Name, c.State, c.Latitude, c.Longitude, c.AreasDiscoveredOn, areas[c.Id].ToList())).ToList();
    }

    private sealed record DistrictCity(Guid Id, string Slug, string Name, string State, decimal? Latitude, decimal? Longitude,
        DateTimeOffset? AreasDiscoveredOn, List<DistrictArea> Areas);

    private sealed record DistrictArea(Guid Id, string Slug, string Name, string Pincode, string? AltNames, decimal? Latitude, decimal? Longitude, bool IsTop,
        string AreaSlug);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>"Saket" matches "Saket" and "Saket District Centre", but not "Saketpuri".</summary>
    private static bool IsPlace(string ipPlace, string areaName) =>
        Same(ipPlace, areaName) || ipPlace.StartsWith(areaName.Trim() + " ", StringComparison.OrdinalIgnoreCase);

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2) => GeoMath.HaversineKm(lat1, lng1, lat2, lng2);
}
