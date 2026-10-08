using System.Text;
using CallingBell.Application.Features.Seo;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace CallingBell.Api.Seo;

/// <summary>Serving the built React app (web/dist) from the API, with each public page's SEO head and content rendered in (Seo:SpaRoot).</summary>
public static class SpaHosting
{
    /// <summary>The built app's folder when configured and present; null otherwise (development, where Vite serves the app).</summary>
    public static string? Root(IConfiguration config, IWebHostEnvironment env)
    {
        var root = config["Seo:SpaRoot"];
        if (string.IsNullOrWhiteSpace(root)) return null;
        var full = Path.GetFullPath(Path.IsPathRooted(root) ? root : Path.Combine(env.ContentRootPath, root));
        return File.Exists(Path.Combine(full, "index.html")) ? full : null;
    }

    public static void UseSpaWithSeo(this WebApplication app, string root)
    {
        // Hashed build assets never change: cache them for a year. Everything else (index.html, favicon) is revalidated.
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets")
                ? "public, max-age=31536000, immutable"
                : "public, max-age=0, must-revalidate",
        });
        app.UseMiddleware<SpaSeoMiddleware>(root);
    }
}

/// <summary>
/// Answers page requests (GET/HEAD for paths that are not API calls or files) with the app's index.html, its head replaced by the page's
/// SEO tags and its root element holding the server-rendered content, at the page's real status: 200, 301 (moved slug, old address),
/// 404 or 410 (business removed). If the SEO service fails, the plain app page is served, so a page never breaks because of SEO.
/// </summary>
public sealed class SpaSeoMiddleware(RequestDelegate next, string root, ILogger<SpaSeoMiddleware> logger)
{
    private static readonly string[] PassThrough = ["/api", "/hubs", "/swagger", "/health", "/assets"];
    private readonly string _indexPath = Path.Combine(root, "index.html");
    private string? _template;
    private DateTime _templateWritten;

    public async Task InvokeAsync(HttpContext ctx, ISeoMetadataService seo, SeoHtmlRenderer renderer)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (!(HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
            || PassThrough.Any(p => ctx.Request.Path.StartsWithSegments(p)) || Path.HasExtension(path))
        {
            await next(ctx);
            return;
        }

        var html = await TemplateAsync(ctx.RequestAborted);
        SeoPage? page = null;
        try
        {
            page = await seo.ResolveAsync(path, ctx.Request.QueryString.Value, ctx.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "SEO rendering failed for {Path}; serving the plain app page", path);
        }

        if (page?.RedirectTo is { } to)
        {
            // Permanent: the content lives at the new address. The query string (e.g. ?welcome=1 for the owner dashboard) is kept.
            ctx.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            ctx.Response.Headers.Location = to + (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "");
            return;
        }

        if (page is not null)
        {
            html = Inject(html, renderer.Head(page.Document), renderer.Body(page), page.Document.Language);
            ctx.Response.StatusCode = page.StatusCode;
            if (page.Document.Robots.StartsWith("noindex", StringComparison.Ordinal)) ctx.Response.Headers["X-Robots-Tag"] = page.Document.Robots;
            if (page.Document.LastModified is { } modified) ctx.Response.Headers.LastModified = modified.ToString("R");
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        // Always revalidated, so a changed business is never served from a stale browser or proxy copy.
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Vary = new StringValues("Accept-Encoding");
        if (HttpMethods.IsHead(ctx.Request.Method)) return;
        await ctx.Response.WriteAsync(html, Encoding.UTF8, ctx.RequestAborted);
    }

    /// <summary>The page with its default title and description replaced by the page's head, and the rendered content in the root element.</summary>
    public static string Inject(string template, string head, string body, string language)
    {
        var html = template;
        html = ReplaceBetween(html, "<title>", "</title>", "", removeTags: true);
        var desc = html.IndexOf("<meta name=\"description\"", StringComparison.OrdinalIgnoreCase);
        if (desc >= 0)
        {
            var end = html.IndexOf('>', desc);
            if (end > desc) html = html.Remove(desc, end - desc + 1);
        }
        var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd >= 0) html = html.Insert(headEnd, head);
        html = html.Replace("<div id=\"root\"></div>", $"<div id=\"root\">{body}</div>", StringComparison.Ordinal);
        var lang = html.IndexOf("<html lang=\"", StringComparison.OrdinalIgnoreCase);
        if (lang >= 0)
        {
            var start = lang + "<html lang=\"".Length;
            var close = html.IndexOf('"', start);
            if (close > start) html = html[..start] + language + html[close..];
        }
        return html;
    }

    private static string ReplaceBetween(string html, string open, string close, string with, bool removeTags)
    {
        var a = html.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (a < 0) return html;
        var b = html.IndexOf(close, a, StringComparison.OrdinalIgnoreCase);
        if (b < 0) return html;
        return removeTags ? html[..a] + with + html[(b + close.Length)..] : html[..(a + open.Length)] + with + html[b..];
    }

    /// <summary>index.html, re-read when a new build replaces it.</summary>
    private async Task<string> TemplateAsync(CancellationToken ct)
    {
        var written = File.GetLastWriteTimeUtc(_indexPath);
        if (_template is null || written != _templateWritten)
        {
            _template = await File.ReadAllTextAsync(_indexPath, ct);
            _templateWritten = written;
        }
        return _template;
    }
}
