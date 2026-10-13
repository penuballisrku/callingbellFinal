using System.Globalization;
using System.Text;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallingBell.Application.Features.Seo;

/// <summary>
/// /llms.txt and /llms-full.txt (llmstxt.org): a plain Markdown guide for AI assistants and answer engines (ChatGPT, Claude, Perplexity,
/// Gemini, Copilot) to what CallInBell is, how its pages and search URLs work, and what is listed: categories and cities with their
/// canonical pages and real counts, and in the full file every listed business with its category, place, rating and profile URL.
/// Built from the database (only canonical, indexable pages, as in the sitemaps), cached with the other SEO data and cleared with it
/// when listings change. Contact details stay on the business pages; nothing private is included.
/// </summary>
public sealed class LlmsTxtService(IUnitOfWork uow, IOptions<SeoOptions> options, SeoCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    /// <summary>The full file stays a manageable size for a model's context; the sitemaps list everything.</summary>
    private const int MaxBusinesses = 2000;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private SeoOptions O => options.Value;
    private string Url(string path) => SeoPaths.Absolute(O.SiteUrl, path);

    private sealed record CategoryRow(string Name, string Slug, int Count, List<(string Name, string Slug, int Count)> Subs);
    private sealed record CityRow(string Name, string State, string Path, int Count);
    private sealed record Catalogue(int Businesses, int Reviews, List<CategoryRow> Categories, List<CityRow> Cities, List<string> Countries);

    public Task<string> SummaryAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync("llms|summary", Lifetime, async () => Build(await CatalogueAsync(ct), null));

    public Task<string> FullAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync("llms|full", Lifetime, async () => Build(await CatalogueAsync(ct), await BusinessesAsync(ct)));

    private Task<Catalogue> CatalogueAsync(CancellationToken ct) => cache.GetOrCreateAsync("llms|catalogue", Lifetime, async () =>
    {
        var listed = uow.Repository<Business>().QueryNoTracking().Listed();
        var total = await listed.CountAsync(ct);
        var reviews = await listed.SumAsync(b => (int?)b.ReviewCount, ct) ?? 0;

        var bySub = await listed.GroupBy(b => new
            {
                Cat = b.Category.Name, CatSlug = b.Category.Slug, CatOrder = b.Category.SortOrder,
                Sub = b.SubCategory != null ? b.SubCategory.Name : null, SubSlug = b.SubCategory != null ? b.SubCategory.Slug : null,
            })
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var categories = bySub.GroupBy(x => new { x.Key.Cat, x.Key.CatSlug, x.Key.CatOrder })
            .Select(g => new CategoryRow(g.Key.Cat, g.Key.CatSlug, g.Sum(x => x.Count),
                g.Where(x => x.Key.SubSlug != null && x.Count >= O.MinBusinessesForIndex)
                    .OrderByDescending(x => x.Count).ThenBy(x => x.Key.Sub).Select(x => (x.Key.Sub!, x.Key.SubSlug!, x.Count)).ToList()))
            .Where(c => c.Count >= O.MinBusinessesForIndex)
            .OrderBy(c => bySub.First(x => x.Key.CatSlug == c.Slug).Key.CatOrder).ThenBy(c => c.Name).ToList();

        // Active cities only, as in the sitemaps: a switched-off city's page answers 404.
        var byCity = await listed.Where(b => b.CityRef != null && b.CityRef.IsActive)
            .GroupBy(b => new { City = b.CityRef!.Name, CitySlug = b.CityRef.Slug, State = b.CityRef.State.Name, StateSlug = b.CityRef.State.Slug, b.CityRef.State.CountryCode })
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var cities = byCity.Where(c => c.Count >= O.MinBusinessesForIndex).OrderByDescending(c => c.Count).ThenBy(c => c.Key.City)
            .Select(c => new CityRow(c.Key.City, c.Key.State, SeoPaths.Location(SeoPaths.CountrySlug(c.Key.CountryCode), c.Key.StateSlug, c.Key.CitySlug), c.Count))
            .ToList();
        var countries = byCity.Select(c => SeoPaths.CountryName(c.Key.CountryCode)).Distinct().Order().ToList();
        return new Catalogue(total, reviews, categories, cities, countries);
    });

    private async Task<List<string>> BusinessesAsync(CancellationToken ct)
    {
        var rows = await uow.Repository<Business>().QueryNoTracking().Listed()
            .OrderByDescending(b => b.ReviewCount).ThenBy(b => b.Name)
            .Take(MaxBusinesses)
            .Select(b => new
            {
                b.Name, b.Slug, b.Tagline, Category = b.SubCategory != null ? b.SubCategory.Name : b.Category.Name, b.Area, b.City,
                State = b.CityRef != null ? b.CityRef.State.Name : null, b.AverageRating, b.ReviewCount,
                b.AcceptsOnlineBooking, b.OffersHomeService, b.OffersVideoConsultation, Verified = b.VerifiedOn != null,
                Services = b.Services.Where(s => s.IsActive).OrderByDescending(s => s.IsPopular).ThenBy(s => s.Name).Select(s => s.Name).Take(6).ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(b =>
        {
            var place = string.Join(", ", new[] { b.Area, b.City, b.State }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            var facts = new List<string> { $"{b.Category} in {place}" };
            if (b.ReviewCount > 0) facts.Add(string.Create(Invariant, $"rated {b.AverageRating:0.0}/5 from {b.ReviewCount:N0} reviews"));
            if (b.Verified) facts.Add("verified");
            var offers = new[] { b.AcceptsOnlineBooking ? "online booking" : null, b.OffersHomeService ? "home visits" : null, b.OffersVideoConsultation ? "video consultation" : null }
                .Where(x => x is not null).ToList();
            if (offers.Count > 0) facts.Add(string.Join(", ", offers));
            var line = $"- [{Md(b.Name)}]({Url(SeoPaths.Business(b.Slug))}): {string.Join("; ", facts)}.";
            if (!string.IsNullOrWhiteSpace(b.Tagline)) line += $" {Md(b.Tagline.Trim().TrimEnd('.'))}.";
            if (b.Services.Count > 0) line += $" Services: {Md(string.Join(", ", b.Services))}.";
            return line;
        }).ToList();
    }

    private string Build(Catalogue c, List<string>? businesses)
    {
        var site = O.SiteName;
        var sb = new StringBuilder();
        sb.AppendLine($"# {site}").AppendLine();
        var where = c.Countries.Count > 0 ? $" in {string.Join(", ", c.Countries)}" : "";
        sb.AppendLine(string.Create(Invariant,
            $"> {site} is a real-time local business directory and booking platform{where}: find local businesses and service providers, see who is available right now, compare reviews, send enquiries, request quotes and book services. It lists {c.Businesses:N0} businesses in {c.Cities.Count:N0} cities across {c.Categories.Count:N0} categories, with {c.Reviews:N0} customer reviews."))
            .AppendLine();
        sb.AppendLine("Every public page is server-rendered HTML with schema.org structured data (LocalBusiness with address, geo, opening hours and aggregate rating; BreadcrumbList; ItemList; FAQPage). Ratings and counts come from the live database. Business pages show contact details, services, hours and reviews.")
            .AppendLine();

        sb.AppendLine("## How to find businesses").AppendLine();
        var exampleCity = c.Cities.FirstOrDefault();
        var exampleCat = c.Categories.SelectMany(x => x.Subs).FirstOrDefault();
        sb.AppendLine($"- Category pages, listed businesses for a service: `{Url("/category/{category-slug}")}`" + (exampleCat.Slug is null ? "" : $", e.g. [{Md(exampleCat.Name)}]({Url(SeoPaths.Category(exampleCat.Slug))})"));
        sb.AppendLine($"- Location pages, businesses in a place (add a category slug for both): `{Url("/location/{country}/{state}/{city}[/{area}][/{category-slug}]")}`" + (exampleCity is null ? "" : $", e.g. [{Md(exampleCity.Name)}]({Url(exampleCity.Path)})"));
        sb.AppendLine($"- Business profiles: `{Url("/business/{business-slug}")}`");
        sb.AppendLine($"- Search (any words, optional city slug): `{Url("/search?q={what}&city={city-slug}")}`. Search pages are for people and are not indexed; link to category, location and business pages instead.");
        sb.AppendLine($"- [All categories]({Url("/categories")}) · [Sitemap]({Url("/sitemap.xml")})").AppendLine();

        sb.AppendLine("## Categories").AppendLine();
        foreach (var cat in c.Categories)
        {
            var subs = cat.Subs.Take(12).Select(s => $"[{Md(s.Name)}]({Url(SeoPaths.Category(s.Slug))}) ({s.Count})");
            sb.AppendLine(string.Create(Invariant, $"- [{Md(cat.Name)}]({Url(SeoPaths.Category(cat.Slug))}): {cat.Count:N0} {(cat.Count == 1 ? "business" : "businesses")}")
                + (cat.Subs.Count > 0 ? $". {string.Join(", ", subs)}" : ""));
        }
        sb.AppendLine();

        sb.AppendLine("## Cities").AppendLine();
        foreach (var city in c.Cities)
            sb.AppendLine(string.Create(Invariant, $"- [{Md(city.Name)}, {Md(city.State)}]({Url(city.Path)}): {city.Count:N0} {(city.Count == 1 ? "business" : "businesses")}"));
        sb.AppendLine();

        sb.AppendLine("## For businesses").AppendLine();
        sb.AppendLine($"- [List your business]({Url("/list-your-business")}): free listing, then optional paid plans");
        sb.AppendLine($"- [Plans and pricing]({Url("/pricing")})").AppendLine();

        sb.AppendLine("## About").AppendLine();
        sb.AppendLine($"- [About {site}]({Url("/about")})");
        sb.AppendLine($"- [Trust and safety]({Url("/trust-and-safety")})");
        sb.AppendLine($"- [Contact support]({Url("/support")})").AppendLine();

        if (businesses is null)
        {
            sb.AppendLine("## Optional").AppendLine();
            sb.AppendLine($"- [Full directory]({Url("/llms-full.txt")}): every listed business with its category, place, rating and profile link");
        }
        else
        {
            sb.AppendLine(string.Create(Invariant, $"## Businesses ({businesses.Count:N0}{(businesses.Count < c.Businesses ? $" of {c.Businesses:N0}, most reviewed first; the sitemap lists all" : "")})")).AppendLine();
            foreach (var line in businesses) sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>Text inside Markdown link labels and lines: brackets and line breaks would break the format.</summary>
    private static string Md(string text) => text.Replace('[', '(').Replace(']', ')').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
