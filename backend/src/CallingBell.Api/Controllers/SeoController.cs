using System.Text;
using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Seo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CallingBell.Api.Controllers;

/// <summary>
/// robots.txt, the sitemaps, and page SEO for the React app. The React app asks GET /api/seo for the page it shows, so titles, canonical
/// URLs, robots rules and structured data come from the same service that renders them for crawlers.
/// </summary>
[ApiController]
public sealed class SeoController(ISeoMetadataService seo, SitemapService sitemaps, LlmsTxtService llms, IOptions<SeoOptions> options) : ControllerBase
{
    /// <summary>
    /// Search and AI answer-engine crawlers welcomed by name (the same rules as everyone): Google and Bing search, Google's and Apple's AI,
    /// and the crawlers behind ChatGPT, Claude and Perplexity search and their fetches for a user's question, so listings can be found,
    /// cited and linked in AI answers. Training-only crawlers fall under "*" like any other bot.
    /// </summary>
    private static readonly string[] NamedCrawlers =
    [
        "Googlebot", "Bingbot", "Google-Extended", "Applebot", "Applebot-Extended",
        "OAI-SearchBot", "ChatGPT-User", "GPTBot", "Claude-SearchBot", "Claude-User", "ClaudeBot", "PerplexityBot", "Perplexity-User",
        "DuckAssistBot", "YandexBot",
    ];

    /// <summary>Public pages open to every crawler; accounts, dashboards and the API (except media) closed; sitemap and llms.txt listed.</summary>
    [HttpGet("/robots.txt")]
    public ContentResult Robots()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        var site = options.Value.SiteUrl.TrimEnd('/');
        var text = new StringBuilder()
            .AppendLine($"# {options.Value.SiteName}: a guide for AI assistants is at {site}/llms.txt (full directory: {site}/llms-full.txt).")
            .AppendLine("User-agent: *");
        // One group for everyone, with the named crawlers in it: a crawler obeys only the group that names it, so they share the rules.
        foreach (var bot in NamedCrawlers) text.AppendLine($"User-agent: {bot}");
        text.AppendLine("Allow: /")
            // Business photos and logos are served from /api/media; image search needs them.
            .AppendLine("Allow: /api/media/")
            .AppendLine("Disallow: /api/")
            .AppendLine("Disallow: /hubs/")
            .AppendLine("Disallow: /admin")
            .AppendLine("Disallow: /owner")
            .AppendLine("Disallow: /account")
            .AppendLine("Disallow: /login")
            .AppendLine("Disallow: /register")
            .AppendLine()
            .AppendLine($"Sitemap: {site}/sitemap.xml");
        return Content(text.ToString(), "text/plain", Encoding.UTF8);
    }

    /// <summary>
    /// llms.txt (llmstxt.org): what CallInBell is, how its pages and search URLs work, and its categories and cities with real counts, in
    /// Markdown for AI assistants and answer engines.
    /// </summary>
    [HttpGet("/llms.txt")]
    public async Task<ContentResult> LlmsTxt(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(await llms.SummaryAsync(ct), "text/markdown; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>llms.txt plus every listed business (category, place, rating, services, profile link).</summary>
    [HttpGet("/llms-full.txt")]
    public async Task<ContentResult> LlmsFullTxt(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(await llms.FullAsync(ct), "text/markdown; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>The IndexNow key, which the search engines fetch to confirm the site sent its change notices. 404 while IndexNow is off.</summary>
    [HttpGet("/indexnow.txt")]
    public IActionResult IndexNowKey()
    {
        var o = options.Value.IndexNow;
        if (!o.IsValid) return NotFound();
        Response.Headers.CacheControl = "public, max-age=86400";
        return Content(o.Key!, "text/plain", Encoding.UTF8);
    }

    /// <summary>The sitemap index, each file with the newest change among its pages.</summary>
    [HttpGet("/sitemap.xml")]
    public async Task<ContentResult> SitemapIndex(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        var files = new List<(string, DateTimeOffset?)>();
        foreach (var f in await sitemaps.FilesAsync(ct)) files.Add((f, await sitemaps.LastModifiedAsync(f, ct)));
        return Content(sitemaps.IndexXml(files), "application/xml", Encoding.UTF8);
    }

    /// <summary>One sitemap file: sitemap-static, sitemap-categories, sitemap-locations-{n}, sitemap-businesses-{n}.</summary>
    [HttpGet("/{file:regex(^sitemap-[[a-z]]+(-\\d+)?$)}.xml")]
    public async Task<IActionResult> SitemapFile(string file, CancellationToken ct)
    {
        var entries = await sitemaps.EntriesAsync(file, ct);
        if (entries is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(SitemapService.UrlSetXml(entries), "application/xml", Encoding.UTF8);
    }

    /// <summary>
    /// SEO for a page of the app: status (404 / 410 / 301 with <c>redirectTo</c>), head metadata (title, description, canonical, robots,
    /// Open Graph, JSON-LD) and the visible facts (summary, questions, links). Category and location pages include their content.
    /// </summary>
    /// <param name="path">The page's path, e.g. /business/mr-electric-san-antonio.</param>
    /// <param name="query">The page's query string (search pages only).</param>
    [HttpGet("/api/seo")]
    public async Task<ActionResult<ApiResponse<SeoPageResponse>>> Page([FromQuery] string path, [FromQuery] string? query, CancellationToken ct)
    {
        var page = await seo.ResolveAsync(path, query, ct);
        Response.Headers.CacheControl = "public, max-age=60";
        return Ok(ApiResponse<SeoPageResponse>.Ok(new SeoPageResponse(page.Kind.ToString(), page.StatusCode, page.RedirectTo, page.Document, page.Content, page.Landing)));
    }
}

/// <param name="Landing">Category and location pages: the page's businesses, links and questions (the React page renders these).</param>
public sealed record SeoPageResponse(string Kind, int StatusCode, string? RedirectTo, SeoDocument Document, SeoPageContent Content, LandingPageDto? Landing);
