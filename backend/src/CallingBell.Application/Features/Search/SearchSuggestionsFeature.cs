using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Search;

/// <summary>
/// One autocomplete suggestion. <see cref="Kind"/> is Category, SubCategory, Service, Business, Location (a city or area), Popular (a
/// popular search) or Combined (a service in the selected area or city). The client builds the link from <see cref="Slug"/> (category /
/// sub-category / business / place slug), <see cref="SubCategorySlug"/>, and for places and combined searches <see cref="CitySlug"/> and
/// <see cref="AreaId"/>; <see cref="Query"/> is the text a popular search runs.
/// </summary>
public sealed record SearchSuggestionDto(string Kind, string Label, string? Detail, string? ImageUrl, string Slug, string? SubCategorySlug, decimal? Rating,
    string? CitySlug = null, Guid? AreaId = null, string? Query = null);

/// <param name="Combined">The best-matching service in the selected area, then city ("AC Repair in Kukatpally"), with real counts.</param>
/// <param name="Locations">Cities and areas whose name (or PIN code) matches, the selected city's first.</param>
/// <param name="Popular">Popular searches in the visitor's country that match.</param>
public sealed record SearchSuggestionsDto(string Query, IReadOnlyList<SearchSuggestionDto> Categories,
    IReadOnlyList<SearchSuggestionDto> Services, IReadOnlyList<SearchSuggestionDto> Businesses,
    IReadOnlyList<SearchSuggestionDto>? Combined = null, IReadOnlyList<SearchSuggestionDto>? Locations = null, IReadOnlyList<SearchSuggestionDto>? Popular = null);

/// <param name="City">The selected city's slug: its businesses and places come first, and combined suggestions use it.</param>
/// <param name="Area">The selected area within <paramref name="City"/>.</param>
/// <param name="Country">The visitor's country (ISO code), for places and popular searches; defaults to the city's.</param>
public sealed record GetSearchSuggestionsQuery(string? Q, string? City, Guid? Area = null, string? Country = null) : IRequest<SearchSuggestionsDto>;

public sealed class GetSearchSuggestionsHandler(IUnitOfWork uow, ISender sender) : IRequestHandler<GetSearchSuggestionsQuery, SearchSuggestionsDto>
{
    public const int MinLength = 2;
    private const int MaxLength = 60;

    private sealed record Place(Guid CityId, string CitySlug, string CityName, string CountryCode, Guid? AreaId, string? AreaName);

    public async Task<SearchSuggestionsDto> Handle(GetSearchSuggestionsQuery request, CancellationToken ct)
    {
        var q = (request.Q ?? string.Empty).Trim();
        if (q.Length > MaxLength) q = q[..MaxLength];
        if (q.Length < MinLength) return new SearchSuggestionsDto(q, [], [], [], [], [], []);
        var t = SuggestionText.Singular(q);
        var shortQuery = q.Length < 3;
        var place = await PlaceAsync(request.City, request.Area, ct);
        var country = SuggestionText.CountryCode(request.Country) ?? place?.CountryCode;

        var categories = await uow.Repository<Category>().QueryNoTracking()
            .Where(c => c.IsActive && c.Name.Contains(t))
            .OrderByDescending(c => c.Name.StartsWith(t)).ThenBy(c => c.SortOrder)
            .Take(2)
            .Select(c => new SearchSuggestionDto("Category", c.Name, "Category", c.IconUrl, c.Slug, null, null, null, null, null))
            .ToListAsync(ct);

        var subCategories = await uow.Repository<SubCategory>().QueryNoTracking()
            // Two letters only match the start of a name or word: inside descriptions they would match almost anything.
            .Where(s => s.IsActive && s.Category.IsActive && (shortQuery
                ? s.Name.StartsWith(t) || s.Name.Contains(" " + t)
                : s.Name.Contains(t) || (s.Description != null && s.Description.Contains(t))))
            .Select(s => new { s.Name, s.Slug, s.IconUrl, Cat = s.Category.Name, NameHit = s.Name.Contains(t), Starts = s.Name.StartsWith(t),
                               Count = s.Businesses.Count(b => b.Status == Domain.Constants.BusinessStatuses.Active) })
            .OrderByDescending(s => s.Starts).ThenByDescending(s => s.NameHit).ThenByDescending(s => s.Count)
            .Take(5 - categories.Count)
            .ToListAsync(ct);

        var services = await uow.Repository<BusinessService>().QueryNoTracking()
            .Where(s => s.IsActive && s.Business.Status == Domain.Constants.BusinessStatuses.Active && s.Business.SubCategory != null
                        && (s.Name.StartsWith(t) || s.Name.Contains(" " + t)))
            .GroupBy(s => new { s.Name, Sub = s.Business.SubCategory!.Name, SubSlug = s.Business.SubCategory.Slug, s.Business.SubCategory.IconUrl })
            .Select(g => new { g.Key, Starts = g.Key.Name.StartsWith(t), Count = g.Count() })
            .OrderByDescending(s => s.Starts).ThenByDescending(s => s.Count).ThenBy(s => s.Key.Name)
            .Take(5)
            .ToListAsync(ct);

        // Businesses in the selected area first, then the selected city, then anywhere.
        var areaId = place?.AreaId;
        var cityId = place?.CityId;
        var businesses = await uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => b.Name.StartsWith(t) || b.Name.Contains(" " + t))
            .OrderByDescending(b => areaId != null && b.AreaId == areaId)
            .ThenByDescending(b => cityId != null && b.CityId == cityId)
            .ThenByDescending(b => b.Name.StartsWith(t)).ThenByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount)
            .Take(5)
            .Select(b => new SearchSuggestionDto("Business", b.Name,
                b.Area != null ? b.Area + ", " + b.City : b.City, b.LogoUrl, b.Slug, null, b.ReviewCount > 0 ? b.AverageRating : null, null, null, null))
            .ToListAsync(ct);

        var subs = subCategories.Select(s => new SearchSuggestionDto("SubCategory", s.Name, s.Cat, s.IconUrl, s.Slug, s.Slug, null)).ToList();
        // A service named exactly like a listed category adds nothing.
        var serviceItems = services.Where(s => !subs.Any(x => string.Equals(x.Label, s.Key.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(s => new SearchSuggestionDto("Service", s.Key.Name, s.Key.Sub, s.Key.IconUrl, s.Key.SubSlug, s.Key.SubSlug, null)).ToList();

        // The best match for "what" (a category whose name matches, else the first matching service's), searched where the visitor is.
        // Two letters say too little to suggest a service somewhere from a service name alone.
        var what = subCategories.FirstOrDefault(s => s.NameHit) is { } hit ? (hit.Name, hit.Slug, hit.IconUrl)
            : !shortQuery && services.FirstOrDefault() is { } svc ? (svc.Key.Sub, svc.Key.SubSlug, svc.Key.IconUrl)
            : ((string Name, string Slug, string? IconUrl)?)null;
        var combined = place is not null && what is { } w ? await CombinedAsync(w.Name, w.Slug, w.IconUrl, place, ct) : [];

        var locations = await LocationsAsync(q, t, country, cityId, ct);
        var shown = categories.Concat(subs).Concat(serviceItems).Select(s => s.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var popular = (await sender.Send(new GetPopularSearchesQuery(country, 60), ct))
            .Where(p => (p.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || p.SearchText.Contains(t, StringComparison.OrdinalIgnoreCase))
                        && !shown.Contains(p.Label) && !shown.Contains(p.SearchText))
            .Take(3)
            .Select(p => new SearchSuggestionDto("Popular", p.Label, p.Group ?? "Popular search", p.IconUrl,
                p.SubCategorySlug ?? p.CategorySlug ?? p.Code, p.SubCategorySlug, null, Query: p.SearchText))
            .ToList();

        return new SearchSuggestionsDto(q, categories.Concat(subs).ToList(), serviceItems, businesses, combined, locations, popular);
    }

    /// <summary>The selected city (and area within it), with the names combined suggestions show; null when no city is selected.</summary>
    private async Task<Place?> PlaceAsync(string? citySlug, Guid? areaId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(citySlug) || citySlug.Length > 120) return null;
        var city = await uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive && c.Slug == citySlug)
            .Select(c => new { c.Id, c.Slug, c.Name, c.State.CountryCode }).FirstOrDefaultAsync(ct);
        if (city is null) return null;
        var area = areaId is { } id
            ? await uow.Repository<Area>().QueryNoTracking().Where(a => a.Id == id && a.CityId == city.Id && a.IsActive).Select(a => new { a.Id, a.Name }).FirstOrDefaultAsync(ct)
            : null;
        return new Place(city.Id, city.Slug, city.Name, city.CountryCode, area?.Id, area?.Name);
    }

    /// <summary>
    /// "AC Repair in Kukatpally", then "AC Repair in Hyderabad": the selected area first, then its city, each with the number of listed
    /// businesses there (or that there are none yet), so nothing about availability is made up.
    /// </summary>
    private async Task<List<SearchSuggestionDto>> CombinedAsync(string what, string subSlug, string? iconUrl, Place place, CancellationToken ct)
    {
        var listed = uow.Repository<Business>().QueryNoTracking().Listed().Where(b => b.SubCategory != null && b.SubCategory.Slug == subSlug);
        var result = new List<SearchSuggestionDto>();
        if (place is { AreaId: { } areaId, AreaName: { } areaName })
        {
            var inArea = await listed.CountAsync(b => b.AreaId == areaId, ct);
            result.Add(new SearchSuggestionDto("Combined", SuggestionText.Combined(what, areaName), SuggestionText.CountDetail(inArea, areaName), iconUrl,
                subSlug, subSlug, null, place.CitySlug, areaId));
        }
        var inCity = await listed.CountAsync(b => b.CityId == place.CityId, ct);
        result.Add(new SearchSuggestionDto("Combined", SuggestionText.Combined(what, place.CityName), SuggestionText.CountDetail(inCity, place.CityName), iconUrl,
            subSlug, subSlug, null, place.CitySlug));
        return result;
    }

    /// <summary>Areas (by name, or by PIN code when digits are typed) and cities in the visitor's country, the selected city's first.</summary>
    private async Task<List<SearchSuggestionDto>> LocationsAsync(string q, string t, string? country, Guid? cityId, CancellationToken ct)
    {
        var pin = SuggestionText.IsPostalCodePrefix(q);
        var areas = await uow.Repository<Area>().QueryNoTracking()
            .Where(a => a.IsActive && a.City.IsActive && (country == null || a.City.State.CountryCode == country)
                        && (pin ? a.Pincode.StartsWith(q) : a.Name.StartsWith(q) || a.Name.Contains(" " + q)))
            .OrderByDescending(a => cityId != null && a.CityId == cityId).ThenByDescending(a => a.ParentAreaId == null)
            .ThenByDescending(a => a.Name.StartsWith(q)).ThenBy(a => a.Name)
            .Take(4)
            // A sub-locality selects the area it belongs to (the location picker lists top-level areas).
            .Select(a => new { a.Name, a.Slug, a.Pincode, City = a.City.Name, CitySlug = a.City.Slug, AreaId = a.ParentAreaId ?? a.Id,
                               InSelectedCity = cityId != null && a.CityId == cityId })
            .ToListAsync(ct);
        var cities = pin ? [] : await uow.Repository<City>().QueryNoTracking()
            .Where(c => c.IsActive && (country == null || c.State.CountryCode == country) && (c.Name.StartsWith(t) || c.Name.StartsWith(q)))
            .OrderByDescending(c => c.Id == cityId).ThenBy(c => c.Name)
            .Take(2)
            .Select(c => new SearchSuggestionDto("Location", c.Name, "City · " + c.State.Name, null, c.Slug, null, null, c.Slug, null, null))
            .ToListAsync(ct);
        var areaItems = areas.Select(a => (InCity: a.InSelectedCity,
            Item: new SearchSuggestionDto("Location", a.Name, pin ? $"{a.Pincode} · {a.City}" : $"Area · {a.City}", null, a.Slug, null, null, a.CitySlug, a.AreaId))).ToList();
        // Areas of the selected city first, then cities, then areas elsewhere.
        return [.. areaItems.Where(a => a.InCity).Select(a => a.Item), .. cities, .. areaItems.Where(a => !a.InCity).Select(a => a.Item)];
    }
}

// ===================== Suggestions by meaning (local AI) =====================

/// <summary>Sub-categories the local AI matches by meaning ("water dripping from the ceiling" gives Plumbers).</summary>
public sealed record GetMeaningSuggestionsQuery(string? Q) : IRequest<IReadOnlyList<SearchSuggestionDto>>;

/// <summary>
/// The search box's "Suggested by AI" group. Separate from (and slower than) the name-based suggestions, so typing never waits for
/// the model: the client asks once the visitor pauses on a phrase. Only confident matches, plus close runners-up.
/// </summary>
public sealed class GetMeaningSuggestionsHandler(IUnitOfWork uow, ISemanticCatalog semantic)
    : IRequestHandler<GetMeaningSuggestionsQuery, IReadOnlyList<SearchSuggestionDto>>
{
    public const int MinLength = 8;
    private const int MaxLength = 120;

    public async Task<IReadOnlyList<SearchSuggestionDto>> Handle(GetMeaningSuggestionsQuery request, CancellationToken ct)
    {
        var q = (request.Q ?? string.Empty).Trim();
        if (q.Length > MaxLength) q = q[..MaxLength];
        // Phrases ("tap leaking", "water dripping from ceiling"), not single words, which the name-based suggestions cover.
        if (q.Length < MinLength || !q.Contains(' ')) return [];

        var result = await semantic.MatchAsync(q, 3, ct);
        if (result is not { Confident: true }) return [];
        var top = result.Top[0].Score;
        var slugs = result.Top.Where(m => top - m.Score <= 0.03).Select(m => m.SubCategorySlug).ToList();
        var subs = await uow.Repository<SubCategory>().QueryNoTracking().Where(s => slugs.Contains(s.Slug))
            .Select(s => new SearchSuggestionDto("SubCategory", s.Name, s.Category.Name, s.IconUrl, s.Slug, s.Slug, null, null, null, null))
            .ToDictionaryAsync(s => s.Slug, ct);
        return slugs.Where(subs.ContainsKey).Select(slug => subs[slug]).ToList(); // best match first
    }
}
