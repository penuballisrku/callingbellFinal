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
public sealed class SeoController(ISeoMetadataService seo, SitemapService sitemaps, IOptions<SeoOptions> options) : ControllerBase
{
    /// <summary>Public pages open to every crawler; accounts, dashboards and the API (except media) closed; sitemap listed.</summary>
    [HttpGet("/robots.txt")]
    public ContentResult Robots()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        var site = options.Value.SiteUrl.TrimEnd('/');
        var text = new StringBuilder()
            .AppendLine("User-agent: *")
            .AppendLine("Allow: /")
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
            .AppendLine($"Sitemap: {site}/sitemap.xml")
            .ToString();
        return Content(text, "text/plain", Encoding.UTF8);
    }

    /// <summary>The sitemap index.</summary>
    [HttpGet("/sitemap.xml")]
    public async Task<ContentResult> SitemapIndex(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        var files = await sitemaps.FilesAsync(ct);
        return Content(sitemaps.IndexXml(files, DateTimeOffset.UtcNow), "application/xml", Encoding.UTF8);
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
