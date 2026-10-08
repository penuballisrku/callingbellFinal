using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CallingBell.Application.Features.Seo;

/// <summary>Site-wide SEO settings (appsettings "Seo").</summary>
public sealed class SeoOptions
{
    public const string Section = "Seo";
    /// <summary>The public origin canonical URLs use, without a trailing slash (e.g. https://www.callingbell.com).</summary>
    public string SiteUrl { get; set; } = "https://www.callingbell.com";
    public string SiteName { get; set; } = "Calling Bell";
    /// <summary>The site's language (html lang, og:locale).</summary>
    public string Language { get; set; } = "en";
    /// <summary>Logo for Organization schema; relative to <see cref="SiteUrl"/> or absolute.</summary>
    public string? LogoUrl { get; set; } = "/favicon.svg";
    /// <summary>Share image for pages without one of their own.</summary>
    public string? DefaultImageUrl { get; set; }
    /// <summary>Calling Bell's own official profiles (Organization sameAs); empty until real ones exist.</summary>
    public string[] SameAs { get; set; } = [];
    /// <summary>A location / category page is indexable only with at least this many listed businesses (no thin pages).</summary>
    public int MinBusinessesForIndex { get; set; } = 1;
}

/// <summary>A named link: breadcrumbs, related pages.</summary>
public sealed record SeoLink(string Name, string Url, int? Count = null);

/// <summary>A group of related links shown on a page ("Electricians in other areas of Hyderabad").</summary>
public sealed record SeoLinkGroup(string Title, IReadOnlyList<SeoLink> Links);

/// <summary>A question and its answer, shown on the page and, only then, in FAQPage structured data.</summary>
public sealed record SeoFaq(string Question, string Answer);

/// <summary>
/// Everything in a page's head that search and answer engines read. <paramref name="Url"/> fields are absolute;
/// <paramref name="JsonLd"/> holds complete, already-serialised JSON-LD documents.
/// </summary>
public sealed record SeoDocument(string Title, string Description, string CanonicalUrl, string Robots, string Language, string OgType,
    string? ImageUrl, string? ImageAlt, IReadOnlyList<SeoLink> Breadcrumbs, IReadOnlyList<string> JsonLd, DateTimeOffset? LastModified = null);

public enum SeoPageKind { Home, Business, Category, Location, Static, Search, Private, Redirect, NotFound, Gone }

/// <summary>
/// The visible, data-backed parts every SEO page shows (in the server-rendered HTML and in the React page alike): a factual summary,
/// questions and answers, and internal links.
/// </summary>
public sealed record SeoPageContent(string? Heading, string? Summary, IReadOnlyList<SeoFaq> Faq, IReadOnlyList<SeoLinkGroup> Links,
    DateTimeOffset? UpdatedOn = null);

/// <summary>A resolved public URL: what to answer (status, redirect) and what to say about it.</summary>
public sealed record SeoPage(SeoPageKind Kind, int StatusCode, SeoDocument Document, SeoPageContent Content, string? RedirectTo = null)
{
    /// <summary>The business shown, for server rendering (business pages only).</summary>
    public Businesses.BusinessDetailDto? Business { get; init; }
    /// <summary>The landing page shown, for server rendering (category and location pages).</summary>
    public LandingPageDto? Landing { get; init; }
}

/// <summary>Stable, human-readable URL paths for public pages; the database id stays internal.</summary>
public static partial class SeoPaths
{
    public static string Business(string slug) => $"/business/{slug}";
    public static string Category(string slug) => $"/category/{slug}";

    /// <summary>/location/{country}/{state}/{city}/{area}/{category}; missing levels are skipped.</summary>
    public static string Location(params string?[] segments) =>
        "/location/" + string.Join('/', segments.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>English country name from its ISO code ("US" → "United States"); the code itself when unknown.</summary>
    public static string CountryName(string code)
    {
        try { return new RegionInfo(code).EnglishName; }
        catch (ArgumentException) { return code.ToUpperInvariant(); }
    }

    /// <summary>"united-states" for "US".</summary>
    public static string CountrySlug(string code) => Slugify(CountryName(code));

    /// <summary>Lower-case ASCII words joined by hyphens ("Côte-des-Neiges" → "cote-des-neiges").</summary>
    public static string Slugify(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) ascii.Append(ch);
        return NonSlug().Replace(ascii.ToString().ToLowerInvariant(), "-").Trim('-');
    }

    /// <summary>Absolute URL on the site for a path (or the value itself when already absolute).</summary>
    public static string Absolute(string siteUrl, string pathOrUrl) =>
        pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : siteUrl.TrimEnd('/') + (pathOrUrl.StartsWith('/') ? pathOrUrl : "/" + pathOrUrl);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}

/// <summary>
/// Titles and meta descriptions from real data. Concise and natural: the name, what it is and where - no keyword stuffing, no claims the
/// data doesn't support.
/// </summary>
public static class SeoText
{
    /// <summary>Search results show about 60 characters and cut the rest; keeping the name, what and where matters more than that.</summary>
    public const int MaxTitle = 80;
    public const int MaxDescription = 160;

    /// <summary>"San Antonio, Texas"; just the city when the region is unknown or the same.</summary>
    public static string Place(string city, string? region) =>
        string.IsNullOrWhiteSpace(region) || string.Equals(region, city, StringComparison.OrdinalIgnoreCase) ? city : $"{city}, {region}";

    /// <summary>
    /// {BusinessName} | {Category} in {City}, {Region} | Calling Bell. When too long: the region goes first, then the site name, then the
    /// category - the name and the city stay as long as possible; never cut mid-word.
    /// </summary>
    public static string BusinessTitle(string name, string? category, string city, string? region, string site)
    {
        var place = Place(city, region);
        var what = category is null ? "" : $"{category} in ";
        foreach (var candidate in new[]
                 {
                     $"{name} | {what}{place} | {site}",
                     $"{name} | {what}{city} | {site}",
                     $"{name} | {what}{city}",
                     $"{name} | {city}",
                 })
            if (candidate.Length <= MaxTitle) return candidate;
        return Clamp(name, MaxTitle);
    }

    /// <summary>{Category} in {City}, {Region} | Local {Category} | Calling Bell; without a place, {Category} | Calling Bell.</summary>
    public static string CategoryTitle(string category, string? place, string site)
    {
        if (place is null) return Fit($"{category} | Find Local {category} | {site}", $"{category} | {site}");
        var title = $"{category} in {place} | Local {category} | {site}";
        return title.Length <= MaxTitle ? title : Fit($"{category} in {place} | {site}", $"{category} in {place}");
    }

    /// <summary>Local Services in {City}, {Region} | Calling Bell.</summary>
    public static string LocationTitle(string place, string site) => Fit($"Local Services in {place} | {site}", $"{place} | {site}");

    /// <summary>
    /// "Find {Name}, {a category} in {Place}. View contact details, services, hours, reviews and location on Calling Bell." Lists only what
    /// the page really has.
    /// </summary>
    public static string BusinessDescription(string name, string? category, string place, bool hasContact, bool hasServices, bool hasHours,
        bool hasReviews, string site)
    {
        var what = category is null ? "" : $", listed under {category},";
        var parts = new List<string>();
        if (hasContact) parts.Add("contact details");
        if (hasServices) parts.Add("services");
        if (hasHours) parts.Add("hours");
        if (hasReviews) parts.Add("reviews");
        parts.Add("location");
        return Clamp($"Find {name}{what} in {place}. View {JoinList(parts)} on {site}.", MaxDescription);
    }

    /// <summary>"Find {count} {category} in {place}. Compare local businesses, services, reviews and contact details on Calling Bell."</summary>
    public static string CategoryDescription(string category, string? place, int count, string site)
    {
        var where = place is null ? "" : $" in {place}";
        if (count == 0) return Clamp($"{category}{where} on {site}. Businesses appear here once they are listed.", MaxDescription);
        var how = count == 1 ? "1 listed business" : $"{count} listed businesses";
        return Clamp($"Find {category}{where}: {how}. Compare services, reviews, contact details and availability on {site}.", MaxDescription);
    }

    public static string LocationDescription(string place, int businesses, int categories, string site) => businesses == 0
        ? Clamp($"Local businesses and services in {place} on {site}. Businesses appear here once they are listed.", MaxDescription)
        : Clamp($"Local businesses and services in {place}: {businesses} listed across {categories} {(categories == 1 ? "category" : "categories")}. " +
                $"Compare reviews, contact details and availability on {site}.", MaxDescription);

    /// <summary>
    /// Address parts joined with commas, leaving out empty parts and parts the address already contains ("Gokhale Road, Dadar West"
    /// plus the area "Dadar West" stays "Gokhale Road, Dadar West").
    /// </summary>
    public static string JoinAddress(params string?[] parts)
    {
        var joined = new List<string>();
        foreach (var raw in parts)
        {
            var part = raw?.Trim().Trim(',');
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (joined.Any(j => j.Contains(part, StringComparison.OrdinalIgnoreCase))) continue;
            joined.Add(part);
        }
        return string.Join(", ", joined);
    }

    /// <summary>"Near the Portuguese Church" for a landmark, without doubling a "near" the landmark already starts with.</summary>
    public static string Near(string landmark) =>
        landmark.TrimStart().StartsWith("near ", StringComparison.OrdinalIgnoreCase) ? landmark.Trim() : $"Near {landmark.Trim()}";

    public static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>Cut at a word boundary with an ellipsis when longer than <paramref name="max"/>.</summary>
    public static string Clamp(string text, int max)
    {
        text = Regex.Replace(text.Trim(), @"\s+", " ");
        if (text.Length <= max) return text;
        var cut = text[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd(',', ';', ':', '-', ' ', '|') + "…";
    }

    private static string Fit(string preferred, string shorter) => preferred.Length <= MaxTitle ? preferred : Clamp(shorter, MaxTitle);
}
