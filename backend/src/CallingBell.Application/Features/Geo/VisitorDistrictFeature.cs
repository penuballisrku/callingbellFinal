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
/// loading the city list first. <paramref name="StateSlug"/> lets the state / province / region be selected without loading the
/// country's city list.
/// </summary>
public sealed record VisitorDistrictDto(string CitySlug, string CityName, string State, string? AreaSlug, string MatchedBy, double? DistanceKm,
    Guid? AreaId = null, Guid? CityId = null, string? StateSlug = null, string? AreaName = null);

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
/// <param name="CountryName">Name of <paramref name="Country"/>.</param>
/// <param name="Importing">True while the country's cities are being imported (the first visit from a new country): poll until false,
/// then the district can be matched.</param>
/// <param name="Located">False when the IP couldn't be located (private network, unknown address, IP service unavailable): the country
/// is then a best guess and the visitor chooses their city themselves.</param>
public sealed record VisitorLocationDto(string? Place, string? Region, VisitorDistrictDto? District,
    string? Country = null, string? Postcode = null, double? Latitude = null, double? Longitude = null,
    string? CountryName = null, bool Importing = false, bool Located = false);

/// <param name="CdnCountry">Country from a CDN edge header (e.g. Cloudflare CF-IPCountry), which wins when present.</param>
public sealed record GetVisitorDistrictQuery(string? ClientIp, double RadiusKm, string? CdnCountry = null) : IRequest<VisitorLocationDto>;

/// <summary>
/// Everything the location picker needs from the visitor's IP address in one request: country (and whether its cities are still being
/// imported), state / province / region, city and area. Only cities of the IP's country are considered, so "London" from a UK address
/// is never London, Ontario.
/// </summary>
public sealed class GetVisitorDistrictHandler(IIpLocationService locator, IUnitOfWork uow, IAreaDiscoveryService discovery, ReferenceDataCache reference,
    ISender sender, ReverseGeocoder geocoder) : IRequestHandler<GetVisitorDistrictQuery, VisitorLocationDto>
{
    public async Task<VisitorLocationDto> Handle(GetVisitorDistrictQuery r, CancellationToken ct)
    {
        var loc = VisitorIp.Parse(r.ClientIp) is { } ip ? await locator.LocateAsync(ip, ct) : null;
        // The country: a CDN edge header, else the IP service's answer, else the local GeoIP database (the catalogue query's fallback).
        // Its cities are imported on the first visit from it.
        var cdn = r.CdnCountry?.Trim().ToUpperInvariant() is { Length: 2 } h && h.All(char.IsAsciiLetter) && h is not ("XX" or "T1") ? h : null;
        var catalog = await sender.Send(new GetCountryCatalogQuery(cdn is null ? loc?.CountryCode : null, cdn, r.ClientIp), ct);
        var country = catalog.CountryCode;
        if (loc is null) return new VisitorLocationDto(null, null, null, country, CountryName: catalog.CountryName, Importing: catalog.Importing);

        // The country's cities with their areas, from memory: built once per country and cleared when cities or areas change.
        var cities = await reference.GetDerivedAsync($"district-index|{country}", () => LoadIndexAsync(country, ct), ct);

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
            // A name only counts in the IP's own region, or near its coordinates: "Ashburn" from Ashburn, Virginia is not the Ashburn
            // neighbourhood of Chicago. Without coordinates or a region, names are all there is.
            bool Plausible(string state, decimal? placeLat, decimal? placeLng) =>
                Same(state, loc.Region ?? "")
                || (loc is { Latitude: { } ipLat, Longitude: { } ipLng }
                    ? placeLat is { } pl && placeLng is { } pg && GeoMath.HaversineKm(ipLat, ipLng, (double)pl, (double)pg) <= r.RadiusKm
                    : string.IsNullOrWhiteSpace(loc.Region));

            // 1. A locality we list (or one of its alternate names / sub-localities) pins both the district and the area;
            // 2. otherwise the IP's PIN code among top-level areas; 3. otherwise the city itself.
            district = cities
                .SelectMany(c => c.Areas.Where(a => NameMatch(a.Name, a.AltNames) && Plausible(c.State, a.Latitude ?? c.Latitude, a.Longitude ?? c.Longitude))
                    .Select(a => new { Dto = new VisitorDistrictDto(c.Slug, c.Name, c.State, a.AreaSlug, "area", null), SameState = Same(c.State, loc.Region ?? ""), a.IsTop }))
                .OrderByDescending(x => x.SameState).ThenByDescending(x => x.IsTop)
                .Select(x => x.Dto)
                .FirstOrDefault()
                ?? cities
                .SelectMany(c => c.Areas.Where(a => a.IsTop && loc.Postcode is { } pin && a.Pincode == pin && Plausible(c.State, a.Latitude ?? c.Latitude, a.Longitude ?? c.Longitude))
                    .Select(a => new { Dto = new VisitorDistrictDto(c.Slug, c.Name, c.State, a.Slug, "area", null), SameState = Same(c.State, loc.Region ?? "") }))
                .OrderByDescending(x => x.SameState)
                .Select(x => x.Dto)
                .FirstOrDefault()
                ?? cities.Where(c => Same(c.Name, town) && Plausible(c.State, c.Latitude, c.Longitude))
                    .Select(c => new VisitorDistrictDto(c.Slug, c.Name, c.State, null, "city", null)).FirstOrDefault();
        }
        // Otherwise reverse-geocode ip-api.com's coordinates against the listed places (spatial index; see ReverseGeocoder): the area they
        // fall in, else the nearest city centre within district range (covers suburbs and alternate names, e.g. Bangalore, Cochin).
        // Computed at most once per request, and only when the names didn't already pin the area.
        ReverseGeocodeResult? geo = null;
        var geocoded = false;
        async Task<ReverseGeocodeResult?> GeocodeAsync()
        {
            if (!geocoded && loc is { Latitude: { } lat, Longitude: { } lng }) geo = await geocoder.ReverseAsync(lat, lng, country, r.RadiusKm, ct);
            geocoded = true;
            return geo;
        }
        if (district is null && await GeocodeAsync() is { } g)
            district = new VisitorDistrictDto(g.City.Slug, g.City.Name, g.Region?.Name ?? "", g.Area?.Slug, "distance", g.CityKm, g.Area?.Id,
                g.City.Id, g.Region?.Slug, g.Area?.Name);

        if (district is not null && cities.FirstOrDefault(c => c.Slug == district.CitySlug) is { } detected)
        {
            district = district with { CityId = detected.Id, StateSlug = detected.StateSlug };
            // City known but no area matched by name or PIN: the area the IP's coordinates fall in, when it is in that city.
            if (district.AreaSlug is null && await GeocodeAsync() is { Area: { } near } nearby && nearby.City.Id == detected.Id)
                district = district with { AreaSlug = near.Slug };
            if (district.AreaSlug is { } areaSlug && detected.Areas.FirstOrDefault(a => a.IsTop && a.Slug == areaSlug) is { } area)
                district = district with { AreaId = area.Id, AreaName = area.Name };
            // Load the detected city's areas: discover them if never done or stale.
            if (detected.AreasDiscoveredOn is null || detected.AreasDiscoveredOn < DateTimeOffset.UtcNow - GetCityAreasHandler.RefreshAfter)
                discovery.Request(detected.Id);
        }

        return new VisitorLocationDto(loc.City, loc.Region, district, country, loc.Postcode,
            loc.Latitude is { } y ? Math.Round(y, 2) : null, loc.Longitude is { } x ? Math.Round(x, 2) : null,
            catalog.CountryName, catalog.Importing, Located: true);
    }

    private async Task<List<DistrictCity>> LoadIndexAsync(string country, CancellationToken ct)
    {
        var cities = await uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive && c.State.CountryCode == country)
            .Select(c => new { c.Id, c.Slug, c.Name, State = c.State.Name, StateSlug = c.State.Slug, c.Latitude, c.Longitude, c.AreasDiscoveredOn })
            .ToListAsync(ct);
        var areas = (await uow.Repository<Area>().QueryNoTracking().Where(a => a.IsActive && a.City.IsActive && a.City.State.CountryCode == country)
                .Select(a => new
                {
                    a.CityId,
                    Area = new DistrictArea(a.Id, a.Slug, a.Name, a.Pincode, a.AltNames, a.Latitude, a.Longitude, a.ParentAreaId == null,
                        // A sub-locality resolves to the area it belongs to.
                        a.ParentArea != null ? a.ParentArea.Slug : a.Slug),
                })
                .ToListAsync(ct))
            .ToLookup(x => x.CityId, x => x.Area);
        return cities.Select(c => new DistrictCity(c.Id, c.Slug, c.Name, c.State, c.StateSlug, c.Latitude, c.Longitude, c.AreasDiscoveredOn,
            areas[c.Id].ToList())).ToList();
    }

    private sealed record DistrictCity(Guid Id, string Slug, string Name, string State, string StateSlug, decimal? Latitude, decimal? Longitude,
        DateTimeOffset? AreasDiscoveredOn, List<DistrictArea> Areas);

    private sealed record DistrictArea(Guid Id, string Slug, string Name, string Pincode, string? AltNames, decimal? Latitude, decimal? Longitude, bool IsTop,
        string AreaSlug);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>"Saket" matches "Saket" and "Saket District Centre", but not "Saketpuri".</summary>
    private static bool IsPlace(string ipPlace, string areaName) =>
        Same(ipPlace, areaName) || ipPlace.StartsWith(areaName.Trim() + " ", StringComparison.OrdinalIgnoreCase);

}
