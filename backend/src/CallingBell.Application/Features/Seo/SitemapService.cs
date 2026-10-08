using System.Globalization;
using System.Text;
using System.Xml;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallingBell.Application.Features.Seo;

public sealed record SitemapEntry(string Location, DateTimeOffset? LastModified);

/// <summary>
/// The sitemaps: /sitemap.xml is an index of sitemap files - static pages, categories, locations and businesses, the last two split into
/// files of <see cref="PerFile"/> URLs, so it scales to millions of pages. Only canonical, indexable URLs are listed: listed businesses
/// (not duplicate listings), and categories and locations that have at least <see cref="SeoOptions.MinBusinessesForIndex"/> listed
/// businesses - never search, filtered, private or empty pages.
/// </summary>
public sealed class SitemapService(IUnitOfWork uow, IOptions<SeoOptions> options, SeoCache cache)
{
    /// <summary>Under the protocol's 50,000-URL limit per file.</summary>
    public const int PerFile = 45_000;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private static readonly string[] StaticPaths = ["/", "/categories", "/list-your-business", "/pricing", "/about", "/trust-and-safety", "/support"];

    private SeoOptions O => options.Value;

    /// <summary>The sitemap files that exist now (names without ".xml").</summary>
    public async Task<IReadOnlyList<string>> FilesAsync(CancellationToken ct)
    {
        var businesses = await CanonicalBusinesses().CountAsync(ct);
        var locations = (await LocationsAsync(ct)).Count;
        var files = new List<string> { "sitemap-static", "sitemap-categories" };
        for (var i = 1; i <= Math.Max(1, (locations + PerFile - 1) / PerFile); i++) files.Add($"sitemap-locations-{i}");
        for (var i = 1; i <= Math.Max(1, (businesses + PerFile - 1) / PerFile); i++) files.Add($"sitemap-businesses-{i}");
        return files;
    }

    /// <summary>The URLs of one sitemap file; null when there is no such file.</summary>
    public async Task<IReadOnlyList<SitemapEntry>?> EntriesAsync(string file, CancellationToken ct)
    {
        if (file == "sitemap-static") return StaticPaths.Select(p => Entry(p, null)).ToList();
        if (file == "sitemap-categories") return await CategoriesAsync(ct);
        if (Page(file, "sitemap-locations-") is { } lp)
        {
            var all = await LocationsAsync(ct);
            return (lp - 1) * PerFile < Math.Max(all.Count, 1) ? all.Skip((lp - 1) * PerFile).Take(PerFile).ToList() : null;
        }
        if (Page(file, "sitemap-businesses-") is { } bp)
        {
            var list = await cache.GetOrCreateAsync($"sitemap|businesses|{bp}", Lifetime, async () =>
                (await CanonicalBusinesses().OrderBy(b => b.CreatedOn).ThenBy(b => b.Id).Skip((bp - 1) * PerFile).Take(PerFile)
                    .Select(b => new { b.Slug, Modified = b.ModifiedOn ?? b.CreatedOn }).ToListAsync(ct))
                .Select(b => Entry(SeoPaths.Business(b.Slug), b.Modified)).ToList());
            return bp == 1 || list.Count > 0 ? list : null;
        }
        return null;
    }

    /// <summary>Listed businesses that are not a later duplicate (same name and phone in the same city) of an earlier listing.</summary>
    private IQueryable<Business> CanonicalBusinesses()
    {
        var listed = uow.Repository<Business>().QueryNoTracking().Listed();
        return listed.Where(b => !listed.Any(o => o.CityId == b.CityId && o.Name == b.Name && o.PhoneNumber == b.PhoneNumber && o.CreatedOn < b.CreatedOn));
    }

    private Task<List<SitemapEntry>> CategoriesAsync(CancellationToken ct) => cache.GetOrCreateAsync("sitemap|categories", Lifetime, async () =>
    {
        var listed = uow.Repository<Business>().QueryNoTracking().Listed();
        var subs = await listed.Where(b => b.SubCategory != null).GroupBy(b => b.SubCategory!.Slug)
            .Select(g => new { Slug = g.Key, Count = g.Count(), Modified = g.Max(b => b.ModifiedOn ?? b.CreatedOn) }).ToListAsync(ct);
        var cats = await listed.GroupBy(b => b.Category.Slug)
            .Select(g => new { Slug = g.Key, Count = g.Count(), Modified = g.Max(b => b.ModifiedOn ?? b.CreatedOn) }).ToListAsync(ct);
        return cats.Concat(subs).Where(x => x.Count >= O.MinBusinessesForIndex)
            .Select(x => Entry(SeoPaths.Category(x.Slug), x.Modified)).DistinctBy(e => e.Location).ToList();
    });

    /// <summary>
    /// Every location page with enough listings - country, state, city, city + category, area, area + category - from one grouped
    /// query over the listed businesses (so only combinations that exist are considered, never every place in the world).
    /// </summary>
    private Task<List<SitemapEntry>> LocationsAsync(CancellationToken ct) => cache.GetOrCreateAsync("sitemap|locations", Lifetime, async () =>
    {
        var groups = await uow.Repository<Business>().QueryNoTracking().Listed().Where(b => b.CityRef != null)
            .GroupBy(b => new
            {
                b.CityRef!.State.CountryCode, StateSlug = b.CityRef.State.Slug, CitySlug = b.CityRef.Slug,
                AreaSlug = b.AreaRef != null ? b.AreaRef.Slug : null, SubSlug = b.SubCategory != null ? b.SubCategory.Slug : null,
            })
            .Select(g => new { g.Key, Count = g.Count(), Modified = g.Max(b => b.ModifiedOn ?? b.CreatedOn) })
            .ToListAsync(ct);

        var pages = new Dictionary<string, (int Count, DateTimeOffset Modified)>();
        void Add(string path, int count, DateTimeOffset modified)
        {
            pages[path] = pages.TryGetValue(path, out var p) ? (p.Count + count, p.Modified > modified ? p.Modified : modified) : (count, modified);
        }
        foreach (var g in groups)
        {
            var country = SeoPaths.CountrySlug(g.Key.CountryCode);
            Add(SeoPaths.Location(country), g.Count, g.Modified);
            Add(SeoPaths.Location(country, g.Key.StateSlug), g.Count, g.Modified);
            Add(SeoPaths.Location(country, g.Key.StateSlug, g.Key.CitySlug), g.Count, g.Modified);
            if (g.Key.SubSlug is not null) Add(SeoPaths.Location(country, g.Key.StateSlug, g.Key.CitySlug, g.Key.SubSlug), g.Count, g.Modified);
            if (g.Key.AreaSlug is not null)
            {
                Add(SeoPaths.Location(country, g.Key.StateSlug, g.Key.CitySlug, g.Key.AreaSlug), g.Count, g.Modified);
                if (g.Key.SubSlug is not null) Add(SeoPaths.Location(country, g.Key.StateSlug, g.Key.CitySlug, g.Key.AreaSlug, g.Key.SubSlug), g.Count, g.Modified);
            }
        }
        return pages.Where(p => p.Value.Count >= O.MinBusinessesForIndex).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => Entry(p.Key, p.Value.Modified)).ToList();
    });

    private SitemapEntry Entry(string path, DateTimeOffset? modified) => new(SeoPaths.Absolute(O.SiteUrl, path), modified);

    private static int? Page(string file, string prefix) =>
        file.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(file[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 ? n : null;

    // ---------------- XML ----------------

    private static readonly XmlWriterSettings Xml = new() { Encoding = new UTF8Encoding(false), Indent = false, Async = false };
    private const string Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>A sitemap index listing the sitemap files.</summary>
    public string IndexXml(IEnumerable<string> files, DateTimeOffset? lastModified = null)
    {
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(new StringWriterUtf8(sb), Xml))
        {
            w.WriteStartDocument();
            w.WriteStartElement("sitemapindex", Ns);
            foreach (var f in files)
            {
                w.WriteStartElement("sitemap", Ns);
                w.WriteElementString("loc", Ns, SeoPaths.Absolute(O.SiteUrl, $"/{f}.xml"));
                if (lastModified is { } m) w.WriteElementString("lastmod", Ns, m.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        return sb.ToString();
    }

    /// <summary>A sitemap file (urlset); text is XML-escaped by the writer.</summary>
    public static string UrlSetXml(IEnumerable<SitemapEntry> entries)
    {
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(new StringWriterUtf8(sb), Xml))
        {
            w.WriteStartDocument();
            w.WriteStartElement("urlset", Ns);
            foreach (var e in entries)
            {
                w.WriteStartElement("url", Ns);
                w.WriteElementString("loc", Ns, e.Location);
                if (e.LastModified is { } m) w.WriteElementString("lastmod", Ns, m.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        return sb.ToString();
    }

    /// <summary>XmlWriter writes the encoding its TextWriter reports; sitemaps are UTF-8.</summary>
    private sealed class StringWriterUtf8(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
