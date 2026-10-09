using System.Globalization;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Catalog;
using CallingBell.Application.Features.Content;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallingBell.Application.Features.Seo;

/// <summary>
/// One place that decides, for any public URL, what to answer (status, redirect) and how the page describes itself to search and answer
/// engines: title, description, canonical URL, robots, Open Graph / Twitter, JSON-LD and breadcrumbs, plus the visible, data-backed
/// summary, questions and links. The server-rendered HTML and the React app both use it, so the rules live here only.
/// </summary>
public interface ISeoMetadataService
{
    /// <param name="path">The URL path ("/business/mr-electric-san-antonio").</param>
    /// <param name="query">The query string, used only to describe search pages (never part of a canonical URL).</param>
    Task<SeoPage> ResolveAsync(string path, string? query, CancellationToken ct);
}

public sealed class SeoMetadataService(ISender sender, IUnitOfWork uow, IOptions<SeoOptions> options, SeoCache cache) : ISeoMetadataService
{
    private const string Index = "index,follow,max-image-preview:large,max-snippet:-1";
    private const string NoIndexFollow = "noindex,follow";
    private const string NoIndexNoFollow = "noindex,nofollow";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Owner dashboard sections that used to live under /business/ (now /owner/).</summary>
    private static readonly HashSet<string> OwnerSections =
        ["setup", "leads", "bookings", "reviews", "services", "profile", "media", "plan", "advertising", "settings"];

    /// <summary>Public pages with fixed titles; descriptions come from their database content when it has one.</summary>
    private static readonly Dictionary<string, (string Title, string Description, string? ContentKey)> StaticPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/categories"] = ("All categories of local businesses and services", "Browse every category of local businesses and services listed on Calling Bell, with the number of businesses in each.", null),
        ["/pricing"] = ("Plans and pricing for businesses", "Calling Bell plans for local businesses: listing, leads, bookings and advertising, with prices for your country.", null),
        ["/list-your-business"] = ("List your business", "List your business on Calling Bell: get found by nearby customers, receive enquiries and bookings, and grow.", "ListYourBusiness"),
        ["/about"] = ("About Calling Bell", "About Calling Bell, the real-time local business network for discovering, contacting and booking local businesses.", "About"),
        ["/trust-and-safety"] = ("Trust and safety", "How Calling Bell verifies businesses, moderates reviews and keeps customers and businesses safe.", "TrustSafety"),
        ["/support"] = ("Contact support", "Get help with Calling Bell: contact support, find answers and report a problem.", "ContactSupport"),
    };

    private SeoOptions O => options.Value;

    public async Task<SeoPage> ResolveAsync(string path, string? query, CancellationToken ct)
    {
        path = Normalize(path);
        var seg = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            return seg switch
            {
                [] => await cache.GetOrCreateAsync("page|home", Lifetime, () => HomeAsync(ct)),
                ["business"] => Redirect("/owner"),
                ["business", var s, ..] when OwnerSections.Contains(s) => Redirect("/owner/" + string.Join('/', seg[1..])),
                ["business", var slug] => await BusinessAsync(slug, ct),
                ["b", var slug] => Redirect(SeoPaths.Business(slug)),
                ["category", _] or ["location", ..] => await LandingAsync(path, path, ct),
                ["categories", var slug] => await LandingAsync(SeoPaths.Category(slug), path, ct),
                ["search"] => Search(query),
                // A Google Maps place: Google's data, not ours to index.
                ["nearby", "place", _] => Plain(SeoPageKind.Search, "Business details", "Details of a business near you, from Google Maps.", path, NoIndexFollow),
                ["nearby"] => Plain(SeoPageKind.Search, "Explore nearby businesses", "Businesses near your location on " + O.SiteName + ".", path, NoIndexFollow),
                ["login" or "register" or "account" or "owner" or "admin" or "video", ..] => Plain(SeoPageKind.Private, "Your account", O.SiteName, path, NoIndexNoFollow),
                _ when StaticPages.ContainsKey(path) => await cache.GetOrCreateAsync("page|" + path, Lifetime, () => StaticAsync(path, ct)),
                _ => Missing(path),
            };
        }
        catch (NotFoundException)
        {
            return Missing(path);
        }
    }

    /// <summary>Lower-case, no trailing slash, no query; "/" for the home page.</summary>
    public static string Normalize(string path)
    {
        var p = path.Split('?', '#')[0].Trim();
        if (!p.StartsWith('/')) p = "/" + p;
        p = p.TrimEnd('/').ToLowerInvariant();
        return p.Length == 0 ? "/" : p;
    }

    // ---------------- Business ----------------

    private async Task<SeoPage> BusinessAsync(string slug, CancellationToken ct)
    {
        var row = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Slug == slug)
            .Select(b => new { b.Id, b.Status, b.ModifiedOn, b.CreatedOn, b.CityId, b.Name, b.PhoneNumber, AreaSlug = b.AreaRef != null ? b.AreaRef.Slug : null })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            // A former slug of a business still listed: its address moved permanently.
            var moved = await uow.Repository<BusinessSlugHistory>().QueryNoTracking().Where(h => h.Slug == slug && h.Business.Status == BusinessStatuses.Active)
                .Select(h => h.Business.Slug).FirstOrDefaultAsync(ct);
            return moved is not null && moved != slug ? Redirect(SeoPaths.Business(moved)) : Missing(SeoPaths.Business(slug));
        }
        // Removed from the directory (deactivated or suspended): gone, not "not found"; awaiting approval: not public yet.
        if (row.Status != BusinessStatuses.Active)
            return row.Status == BusinessStatuses.PendingApproval ? Missing(SeoPaths.Business(slug)) : Gone(SeoPaths.Business(slug));

        // Keyed by the record's last change, so an edit is never served from an older entry.
        var version = (row.ModifiedOn ?? row.CreatedOn).UtcTicks;
        return await cache.GetOrCreateAsync($"business|{row.Id}|{version}", Lifetime, async () =>
        {
            var b = await sender.Send(new GetBusinessBySlugQuery(slug), ct);
            var place = await uow.Repository<City>().QueryNoTracking().Where(c => c.Slug == b.CitySlug)
                .Select(c => new { c.Slug, StateSlug = c.State.Slug, StateName = c.State.Name, c.State.CountryCode }).FirstOrDefaultAsync(ct);
            // The same business listed twice (same name and phone in the same city): the earliest listing is the canonical page.
            var canonicalSlug = row.CityId is { } cityId ? await uow.Repository<Business>().QueryNoTracking().Listed()
                .Where(o => o.CityId == cityId && o.Name == row.Name && o.PhoneNumber == row.PhoneNumber && o.CreatedOn < row.CreatedOn)
                .OrderBy(o => o.CreatedOn).Select(o => o.Slug).FirstOrDefaultAsync(ct) : null;
            return BuildBusiness(b, place?.StateSlug, place?.StateName ?? b.State, place?.CountryCode ?? b.CountryCode, row.AreaSlug,
                canonicalSlug, row.ModifiedOn ?? row.CreatedOn);
        });
    }

    /// <summary>The business page's metadata and visible facts, from its database record only. Public so it can be tested in isolation.</summary>
    public SeoPage BuildBusiness(BusinessDetailDto b, string? stateSlug, string? stateName, string? countryCode, string? areaSlug,
        string? canonicalSlug, DateTimeOffset updatedOn)
    {
        var c = b.Card;
        var path = SeoPaths.Business(c.Slug);
        var url = SeoPaths.Absolute(O.SiteUrl, path);
        var category = c.SubCategoryName ?? c.CategoryName;
        var place = SeoText.Place(c.City, stateName);
        var countrySlug = countryCode is null ? null : SeoPaths.CountrySlug(countryCode);
        var hasLocation = countrySlug is not null && stateSlug is not null && b.CitySlug is not null;
        var topicSlug = c.SubCategorySlug ?? c.CategorySlug;

        // Breadcrumbs follow the location hierarchy down to the category in the city.
        var crumbs = new List<SeoLink> { new("Home", "/") };
        if (hasLocation)
        {
            crumbs.Add(new SeoLink(SeoPaths.CountryName(countryCode!), SeoPaths.Location(countrySlug)));
            if (!string.IsNullOrWhiteSpace(stateName)) crumbs.Add(new SeoLink(stateName!, SeoPaths.Location(countrySlug, stateSlug)));
            crumbs.Add(new SeoLink(c.City, SeoPaths.Location(countrySlug, stateSlug, b.CitySlug)));
            crumbs.Add(new SeoLink(category, SeoPaths.Location(countrySlug, stateSlug, b.CitySlug, topicSlug)));
        }
        else crumbs.Add(new SeoLink(category, SeoPaths.Category(topicSlug)));
        crumbs.Add(new SeoLink(c.Name, path));

        var images = new List<string>();
        foreach (var u in new[] { c.LogoUrl, b.Images.FirstOrDefault(i => i.IsPrimary)?.DesktopImageUrl ?? c.CoverImageUrl }
                     .Concat(b.Images.Select(i => i.DesktopImageUrl ?? i.ImageUrl)))
            if (!string.IsNullOrWhiteSpace(u) && !images.Contains(SeoPaths.Absolute(O.SiteUrl, u))) images.Add(SeoPaths.Absolute(O.SiteUrl, u));

        var content = BusinessContent(b, category, place, stateName, hasLocation ? (countrySlug!, stateSlug!) : null, areaSlug, topicSlug, updatedOn);
        var jsonLd = new List<string>
        {
            SchemaOrg.Serialize(SchemaOrg.LocalBusiness(b, url, countryCode is null ? null : SeoPaths.CountryName(countryCode), stateName, images.Take(6).ToList())),
            SchemaOrg.Serialize(SchemaOrg.Breadcrumbs(crumbs, O.SiteUrl)),
        };
        if (content.Faq.Count > 0) jsonLd.Add(SchemaOrg.Serialize(SchemaOrg.Faq(content.Faq)));

        var duplicate = canonicalSlug is not null && canonicalSlug != c.Slug;
        var doc = new SeoDocument(
            SeoText.BusinessTitle(c.Name, category, c.City, stateName, O.SiteName),
            SeoText.BusinessDescription(c.Name, category, place, b.PhoneNumber is not null || b.Email is not null, b.Services.Count > 0,
                b.Hours.Any(h => !h.IsClosed), c.ReviewCount > 0, O.SiteName),
            duplicate ? SeoPaths.Absolute(O.SiteUrl, SeoPaths.Business(canonicalSlug!)) : url,
            duplicate ? NoIndexFollow : Index, O.Language, "business.business",
            images.FirstOrDefault() ?? DefaultImage(), $"{c.Name}, {category} in {place}", crumbs, jsonLd, updatedOn);
        return new SeoPage(SeoPageKind.Business, 200, doc, content) { Business = b };
    }

    /// <summary>A factual summary, answers and links, each only from what the business record holds.</summary>
    private SeoPageContent BusinessContent(BusinessDetailDto b, string category, string place, string? stateName, (string Country, string State)? loc,
        string? areaSlug, string topicSlug, DateTimeOffset updatedOn)
    {
        var c = b.Card;
        var where = string.IsNullOrWhiteSpace(c.Area) ? place : $"{c.Area}, {place}";
        var address = SeoText.JoinAddress(b.AddressLine, c.Area, c.City, stateName, b.Pincode);
        var services = b.Services.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
        var openDays = b.Hours.Where(h => !h.IsClosed && h.Open is not null && h.Close is not null).ToList();

        var summary = new List<string> { $"{c.Name} is listed on {O.SiteName} under {category} in {where}." };
        if (!string.IsNullOrWhiteSpace(b.AddressLine)) summary.Add($"It is located at {address}.");
        if (services.Count > 0)
            summary.Add($"It offers {Plural(services.Count, "service", "services")}, including {SeoText.JoinList(services.Take(3).Select(s => s.Name).ToList())}.");
        if (!string.IsNullOrWhiteSpace(b.PhoneNumber)) summary.Add($"Customers can call {b.PhoneNumber}" + (c.AcceptsOnlineBooking ? " or book online." : " or send an enquiry."));
        if (c.ReviewCount > 0) summary.Add($"It is rated {c.AverageRating:0.0} out of 5 from {Plural(c.ReviewCount, "review", "reviews")}.");
        if (c.IsVerified && b.VerifiedOn is { } verified) summary.Add($"The business was verified on {verified:d MMMM yyyy}.");

        var faq = new List<SeoFaq>();
        if (address.Length > 0)
            faq.Add(new SeoFaq($"Where is {c.Name} located?", $"{c.Name} is at {address}" + (string.IsNullOrWhiteSpace(b.Landmark) ? "." : $" ({SeoText.Near(b.Landmark)}).")));
        var contact = new List<string>();
        if (!string.IsNullOrWhiteSpace(b.PhoneNumber)) contact.Add($"by phone on {b.PhoneNumber}");
        if (!string.IsNullOrWhiteSpace(b.WhatsAppNumber)) contact.Add($"on WhatsApp at {b.WhatsAppNumber}");
        if (!string.IsNullOrWhiteSpace(b.Email)) contact.Add($"by email at {b.Email}");
        if (contact.Count > 0)
            faq.Add(new SeoFaq($"How can I contact {c.Name}?", $"You can contact {c.Name} {SeoText.JoinList(contact)}, or send an enquiry or quote request on this page."));
        if (services.Count > 0)
            faq.Add(new SeoFaq($"What services does {c.Name} provide?", SeoText.JoinList(services.Take(8).Select(s => s.Price > 0
                ? $"{s.Name} ({MoneyText(s.Price, b.CountryCode)}{(string.IsNullOrWhiteSpace(s.PriceUnit) ? "" : " " + s.PriceUnit)})" : s.Name).ToList())
                + (services.Count > 8 ? $", and {services.Count - 8} more." : ".")));
        if (openDays.Count > 0)
            faq.Add(new SeoFaq($"What are the opening hours of {c.Name}?", string.Join("; ", b.Hours.Select(h => h.IsClosed || h.Open is null || h.Close is null
                ? $"{h.Day}: closed" : h.Open.StartsWith("00:00") && h.Close.StartsWith("23:59") ? $"{h.Day}: open 24 hours" : $"{h.Day}: {h.Open[..5]} to {h.Close[..5]}")) + "."));
        var served = new[] { c.Area, c.City }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (served.Count > 0)
            faq.Add(new SeoFaq($"Which areas does {c.Name} serve?", $"{c.Name} is based in {SeoText.JoinList(served!)}" +
                (c.OffersHomeService ? " and offers home visits." : ".") + (c.OffersVideoConsultation ? " Video consultations are also available." : "")));

        var links = new List<SeoLinkGroup>();
        if (loc is { } l)
        {
            var nearby = new List<SeoLink>
            {
                new($"{category} in {c.City}", SeoPaths.Location(l.Country, l.State, b.CitySlug, topicSlug)),
                new($"Local services in {c.City}", SeoPaths.Location(l.Country, l.State, b.CitySlug)),
            };
            if (areaSlug is not null && !string.IsNullOrWhiteSpace(c.Area))
            {
                nearby.Insert(1, new SeoLink($"{category} in {c.Area}", SeoPaths.Location(l.Country, l.State, b.CitySlug, areaSlug, topicSlug)));
                nearby.Add(new SeoLink($"Businesses in {c.Area}", SeoPaths.Location(l.Country, l.State, b.CitySlug, areaSlug)));
            }
            nearby.Add(new SeoLink($"Businesses in {stateName}", SeoPaths.Location(l.Country, l.State)));
            nearby.Add(new SeoLink($"All {category.ToLowerInvariant()}", SeoPaths.Category(topicSlug)));
            links.Add(new SeoLinkGroup("Explore nearby", nearby));
        }
        if (b.Similar.Count > 0)
            links.Add(new SeoLinkGroup($"Other {category.ToLowerInvariant()} nearby", b.Similar.Select(s => new SeoLink(s.Name, SeoPaths.Business(s.Slug))).ToList()));

        return new SeoPageContent($"About {c.Name}", string.Join(' ', summary), faq, links, updatedOn);
    }

    // ---------------- Landing pages, home, static ----------------

    private async Task<SeoPage> LandingAsync(string landingPath, string requestedPath, CancellationToken ct)
    {
        var result = await sender.Send(new GetLandingPageQuery(landingPath), ct);
        if (result.RedirectTo is { } to) return Redirect(to);
        if (result.Page is not { } p) return Missing(requestedPath);
        var url = SeoPaths.Absolute(O.SiteUrl, p.Path);
        var jsonLd = new List<string> { SchemaOrg.Serialize(SchemaOrg.Breadcrumbs(p.Breadcrumbs, O.SiteUrl)) };
        if (p.Businesses.Count > 0)
            jsonLd.Add(SchemaOrg.Serialize(SchemaOrg.ItemList(p.Heading, p.Businesses.Select(b => (b.Name, SeoPaths.Absolute(O.SiteUrl, SeoPaths.Business(b.Slug)))).ToList())));
        if (p.Faq.Count > 0) jsonLd.Add(SchemaOrg.Serialize(SchemaOrg.Faq(p.Faq)));
        var image = p.Businesses.Select(b => b.CoverImageUrl ?? b.LogoUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        // An old /categories/{slug} address shows the same page: its canonical is the /category/ one.
        var doc = new SeoDocument(p.Title, p.Description, url, p.Indexable ? Index : NoIndexFollow, O.Language, "website",
            image is null ? DefaultImage() : SeoPaths.Absolute(O.SiteUrl, image), p.Heading, p.Breadcrumbs, jsonLd);
        return new SeoPage(p.Kind == "category" ? SeoPageKind.Category : SeoPageKind.Location, 200, doc,
            new SeoPageContent(p.Heading, p.Summary, p.Faq, p.Links)) { Landing = p };
    }

    private async Task<SeoPage> HomeAsync(CancellationToken ct)
    {
        // Links into the directory: categories and cities that have listings.
        var categories = await sender.Send(new GetCategoriesQuery(), ct);
        var topSubs = categories.SelectMany(c => c.SubCategories).Where(s => s.BusinessCount > 0).OrderByDescending(s => s.BusinessCount).Take(24)
            .Select(s => new SeoLink(s.Name, SeoPaths.Category(s.Slug), s.BusinessCount)).ToList();
        var cities = await uow.Repository<Business>().QueryNoTracking().Listed().Where(b => b.CityRef != null)
            .GroupBy(b => new { b.CityRef!.Slug, b.CityRef.Name, StateSlug = b.CityRef.State.Slug, b.CityRef.State.CountryCode })
            .Select(g => new { g.Key.Slug, g.Key.Name, g.Key.StateSlug, g.Key.CountryCode, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(24).ToListAsync(ct);
        var links = new List<SeoLinkGroup>();
        if (topSubs.Count > 0) links.Add(new SeoLinkGroup("Popular categories", topSubs));
        if (cities.Count > 0)
            links.Add(new SeoLinkGroup("Cities", cities.Select(x => new SeoLink(x.Name, SeoPaths.Location(SeoPaths.CountrySlug(x.CountryCode), x.StateSlug, x.Slug), x.Count)).ToList()));
        var summary = $"{O.SiteName} is a local business directory and booking platform: find local businesses and service providers, see who is " +
                      "available right now, compare reviews, send enquiries, request quotes and book services." +
                      (cities.Count > 0 ? $" Businesses are listed in {Plural(cities.Count, "city", "cities")}, including {SeoText.JoinList(cities.Take(5).Select(c => c.Name).ToList())}." : "");
        var crumbs = new List<SeoLink> { new("Home", "/") };
        var doc = new SeoDocument($"{O.SiteName} | Find Local Businesses and Services Near You",
            SeoText.Clamp($"Discover trusted local businesses on {O.SiteName}. See who is available right now, compare reviews, request quotes and book services.", SeoText.MaxDescription),
            SeoPaths.Absolute(O.SiteUrl, "/"), Index, O.Language, "website", DefaultImage(), O.SiteName, crumbs,
            [SchemaOrg.Serialize(SchemaOrg.Organization(O)), SchemaOrg.Serialize(SchemaOrg.WebSite(O))]);
        return new SeoPage(SeoPageKind.Home, 200, doc, new SeoPageContent($"{O.SiteName}: find local businesses and services", summary, [], links));
    }

    private async Task<SeoPage> StaticAsync(string path, CancellationToken ct)
    {
        var (title, description, key) = StaticPages[path];
        string? heading = null, summary = null;
        if (key is not null)
        {
            try
            {
                // The page's own hero text from the database, written without a particular visitor's country.
                var page = await sender.Send(new GetMarketingPageQuery(key), ct);
                if (page.Blocks.FirstOrDefault(b => b.Section == "Hero") is { } hero)
                {
                    heading = Neutral(hero.Title);
                    summary = hero.Subtitle is null ? null : Neutral(hero.Subtitle);
                    if (summary is not null) description = SeoText.Clamp(summary, SeoText.MaxDescription);
                }
            }
            catch (NotFoundException) { /* no content rows: the fixed text stands */ }
        }
        var crumbs = new List<SeoLink> { new("Home", "/"), new(title, path) };
        var doc = new SeoDocument($"{title} | {O.SiteName}", description, SeoPaths.Absolute(O.SiteUrl, path), Index, O.Language, "website",
            DefaultImage(), title, crumbs, [SchemaOrg.Serialize(SchemaOrg.Breadcrumbs(crumbs, O.SiteUrl))]);
        return new SeoPage(SeoPageKind.Static, 200, doc, new SeoPageContent(heading ?? title, summary ?? description, [], []));
    }

    private static string Neutral(string text) => text.Replace("{country's}", "your country's").Replace("{country}", "your country");

    // ---------------- Search, errors, redirects ----------------

    /// <summary>Internal search results: useful to visitors, never an index page (the category and location pages are).</summary>
    private SeoPage Search(string? query)
    {
        var q = query is null ? null : System.Web.HttpUtility.ParseQueryString(query)["q"]?.Trim();
        var title = string.IsNullOrWhiteSpace(q) ? "Search local businesses" : $"Search results for \"{SeoText.Clamp(q, 60)}\"";
        return Plain(SeoPageKind.Search, title, $"Search local businesses and services on {O.SiteName}.", "/search", NoIndexFollow);
    }

    private SeoPage Plain(SeoPageKind kind, string title, string description, string path, string robots) =>
        new(kind, 200, new SeoDocument($"{title} | {O.SiteName}", description, SeoPaths.Absolute(O.SiteUrl, path), robots, O.Language, "website",
            DefaultImage(), null, [], []), new SeoPageContent(title, null, [], []));

    private SeoPage Redirect(string to) => new(SeoPageKind.Redirect, 301, Blank("Moved", to), Empty, to);

    private SeoPage Missing(string path) =>
        new(SeoPageKind.NotFound, 404, Blank("Page not found", path), new SeoPageContent("Page not found", "The page you're looking for doesn't exist or has moved.", [], []));

    private SeoPage Gone(string path) =>
        new(SeoPageKind.Gone, 410, Blank("Business no longer listed", path),
            new SeoPageContent("This business is no longer listed", $"This business has been removed from {O.SiteName}.", [], []));

    private SeoDocument Blank(string title, string path) =>
        new($"{title} | {O.SiteName}", title, SeoPaths.Absolute(O.SiteUrl, path), NoIndexFollow, O.Language, "website", null, null, [], []);

    private static readonly SeoPageContent Empty = new(null, null, [], []);

    private string? DefaultImage() => string.IsNullOrWhiteSpace(O.DefaultImageUrl) ? null : SeoPaths.Absolute(O.SiteUrl, O.DefaultImageUrl);

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string MoneyText(decimal amount, string? country)
    {
        var currency = SchemaOrg.Currency(country);
        return currency is null ? amount.ToString("0.##", CultureInfo.InvariantCulture) : $"{currency} {amount.ToString("#,0.##", CultureInfo.InvariantCulture)}";
    }
}
