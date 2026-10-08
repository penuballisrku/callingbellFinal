using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallingBell.Application.Features.Seo;

/// <summary>
/// A category or location landing page (/category/{slug}, /location/{country}/{state}/{city}/{area}/{category}): its listed businesses,
/// a factual summary, questions answered from the data, and links to the places and categories around it. <paramref name="Indexable"/>
/// is false when too few businesses are listed for the page to be useful (it is then noindex and left out of the sitemap).
/// </summary>
public sealed record LandingPageDto(string Kind, string Path, string Title, string Description, string Heading, string Summary,
    string? Place, string? CategoryName, int BusinessCount, bool Indexable, IReadOnlyList<SeoLink> Breadcrumbs,
    IReadOnlyList<BusinessCardDto> Businesses, IReadOnlyList<SeoLinkGroup> Links, IReadOnlyList<SeoFaq> Faq);

/// <summary>The page, or where its canonical address is (a city under the wrong state), or nothing (unknown place or category).</summary>
public sealed record LandingResult(LandingPageDto? Page, string? RedirectTo);

public sealed record GetLandingPageQuery(string Path) : IRequest<LandingResult>;

public sealed class GetLandingPageHandler(IUnitOfWork uow, IOptions<SeoOptions> options, SeoCache cache)
    : IRequestHandler<GetLandingPageQuery, LandingResult>
{
    private const int ShowBusinesses = 24;
    private const int ShowLinks = 40;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly LandingResult NotFound = new(null, null);

    public Task<LandingResult> Handle(GetLandingPageQuery request, CancellationToken ct)
    {
        var path = request.Path.Trim().TrimEnd('/').ToLowerInvariant();
        return cache.GetOrCreateAsync($"landing|{path}", Lifetime, () => BuildAsync(path, ct));
    }

    private sealed record SubRef(Guid Id, string Slug, string Name, Guid CategoryId, string CategorySlug, string CategoryName);
    private sealed record CatRef(Guid Id, string Slug, string Name);
    private sealed record Catalog(IReadOnlyList<SubRef> Subs, IReadOnlyList<CatRef> Cats, IReadOnlyDictionary<string, string> CountryBySlug);

    /// <summary>A category the page filters by: a sub-category ("Electricians") or a whole category ("Home Services").</summary>
    private sealed record Topic(string Slug, string Name, SubRef? Sub, CatRef? Cat);

    private Task<Catalog> CatalogAsync(CancellationToken ct) => cache.GetOrCreateAsync("landing-catalog", TimeSpan.FromHours(1), async () =>
    {
        var subs = await uow.Repository<SubCategory>().QueryNoTracking().Where(s => s.IsActive && s.Category.IsActive)
            .Select(s => new SubRef(s.Id, s.Slug, s.Name, s.CategoryId, s.Category.Slug, s.Category.Name)).ToListAsync(ct);
        var cats = await uow.Repository<Category>().QueryNoTracking().Where(c => c.IsActive).Select(c => new CatRef(c.Id, c.Slug, c.Name)).ToListAsync(ct);
        var countries = await uow.Repository<State>().QueryNoTracking().Where(s => s.IsActive).Select(s => s.CountryCode).Distinct().ToListAsync(ct);
        var bySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in countries) bySlug.TryAdd(SeoPaths.CountrySlug(code), code.Trim().ToUpperInvariant());
        return new Catalog(subs, cats, bySlug);
    });

    private static Topic? FindTopic(Catalog catalog, string slug) =>
        catalog.Subs.FirstOrDefault(s => s.Slug == slug) is { } sub ? new Topic(sub.Slug, sub.Name, sub, null)
        : catalog.Cats.FirstOrDefault(c => c.Slug == slug) is { } cat ? new Topic(cat.Slug, cat.Name, null, cat)
        : null;

    private async Task<LandingResult> BuildAsync(string path, CancellationToken ct)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && segments[0] == "category") return await CategoryAsync(segments[1], ct);
        if (segments.Length is >= 2 and <= 6 && segments[0] == "location") return await LocationAsync(segments[1..], ct);
        return NotFound;
    }

    // ---------------- /category/{slug} ----------------

    private async Task<LandingResult> CategoryAsync(string slug, CancellationToken ct)
    {
        var catalog = await CatalogAsync(ct);
        if (FindTopic(catalog, slug) is not { } topic) return NotFound;
        var o = options.Value;
        var q = ByTopic(Listed(), topic);
        var count = await q.CountAsync(ct);
        var businesses = await TopAsync(q, ct);

        // Where it is offered: cities by number of listings, linked to their city + category page.
        var cityCounts = await q.Where(b => b.CityId != null).GroupBy(b => b.CityId!.Value)
            .Select(g => new { CityId = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(ShowLinks).ToListAsync(ct);
        var cities = await CityRefsAsync(cityCounts.Select(x => x.CityId), ct);
        var listedCities = cityCounts.Where(x => cities.ContainsKey(x.CityId)).Select(x => (City: cities[x.CityId], x.Count)).ToList();
        var cityLinks = listedCities.Select(x => new SeoLink($"{topic.Name} in {x.City.Name}",
            SeoPaths.Location(SeoPaths.CountrySlug(x.City.CountryCode), x.City.StateSlug, x.City.Slug, topic.Slug), x.Count)).ToList();

        // Related categories: the other sub-categories of the same category (or this category's sub-categories).
        var categoryId = topic.Sub?.CategoryId ?? topic.Cat!.Id;
        var related = catalog.Subs.Where(s => s.CategoryId == categoryId && s.Slug != topic.Slug).ToList();
        var relatedCounts = await CountBySubAsync(Listed().Where(b => b.CategoryId == categoryId), ct);
        var relatedLinks = related.Where(s => relatedCounts.GetValueOrDefault(s.Id) > 0)
            .OrderByDescending(s => relatedCounts[s.Id]).Select(s => new SeoLink(s.Name, SeoPaths.Category(s.Slug), relatedCounts[s.Id])).Take(ShowLinks).ToList();

        var links = new List<SeoLinkGroup>();
        if (cityLinks.Count > 0) links.Add(new SeoLinkGroup($"{topic.Name} by city", cityLinks));
        if (relatedLinks.Count > 0) links.Add(new SeoLinkGroup(topic.Sub is null ? $"Services in {topic.Name}" : "Related services", relatedLinks));

        var faq = new List<SeoFaq>();
        if (cityLinks.Count > 0)
            faq.Add(new SeoFaq($"In which cities are {topic.Name.ToLowerInvariant()} listed on {o.SiteName}?",
                $"{topic.Name} are listed in {SeoText.JoinList(listedCities.Take(8).Select(x => $"{x.City.Name} ({x.Count})").ToList())}" +
                (listedCities.Count > 8 ? $", and {listedCities.Count - 8} more cities." : ".")));
        AddContactFaq(faq, topic.Name, null, o.SiteName, count);
        AddTopRatedFaq(faq, topic.Name, null, businesses);

        var crumbs = new List<SeoLink> { new("Home", "/"), new("Categories", "/categories") };
        if (topic.Sub is { } sub) crumbs.Add(new SeoLink(sub.CategoryName, SeoPaths.Category(sub.CategorySlug)));
        crumbs.Add(new SeoLink(topic.Name, SeoPaths.Category(topic.Slug)));

        var summary = count == 0
            ? $"No {topic.Name.ToLowerInvariant()} are listed on {o.SiteName} yet."
            : $"{Listings(count)} under {topic.Name} on {o.SiteName}" + (cityLinks.Count > 0 ? $", in {Plural(cityCounts.Count, "city", "cities")}." : ".");
        return new LandingResult(new LandingPageDto("category", SeoPaths.Category(topic.Slug), SeoText.CategoryTitle(topic.Name, null, o.SiteName),
            SeoText.CategoryDescription(topic.Name, null, count, o.SiteName), topic.Name, summary + TopRatedSentence(businesses), null, topic.Name, count,
            count >= o.MinBusinessesForIndex, crumbs, businesses, links, faq), null);
    }

    // ---------------- /location/{country}/{state}/{city}/{area}/{category} ----------------

    private async Task<LandingResult> LocationAsync(string[] seg, CancellationToken ct)
    {
        var o = options.Value;
        var catalog = await CatalogAsync(ct);
        if (!catalog.CountryBySlug.TryGetValue(seg[0], out var countryCode)) return NotFound;
        var countrySlug = SeoPaths.CountrySlug(countryCode);
        var countryName = SeoPaths.CountryName(countryCode);

        StateInfo? state = null;
        CityInfo? city = null;
        AreaInfo? area = null;
        Topic? topic = null;
        if (seg.Length >= 2)
        {
            state = await uow.Repository<State>().QueryNoTracking().Where(s => s.Slug == seg[1] && s.IsActive)
                .Select(s => new StateInfo(s.Id, s.Slug, s.Name, s.CountryCode)).FirstOrDefaultAsync(ct);
            if (state is null) return NotFound;
        }
        if (seg.Length >= 3)
        {
            city = await uow.Repository<City>().QueryNoTracking().Where(c => c.Slug == seg[2] && c.IsActive)
                .Select(c => new CityInfo(c.Id, c.Slug, c.Name, c.StateId, c.State.Slug, c.State.Name, c.State.CountryCode)).FirstOrDefaultAsync(ct);
            if (city is null) return NotFound;
        }
        // The 4th segment is a category or an area of the city; a 5th is a category within that area.
        if (seg.Length >= 4)
        {
            topic = FindTopic(catalog, seg[3]);
            if (topic is null)
            {
                area = await uow.Repository<Area>().QueryNoTracking().Where(a => a.CityId == city!.Id && a.Slug == seg[3] && a.IsActive)
                    .Select(a => new AreaInfo(a.Id, a.Slug, a.Name)).FirstOrDefaultAsync(ct);
                if (area is null) return NotFound;
            }
            else if (seg.Length > 4) return NotFound;
        }
        if (seg.Length == 5)
        {
            topic = FindTopic(catalog, seg[4]);
            if (topic is null) return NotFound;
        }
        if (seg.Length > 5) return NotFound;

        // One address per page: a state under another country, or a city under another state, redirects to where it belongs.
        if (city is not null && (city.StateSlug != state!.Slug || !Same(city.CountryCode, countryCode)))
            return new LandingResult(null, SeoPaths.Location(SeoPaths.CountrySlug(city.CountryCode), city.StateSlug, city.Slug, area?.Slug, topic?.Slug));
        if (state is not null && !Same(state.CountryCode, countryCode))
            return new LandingResult(null, SeoPaths.Location(SeoPaths.CountrySlug(state.CountryCode), state.Slug));

        var path = SeoPaths.Location(countrySlug, state?.Slug, city?.Slug, area?.Slug, topic?.Slug);
        var place = area is not null ? $"{area.Name}, {city!.Name}"
            : city is not null ? SeoText.Place(city.Name, city.StateName)
            : state is not null ? $"{state.Name}, {countryName}"
            : countryName;

        var q = Listed();
        if (area is not null) q = q.Where(b => b.AreaId == area.Id);
        else if (city is not null) q = q.Where(b => b.CityId == city.Id);
        else if (state is not null) q = q.Where(b => b.CityRef != null && b.CityRef.StateId == state.Id);
        else q = q.Where(b => b.CityRef != null && b.CityRef.State.CountryCode == countryCode);
        var scoped = q;
        if (topic is not null) q = ByTopic(q, topic);

        var count = await q.CountAsync(ct);
        var businesses = await TopAsync(q, ct);
        var links = new List<SeoLinkGroup>();
        var faq = new List<SeoFaq>();
        var bySub = await CountBySubAsync(topic is null ? q : scoped, ct);
        var subLinks = catalog.Subs.Where(s => bySub.GetValueOrDefault(s.Id) > 0 && s.Slug != topic?.Slug)
            .OrderByDescending(s => bySub[s.Id]).Take(ShowLinks).ToList();

        if (city is null)
        {
            // Country or state: its states or cities with listings.
            if (state is null)
            {
                var states = await q.Where(b => b.CityRef != null).GroupBy(b => new { b.CityRef!.State.Slug, b.CityRef.State.Name })
                    .Select(g => new { g.Key.Slug, g.Key.Name, Count = g.Count() }).OrderByDescending(x => x.Count).Take(ShowLinks).ToListAsync(ct);
                if (states.Count > 0)
                    links.Add(new SeoLinkGroup($"States and regions in {countryName}",
                        states.Select(s => new SeoLink(s.Name, SeoPaths.Location(countrySlug, s.Slug), s.Count)).ToList()));
            }
            else
            {
                var cityCounts = await q.Where(b => b.CityId != null).GroupBy(b => b.CityId!.Value)
                    .Select(g => new { CityId = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(ShowLinks).ToListAsync(ct);
                var refs = await CityRefsAsync(cityCounts.Select(x => x.CityId), ct);
                var cityLinks = cityCounts.Where(x => refs.ContainsKey(x.CityId))
                    .Select(x => new SeoLink(refs[x.CityId].Name, SeoPaths.Location(countrySlug, state.Slug, refs[x.CityId].Slug), x.Count)).ToList();
                if (cityLinks.Count > 0)
                {
                    links.Add(new SeoLinkGroup($"Cities in {state.Name}", cityLinks));
                    faq.Add(new SeoFaq($"Which cities in {state.Name} have businesses listed on {o.SiteName}?",
                        $"{SeoText.JoinList(cityLinks.Take(8).Select(l => $"{l.Name} ({l.Count})").ToList())}" + (cityLinks.Count > 8 ? $", and {cityLinks.Count - 8} more." : ".")));
                }
            }
        }
        else
        {
            // City or area: its areas (for the category, when there is one) and the other categories here.
            if (area is null)
            {
                var areaCounts = await q.Where(b => b.AreaId != null).GroupBy(b => b.AreaId!.Value)
                    .Select(g => new { AreaId = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(ShowLinks).ToListAsync(ct);
                var ids = areaCounts.Select(x => x.AreaId).ToList();
                var names = await uow.Repository<Area>().QueryNoTracking().Where(a => ids.Contains(a.Id) && a.IsActive)
                    .Select(a => new { a.Id, a.Slug, a.Name }).ToDictionaryAsync(a => a.Id, ct);
                var listedAreas = areaCounts.Where(x => names.ContainsKey(x.AreaId)).Select(x => (Area: names[x.AreaId], x.Count)).ToList();
                var areaLinks = listedAreas.Select(x => new SeoLink(topic is null ? x.Area.Name : $"{topic.Name} in {x.Area.Name}",
                    SeoPaths.Location(countrySlug, city.StateSlug, city.Slug, x.Area.Slug, topic?.Slug), x.Count)).ToList();
                if (areaLinks.Count > 0)
                {
                    links.Add(new SeoLinkGroup(topic is null ? $"Areas of {city.Name}" : $"{topic.Name} by area of {city.Name}", areaLinks));
                    faq.Add(new SeoFaq(topic is null ? $"Which areas of {city.Name} have the most businesses listed?"
                            : $"Which areas of {city.Name} have {topic.Name.ToLowerInvariant()}?",
                        SeoText.JoinList(listedAreas.Take(8).Select(x => $"{x.Area.Name} ({x.Count})").ToList()) + "."));
                }
            }
            else if (topic is not null)
            {
                links.Add(new SeoLinkGroup($"More in {city.Name}", [
                    new SeoLink($"{topic.Name} in {city.Name}", SeoPaths.Location(countrySlug, city.StateSlug, city.Slug, topic.Slug)),
                    new SeoLink($"Local services in {area.Name}", SeoPaths.Location(countrySlug, city.StateSlug, city.Slug, area.Slug)),
                ]));
            }
        }
        if (subLinks.Count > 0)
        {
            var groupTitle = topic is null ? $"Services in {place}" : $"Other services in {place}";
            links.Add(new SeoLinkGroup(groupTitle, subLinks.Select(s => new SeoLink(s.Name,
                city is null ? SeoPaths.Category(s.Slug) : SeoPaths.Location(countrySlug, city.StateSlug, city.Slug, area?.Slug, s.Slug), bySub[s.Id])).ToList()));
            if (topic is null)
                faq.Add(new SeoFaq($"What services are listed in {place}?",
                    SeoText.JoinList(subLinks.Take(8).Select(s => $"{s.Name} ({bySub[s.Id]})").ToList()) + (subLinks.Count > 8 ? $", and {subLinks.Count - 8} more." : ".")));
        }
        if (topic is not null) links.Add(new SeoLinkGroup($"{topic.Name} elsewhere", [new SeoLink($"All {topic.Name.ToLowerInvariant()}", SeoPaths.Category(topic.Slug))]));
        AddContactFaq(faq, topic?.Name ?? "businesses", place, o.SiteName, count);
        AddTopRatedFaq(faq, topic?.Name ?? "businesses", place, businesses);

        var crumbs = new List<SeoLink> { new("Home", "/"), new(countryName, SeoPaths.Location(countrySlug)) };
        if (state is not null) crumbs.Add(new SeoLink(state.Name, SeoPaths.Location(countrySlug, state.Slug)));
        if (city is not null) crumbs.Add(new SeoLink(city.Name, SeoPaths.Location(countrySlug, city.StateSlug, city.Slug)));
        if (area is not null) crumbs.Add(new SeoLink(area.Name, SeoPaths.Location(countrySlug, city!.StateSlug, city.Slug, area.Slug)));
        if (topic is not null) crumbs.Add(new SeoLink(topic.Name, path));

        var heading = topic is not null ? $"{topic.Name} in {place}" : $"Local businesses and services in {place}";
        var what = topic?.Name.ToLowerInvariant() ?? "businesses";
        var summary = count == 0
            ? $"No {what} are listed on {o.SiteName} in {place} yet."
            : $"{Listings(count)}{(topic is null ? "" : $" under {topic.Name}")} on {o.SiteName} in {place}"
              + (topic is null && bySub.Count > 0 ? $", across {Plural(bySub.Count, "category", "categories")}." : ".");
        var title = topic is not null ? SeoText.CategoryTitle(topic.Name, place, o.SiteName) : SeoText.LocationTitle(place, o.SiteName);
        var description = topic is not null ? SeoText.CategoryDescription(topic.Name, place, count, o.SiteName)
            : SeoText.LocationDescription(place, count, bySub.Count, o.SiteName);
        return new LandingResult(new LandingPageDto("location", path, title, description, heading, summary + TopRatedSentence(businesses), place,
            topic?.Name, count, count >= o.MinBusinessesForIndex, crumbs, businesses, links, faq), null);
    }

    // ---------------- Helpers ----------------

    private sealed record StateInfo(Guid Id, string Slug, string Name, string CountryCode);
    private sealed record CityInfo(Guid Id, string Slug, string Name, Guid StateId, string StateSlug, string StateName, string CountryCode);
    private sealed record AreaInfo(Guid Id, string Slug, string Name);
    private sealed record CityRef(string Slug, string Name, string StateSlug, string CountryCode);

    private IQueryable<Business> Listed() => uow.Repository<Business>().QueryNoTracking().Listed();

    private static IQueryable<Business> ByTopic(IQueryable<Business> q, Topic topic) =>
        topic.Sub is { } sub ? q.Where(b => b.SubCategoryId == sub.Id) : q.Where(b => b.CategoryId == topic.Cat!.Id);

    /// <summary>The businesses shown: featured first, then by rating and number of reviews.</summary>
    private static Task<List<BusinessCardDto>> TopAsync(IQueryable<Business> q, CancellationToken ct) =>
        q.OrderByDescending(b => b.IsFeatured).ThenByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount).ThenBy(b => b.Name)
            .Take(ShowBusinesses).Select(BusinessCards.ToCard(IndianTime.Now)).ToListAsync(ct);

    private static async Task<Dictionary<Guid, int>> CountBySubAsync(IQueryable<Business> q, CancellationToken ct) =>
        await q.Where(b => b.SubCategoryId != null).GroupBy(b => b.SubCategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);

    private async Task<Dictionary<Guid, CityRef>> CityRefsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return await uow.Repository<City>().QueryNoTracking().Where(c => list.Contains(c.Id))
            .Select(c => new { c.Id, Ref = new CityRef(c.Slug, c.Name, c.State.Slug, c.State.CountryCode) })
            .ToDictionaryAsync(x => x.Id, x => x.Ref, ct);
    }

    private static void AddContactFaq(List<SeoFaq> faq, string what, string? place, string site, int count)
    {
        if (count == 0) return;
        faq.Add(new SeoFaq($"How can I contact {what.ToLowerInvariant()}{(place is null ? "" : $" in {place}")} on {site}?",
            "Each business page shows the contact details the business has provided, such as its phone number, address and opening hours, " +
            "and lets you send an enquiry, request a quote or ask for a callback."));
    }

    private static void AddTopRatedFaq(List<SeoFaq> faq, string what, string? place, IReadOnlyList<BusinessCardDto> businesses)
    {
        var rated = businesses.Where(b => b.ReviewCount > 0).OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount).Take(3).ToList();
        if (rated.Count == 0) return;
        faq.Add(new SeoFaq($"Which {what.ToLowerInvariant()}{(place is null ? "" : $" in {place}")} have the highest customer ratings?",
            SeoText.JoinList(rated.Select(b => $"{b.Name} ({b.AverageRating:0.0} out of 5 from {Plural(b.ReviewCount, "review", "reviews")})").ToList()) + "."));
    }

    private static string TopRatedSentence(IReadOnlyList<BusinessCardDto> businesses)
    {
        var top = businesses.Where(b => b.ReviewCount > 0).OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount).FirstOrDefault();
        return top is null ? "" : $" The highest rated is {top.Name}, at {top.AverageRating:0.0} out of 5 from {Plural(top.ReviewCount, "review", "reviews")}.";
    }

    private static string Listings(int count) => count == 1 ? "1 business is listed" : $"{count} businesses are listed";
    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
