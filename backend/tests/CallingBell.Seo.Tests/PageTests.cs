using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CallingBell.Api.Seo;
using CallingBell.Application.Features.Seo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CallingBell.Seo.Tests;

public class BusinessPageTests
{
    private static SeoPage Page(string? canonicalSlug = null) =>
        TestData.Service().BuildBusiness(TestData.Full(), "texas", "Texas", "US", "alamo-heights", canonicalSlug, DateTimeOffset.UtcNow);

    [Fact]
    public void Business_page_has_title_description_canonical_and_index()
    {
        var d = Page().Document;
        Assert.Equal("Mr. Electric of San Antonio | Electricians in San Antonio, Texas | Calling Bell", d.Title);
        Assert.StartsWith("Find Mr. Electric of San Antonio", d.Description);
        Assert.Equal("https://www.callingbell.com/business/mr-electric-san-antonio", d.CanonicalUrl);
        Assert.StartsWith("index,follow", d.Robots);
        Assert.Equal("https://www.callingbell.com/api/media/logo-1", d.ImageUrl);
    }

    [Fact]
    public void Breadcrumbs_follow_the_location_hierarchy()
    {
        var crumbs = Page().Document.Breadcrumbs.Select(c => (c.Name, c.Url)).ToList();
        Assert.Equal(
        [
            ("Home", "/"), ("United States", "/location/united-states"), ("Texas", "/location/united-states/texas"),
            ("San Antonio", "/location/united-states/texas/san-antonio"), ("Electricians", "/location/united-states/texas/san-antonio/electrical"),
            ("Mr. Electric of San Antonio", "/business/mr-electric-san-antonio"),
        ], crumbs);
    }

    [Fact]
    public void Structured_data_is_business_breadcrumbs_and_the_visible_faq()
    {
        var page = Page();
        var types = page.Document.JsonLd.Select(j => JsonDocument.Parse(j).RootElement.GetProperty("@type").GetString()).ToList();
        Assert.Equal(["Electrician", "BreadcrumbList", "FAQPage"], types);
        Assert.Contains(page.Content.Faq, f => f.Question == "How can I contact Mr. Electric of San Antonio?");
        Assert.Contains(page.Content.Faq, f => f.Answer.Contains("offers home visits"));
    }

    [Fact]
    public void Summary_and_links_come_from_the_record()
    {
        var c = Page().Content;
        Assert.Contains("listed on Calling Bell under Electricians in Alamo Heights, San Antonio, Texas", c.Summary);
        Assert.Contains("+1 210 555 0142", c.Summary);
        var links = c.Links.SelectMany(g => g.Links).Select(l => l.Url).ToList();
        Assert.Contains("/location/united-states/texas/san-antonio/electrical", links);
        Assert.Contains("/location/united-states/texas/san-antonio/alamo-heights/electrical", links);
        Assert.Contains("/category/electrical", links);
    }

    [Fact]
    public void A_duplicate_listing_points_to_the_original_and_is_not_indexed()
    {
        var d = Page(canonicalSlug: "mr-electric-of-san-antonio-original").Document;
        Assert.Equal("https://www.callingbell.com/business/mr-electric-of-san-antonio-original", d.CanonicalUrl);
        Assert.Equal("noindex,follow", d.Robots);
    }

    [Fact]
    public void Minimal_record_gets_no_invented_faq()
    {
        var page = TestData.Service().BuildBusiness(TestData.Minimal(), null, null, null, null, null, DateTimeOffset.UtcNow);
        Assert.DoesNotContain(page.Content.Faq, f => f.Question.StartsWith("How can I contact"));
        Assert.DoesNotContain(page.Content.Faq, f => f.Question.StartsWith("What are the opening hours"));
        Assert.DoesNotContain(page.Content.Faq, f => f.Question.StartsWith("What services"));
    }
}

public class SitemapAndHtmlTests
{
    [Fact]
    public void Sitemap_files_are_well_formed_and_escaped()
    {
        var xml = SitemapService.UrlSetXml([new SitemapEntry("https://www.callingbell.com/business/a&b", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))]);
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal("https://www.callingbell.com/business/a&b", doc.Root!.Element(ns + "url")!.Element(ns + "loc")!.Value);
        Assert.Equal("2026-10-01", doc.Root.Element(ns + "url")!.Element(ns + "lastmod")!.Value);
        Assert.Contains("&amp;", xml);
    }

    [Fact]
    public void Sitemap_index_lists_its_files()
    {
        var service = new SitemapService(null!, Microsoft.Extensions.Options.Options.Create(TestData.Options), null!);
        var doc = XDocument.Parse(service.IndexXml(["sitemap-static", "sitemap-businesses-1"]));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal(["https://www.callingbell.com/sitemap-static.xml", "https://www.callingbell.com/sitemap-businesses-1.xml"],
            doc.Root!.Elements(ns + "sitemap").Select(s => s.Element(ns + "loc")!.Value).ToList());
    }

    private const string Template = "<!doctype html><html lang=\"en-IN\"><head><meta name=\"description\" content=\"old\" /><title>Old</title></head>" +
                                    "<body><div id=\"root\"></div></body></html>";

    [Fact]
    public void Head_and_content_are_rendered_into_the_app_page()
    {
        var page = TestData.Service().BuildBusiness(TestData.Full(), "texas", "Texas", "US", "alamo-heights", null, DateTimeOffset.UtcNow);
        var renderer = new SeoHtmlRenderer(Microsoft.Extensions.Options.Options.Create(TestData.Options));
        var html = SpaSeoMiddleware.Inject(Template, renderer.Head(page.Document), renderer.Body(page), "en");
        Assert.DoesNotContain("<title>Old</title>", html);
        Assert.DoesNotContain("content=\"old\"", html);
        Assert.Contains("<title>Mr. Electric of San Antonio | Electricians in San Antonio, Texas | Calling Bell</title>", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://www.callingbell.com/business/mr-electric-san-antonio\"", html);
        Assert.Contains("<meta property=\"og:title\"", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"", html);
        Assert.Contains("<script type=\"application/ld+json\" data-seo>", html);
        Assert.Contains("<h1 class=\"text-3xl font-bold\">Mr. Electric of San Antonio</h1>", html);
        Assert.Contains("<address", html);
        Assert.Contains("href=\"tel:&#x2B;12105550142\"", html); // "+" is entity-encoded; browsers decode it
        Assert.Contains("alt=\"Mr. Electric of San Antonio logo\" width=\"96\" height=\"96\"", html);
        Assert.Contains("<html lang=\"en\">", html);
    }

    [Fact]
    public void Database_text_is_html_encoded()
    {
        var b = TestData.Full("<img src=x onerror=alert(1)> Co");
        var page = TestData.Service().BuildBusiness(b, "texas", "Texas", "US", null, null, DateTimeOffset.UtcNow);
        var renderer = new SeoHtmlRenderer(Microsoft.Extensions.Options.Options.Create(TestData.Options));
        var html = renderer.Head(page.Document) + renderer.Body(page);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img src=x", html);
    }

    /// <summary>A failing SEO service must not break the page: the plain app page is served.</summary>
    [Fact]
    public async Task Seo_failure_serves_the_plain_app_page()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), Template);
        var middleware = new SpaSeoMiddleware(_ => Task.CompletedTask, root, NullLogger<SpaSeoMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/business/anything";
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx, new ThrowingSeo(), new SeoHtmlRenderer(Microsoft.Extensions.Options.Options.Create(TestData.Options)));
        Assert.Equal(200, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        Assert.Contains("<div id=\"root\"></div>", Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray()));
    }

    [Fact]
    public async Task Moved_pages_answer_301_with_the_query_kept()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), Template);
        var middleware = new SpaSeoMiddleware(_ => Task.CompletedTask, root, NullLogger<SpaSeoMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/b/old";
        ctx.Request.QueryString = new QueryString("?welcome=1");
        await middleware.InvokeAsync(ctx, new RedirectingSeo("/business/new"), new SeoHtmlRenderer(Microsoft.Extensions.Options.Options.Create(TestData.Options)));
        Assert.Equal(301, ctx.Response.StatusCode);
        Assert.Equal("/business/new?welcome=1", ctx.Response.Headers.Location.ToString());
    }

    private sealed class ThrowingSeo : ISeoMetadataService
    {
        public Task<SeoPage> ResolveAsync(string path, string? query, CancellationToken ct) => throw new InvalidOperationException("database down");
    }

    private sealed class RedirectingSeo(string to) : ISeoMetadataService
    {
        public Task<SeoPage> ResolveAsync(string path, string? query, CancellationToken ct) => Task.FromResult(new SeoPage(SeoPageKind.Redirect, 301,
            new SeoDocument("Moved", "Moved", to, "noindex,follow", "en", "website", null, null, [], []), new SeoPageContent(null, null, [], []), to));
    }
}
