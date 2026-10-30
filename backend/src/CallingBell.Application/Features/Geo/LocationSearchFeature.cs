using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Geo;

/// <summary>A place the location box suggests: a whole city, or an area within one.</summary>
/// <param name="Kind">"city" or "area".</param>
/// <param name="District">The district: an area's from its PIN code (India Post), else the city's (GeoNames); null when unknown.</param>
/// <param name="Matched">The alternate name, sub-locality or PIN code that matched, when it isn't the name itself ("includes Hydernagar").</param>
/// <param name="BusinessCount">Active businesses listed in the city or area.</param>
public sealed record LocationSuggestionDto(string Kind, string CitySlug, string CityName, string? District, string? StateSlug, string StateName,
    Guid? AreaId, string? AreaSlug, string? AreaName, string? Pincode, string? Matched, int BusinessCount);

/// <param name="Q">What the visitor typed: a city, area, locality or PIN code. Empty gives the country's popular cities.</param>
/// <param name="Country">ISO 3166-1 alpha-2; the default country when missing.</param>
/// <param name="City">The city being browsed: its areas are listed first.</param>
public sealed record SearchLocationsQuery(string? Q, string? Country, string? City) : IRequest<IReadOnlyList<LocationSuggestionDto>>;

/// <summary>
/// Type-ahead for the location box: cities (by name or alternate name) and areas (by name, alternate name, sub-locality or PIN code)
/// across the visitor's country. A sub-locality is offered as the area it belongs to, since areas are what pages filter by.
/// </summary>
public sealed class SearchLocationsHandler(IUnitOfWork uow, IGeoLocationService geo) : IRequestHandler<SearchLocationsQuery, IReadOnlyList<LocationSuggestionDto>>
{
    private const int MaxCities = 5;
    private const int MaxAreas = 8;

    public async Task<IReadOnlyList<LocationSuggestionDto>> Handle(SearchLocationsQuery r, CancellationToken ct)
    {
        var country = r.Country is { Length: 2 } c2 && c2.All(char.IsAsciiLetter) ? c2.ToUpperInvariant() : geo.DefaultCountryCode;
        var q = (r.Q ?? "").Trim();
        if (q.Length > 80) q = q[..80];
        var preferred = string.IsNullOrWhiteSpace(r.City) ? null : r.City.Trim().ToLowerInvariant();

        var cityQuery = uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive && c.State.CountryCode == country);
        cityQuery = q.Length == 0
            ? cityQuery.Where(c => c.IsPopular)
            : cityQuery.Where(c => c.Name.Contains(q) || (c.AltNames != null && c.AltNames.Contains(q)));
        var cities = await cityQuery
            .OrderBy(c => c.Name.StartsWith(q) ? 0 : 1).ThenByDescending(c => c.IsPopular).ThenBy(c => c.Source != null)
            .ThenBy(c => c.SortOrder).ThenByDescending(c => c.Population).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Slug, c.Name, c.AltNames, c.District, StateSlug = c.State.Slug, StateName = c.State.Name })
            .Take(q.Length == 0 ? 8 : MaxCities)
            .ToListAsync(ct);

        // Areas need two letters (or a PIN code's digits); a single letter would match half the country.
        var areas = q.Length < 2 ? [] : await uow.Repository<Area>().QueryNoTracking()
            .Where(a => a.IsActive && a.City.IsActive && a.City.State.CountryCode == country
                        && (a.Name.Contains(q) || a.Pincode.StartsWith(q) || (a.AltNames != null && a.AltNames.Contains(q))))
            .OrderBy(a => a.City.Slug == preferred ? 0 : 1).ThenBy(a => a.Name.StartsWith(q) ? 0 : 1)
            .ThenBy(a => a.ParentAreaId == null ? 0 : 1).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new
            {
                a.Id, a.Name, a.Slug, a.Pincode, a.AltNames, a.ParentAreaId,
                ParentName = a.ParentArea != null ? a.ParentArea.Name : null,
                ParentSlug = a.ParentArea != null ? a.ParentArea.Slug : null,
                ParentPincode = a.ParentArea != null ? a.ParentArea.Pincode : null,
                CityId = a.CityId, CitySlug = a.City.Slug, CityName = a.City.Name,
                District = (a.ParentArea != null ? a.ParentArea.District : null) ?? a.District ?? a.City.District, StateSlug = a.City.State.Slug, StateName = a.City.State.Name,
            })
            .Take(MaxAreas * 4)
            .ToListAsync(ct);

        // One suggestion per top-level area: a sub-locality stands for its parent.
        var areaRows = areas
            .Select(a => new
            {
                AreaId = a.ParentAreaId ?? a.Id,
                Name = a.ParentName ?? a.Name,
                Slug = a.ParentSlug ?? a.Slug,
                Pincode = a.ParentAreaId is null ? a.Pincode : a.ParentPincode ?? a.Pincode,
                Matched = a.ParentAreaId is not null ? a.Name
                    : a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ? null
                    : a.Pincode.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? null
                    : AltMatch(a.AltNames, q),
                a.CityId, a.CitySlug, a.CityName, a.District, a.StateSlug, a.StateName,
            })
            .DistinctBy(a => a.AreaId)
            .Take(MaxAreas)
            .ToList();

        var cityIds = cities.Select(c => c.Id).ToList();
        var areaIds = areaRows.Select(a => a.AreaId).ToList();
        var active = uow.Repository<Business>().QueryNoTracking().Where(b => b.Status == BusinessStatuses.Active);
        var cityCounts = cityIds.Count == 0 ? new Dictionary<Guid, int>() : await active
            .Where(b => b.CityId != null && cityIds.Contains(b.CityId.Value))
            .GroupBy(b => b.CityId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var areaCounts = areaIds.Count == 0 ? new Dictionary<Guid, int>() : await active
            .Where(b => b.AreaId != null && areaIds.Contains(b.AreaId.Value))
            .GroupBy(b => b.AreaId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var cityResults = cities.Select(c => (Starts: StartsWord(c.Name, c.AltNames, q), Dto: new LocationSuggestionDto("city", c.Slug, c.Name, c.District, c.StateSlug,
            c.StateName, null, null, null, null, q.Length == 0 || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ? null : AltMatch(c.AltNames, q),
            cityCounts.GetValueOrDefault(c.Id)))).ToList();
        var areaResults = areaRows.Select(a => (Local: a.CitySlug == preferred, Dto: new LocationSuggestionDto("area", a.CitySlug, a.CityName, a.District, a.StateSlug,
            a.StateName, a.AreaId, a.Slug, a.Name, string.IsNullOrWhiteSpace(a.Pincode) ? null : a.Pincode, a.Matched, areaCounts.GetValueOrDefault(a.AreaId)))).ToList();

        // Areas of the browsed city, then cities whose name (or other name: "Bangal" → Bengaluru) starts with the text, then other areas,
        // then the remaining cities. Within the others, places with listed businesses come first ("Kora" → Koramangala before Koratla).
        var others = cityResults.Where(c => c.Starts).Select(c => c.Dto).Concat(areaResults.Where(a => !a.Local).Select(a => a.Dto))
            .OrderByDescending(d => q.Length > 0 && d.BusinessCount > 0);
        return areaResults.Where(a => a.Local).Select(a => a.Dto)
            .Concat(others)
            .Concat(cityResults.Where(c => !c.Starts).Select(c => c.Dto))
            .ToList();
    }

    private static bool StartsWord(string name, string? altNames, string q) =>
        q.Length == 0 || name.StartsWith(q, StringComparison.OrdinalIgnoreCase)
        || (altNames ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(n => n.StartsWith(q, StringComparison.OrdinalIgnoreCase));

    private static string? AltMatch(string? altNames, string q) =>
        (altNames ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(n => n.Contains(q, StringComparison.OrdinalIgnoreCase));
}
