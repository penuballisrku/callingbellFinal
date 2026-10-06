using System.Text.RegularExpressions;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Search;

/// <summary>A listed city, or an area within it, that the place typed in a search matched. These are the location dropdown's values.</summary>
/// <param name="AreaId">Null when the place is the city itself. A neighbourhood maps to the area it belongs to.</param>
/// <param name="MatchedBy">"city", "area", "altName" (alternate spelling) or "pincode".</param>
public sealed record SearchPlaceMatchDto(string CitySlug, string CityName, Guid? AreaId, string? AreaName, string MatchedBy);

/// <param name="Text">The text as typed, trimmed.</param>
/// <param name="Query">What is being searched for, without the place: "lawyers in Nellore" gives "lawyers".</param>
/// <param name="PlaceText">The place after "in"/"near", as typed; null when the text names none.</param>
/// <param name="Place">The listed city/area the place matched; null when it names none or isn't listed (e.g. a city not on Calling Bell yet).</param>
/// <param name="CategorySlug">Set when <paramref name="Query"/> names a category, e.g. "legal services".</param>
/// <param name="SubCategorySlug">Set when <paramref name="Query"/> names a sub-category, e.g. "lawyers".</param>
public sealed record ParsedSearchDto(string Text, string Query, string? PlaceText, SearchPlaceMatchDto? Place, string? CategorySlug, string? SubCategorySlug);

/// <param name="City">The city selected in the dropdown: an area name found in several cities resolves to this one first.</param>
/// <param name="ByMeaning">When the query names no category, match it by meaning with the local AI (embeddings), e.g. "tap leaking" gives Plumbers.</param>
public sealed record ParseSearchQuery(string? Text, string? City, bool ByMeaning = true) : IRequest<ParsedSearchDto>;

public sealed class ParseSearchValidator : AbstractValidator<ParseSearchQuery>
{
    public ParseSearchValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(120);
    }
}

/// <summary>
/// Splits a search such as "lawyers in Nellore" or "plumbers near Madhapur, Hyderabad" into what is searched for and where, and matches
/// the place to a listed city or area (city name or slug, area name, alternate spelling or PIN code) so the location dropdown can show it.
/// </summary>
public sealed partial class ParseSearchHandler(IUnitOfWork uow, ReferenceDataCache reference, ISemanticCatalog semantic) : IRequestHandler<ParseSearchQuery, ParsedSearchDto>
{
    /// <summary>The last " in " / " near " separates the place: "work from home jobs in pune" keeps "work from home jobs".</summary>
    [GeneratedRegex(@"^(?<what>.+)\s+(?:in|near|around|at)\s+(?<where>[^\s].*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceSplit();

    /// <summary>"near me" and the like name no place: the selected location applies.</summary>
    private static readonly HashSet<string> NotPlaces = new(StringComparer.OrdinalIgnoreCase)
        { "me", "my area", "my location", "here", "nearby", "my city", "home", "my home" };

    public async Task<ParsedSearchDto> Handle(ParseSearchQuery r, CancellationToken ct)
    {
        var text = Regex.Replace(r.Text!.Trim(), @"\s+", " ");
        string query = text;
        string? placeText = null;
        if (PlaceSplit().Match(text) is { Success: true } m)
        {
            var where = m.Groups["where"].Value.Trim().TrimEnd('.', ',', '!', '?');
            if (NotPlaces.Contains(where)) query = m.Groups["what"].Value.Trim(); // "doctors near me": the selected location applies
            else if (Norm(where).Length >= 2)
            {
                query = m.Groups["what"].Value.Trim();
                placeText = where;
            }
        }

        var place = placeText is null ? null : await MatchPlaceAsync(placeText, r.City, ct);
        var (categorySlug, subSlug) = await MatchCatalogueAsync(query, ct);
        if (categorySlug is null && subSlug is null && r.ByMeaning && await semantic.MatchAsync(query, 1, ct) is { Confident: true } match)
            subSlug = match.Top[0].SubCategorySlug;
        return new ParsedSearchDto(text, query, placeText, place, categorySlug, subSlug);
    }

    private async Task<SearchPlaceMatchDto?> MatchPlaceAsync(string placeText, string? hintCity, CancellationToken ct)
    {
        var cities = (await reference.ActiveCitiesAsync(ct)).Select(c => new CityRow(c.Id, c.Slug, c.Name, c.AltNames)).ToList();
        CityRow? ByName(string n)
        {
            var compact = n.Replace(" ", "");
            return n.Length == 0 ? null : cities.FirstOrDefault(c => Norm(c.Name) == n || c.Slug == Slug(n) || Norm(c.Name).Replace(" ", "") == compact);
        }
        CityRow? ByAltName(string n) => n.Length == 0 ? null
            : cities.FirstOrDefault(c => (c.AltNames ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Any(alt => Norm(alt) == n));
        CityRow? FindCity(string s) => ByName(Norm(s)) ?? ByAltName(Norm(s));

        // "Madhapur, Hyderabad" or "Madhapur Hyderabad": an area followed by its city. Otherwise the whole text is a city or an area.
        var parts = placeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = new List<(string Area, CityRow? City)>();
        if (parts.Length >= 2 && FindCity(parts[^1]) is { } named) candidates.Add((string.Join(", ", parts[..^1]), named));
        else
        {
            var whole = string.Join(" ", parts);
            if (ByName(Norm(whole)) is { } city) return new SearchPlaceMatchDto(city.Slug, city.Name, null, null, "city");
            // A listed area beats a city's other name: "Secunderabad" is an area of Hyderabad as well as one of its names.
            if (await MatchAreaAsync(whole, null, cities, hintCity, ct) is { } wholeArea) return wholeArea;
            if (ByAltName(Norm(whole)) is { } aliasCity) return new SearchPlaceMatchDto(aliasCity.Slug, aliasCity.Name, null, null, "altName");
            var words = whole.Split(' ');
            for (var k = Math.Min(2, words.Length - 1); k >= 1; k--)
                if (FindCity(string.Join(' ', words[^k..])) is { } suffixCity) candidates.Add((string.Join(' ', words[..^k]), suffixCity));
        }

        foreach (var (areaText, city) in candidates)
            if (await MatchAreaAsync(areaText, city, cities, hintCity, ct) is { } area) return area;
        // An area that isn't listed in a named city ("Gachibowli Pune"): still select the city.
        return candidates.FirstOrDefault().City is { } fallback ? new SearchPlaceMatchDto(fallback.Slug, fallback.Name, null, null, "city") : null;
    }

    private async Task<SearchPlaceMatchDto?> MatchAreaAsync(string areaText, CityRow? city, List<CityRow> cities, string? hintCity, CancellationToken ct)
    {
        var name = areaText.Trim();
        var n = Norm(name);
        if (n.Length < 2) return null;
        var slug = Slug(n);
        var cityIds = city is null ? cities.Select(c => c.Id).ToList() : [city.Id];

        // SQL Server's default collation compares case-insensitively; alternate spellings are confirmed below.
        var rows = await uow.Repository<Area>().QueryNoTracking()
            .Where(a => a.IsActive && cityIds.Contains(a.CityId)
                        && (a.Name == name || a.Slug == slug || a.Pincode == name || (a.AltNames != null && a.AltNames.Contains(name))))
            .Select(a => new { a.Id, a.Name, a.Slug, a.Pincode, a.AltNames, a.CityId, a.ParentAreaId, ParentName = a.ParentArea != null ? a.ParentArea.Name : null })
            .Take(50).ToListAsync(ct);

        var best = rows
            .Select(a => new
            {
                Row = a,
                By = Norm(a.Name) == n || a.Slug == slug ? "area"
                    : a.Pincode == name ? "pincode"
                    : (a.AltNames ?? "").Split('|', ';').Any(alt => Norm(alt) == n) ? "altName" : null,
            })
            .Where(x => x.By is not null)
            .OrderBy(x => x.By == "area" ? 0 : x.By == "altName" ? 1 : 2)
            .ThenBy(x => cities.FirstOrDefault(c => c.Id == x.Row.CityId)?.Slug == hintCity ? 0 : 1)
            .ThenBy(x => x.Row.ParentAreaId is null ? 0 : 1)
            .ThenBy(x => cities.FindIndex(c => c.Id == x.Row.CityId))
            .FirstOrDefault();
        if (best is null) return null;
        var areaCity = cities.First(c => c.Id == best.Row.CityId);
        // The dropdown lists top-level areas; a neighbourhood selects the area it belongs to.
        return best.Row.ParentAreaId is { } parentId
            ? new SearchPlaceMatchDto(areaCity.Slug, areaCity.Name, parentId, best.Row.ParentName, best.By!)
            : new SearchPlaceMatchDto(areaCity.Slug, areaCity.Name, best.Row.Id, best.Row.Name, best.By!);
    }

    /// <summary>A sub-category or category whose name is the query (ignoring case and simple plurals): "lawyer" or "Lawyers" gives Lawyers.</summary>
    private async Task<(string? Category, string? Sub)> MatchCatalogueAsync(string query, CancellationToken ct)
    {
        var q = Stem(Norm(query));
        if (q.Length < 3) return (null, null);
        var subs = await uow.Repository<SubCategory>().QueryNoTracking().Where(s => s.IsActive && s.Category.IsActive)
            .Select(s => new { s.Slug, s.Name }).ToListAsync(ct);
        if (subs.FirstOrDefault(s => Stem(Norm(s.Name)) == q) is { } sub) return (null, sub.Slug);
        var categories = await uow.Repository<Category>().QueryNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Slug, c.Name }).ToListAsync(ct);
        return categories.FirstOrDefault(c => Stem(Norm(c.Name)) == q) is { } cat ? (cat.Slug, null) : (null, null);
    }

    /// <summary>Lower case, letters and digits only, single spaces: "Madhapur," and "madhapur" compare equal.</summary>
    private static string Norm(string s) =>
        Regex.Replace(Regex.Replace(s.ToLowerInvariant().Replace("&", " and "), @"[^\p{L}\p{N}]+", " "), @"\s+", " ").Trim();

    private static string Slug(string normalized) => normalized.Replace(' ', '-');

    /// <summary>Drops simple plural endings word by word: "ac repairs" and "ac repair" compare equal.</summary>
    private static string Stem(string normalized) => string.Join(' ', normalized.Split(' ').Select(w =>
        w.Length > 4 && w.EndsWith("ies") ? w[..^3] + "y" : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w));

    private sealed record CityRow(Guid Id, string Slug, string Name, string? AltNames);
}
