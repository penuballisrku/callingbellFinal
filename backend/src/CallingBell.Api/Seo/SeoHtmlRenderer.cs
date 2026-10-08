using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Seo;

namespace CallingBell.Api.Seo;

/// <summary>
/// HTML for a resolved SEO page: the head tags (title, description, canonical, robots, Open Graph, Twitter, JSON-LD), and a semantic,
/// server-rendered version of the page's content placed inside the app's root element. It is what the page shows - the same facts -
/// until the React app starts and renders over it, so crawlers and answer engines get the content without running JavaScript, and
/// visitors see content immediately. Nothing is hidden. Every value is HTML-encoded; JSON-LD is already escaped by its serialiser.
/// </summary>
public sealed class SeoHtmlRenderer(Microsoft.Extensions.Options.IOptions<SeoOptions> options)
{
    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public string Head(SeoDocument d)
    {
        var sb = new StringBuilder();
        sb.Append("<title>").Append(E(d.Title)).Append("</title>\n");
        Meta(sb, "name", "description", d.Description);
        Meta(sb, "name", "robots", d.Robots);
        sb.Append("    <link rel=\"canonical\" href=\"").Append(E(d.CanonicalUrl)).Append("\" data-seo />\n");
        Meta(sb, "property", "og:type", d.OgType);
        Meta(sb, "property", "og:site_name", options.Value.SiteName);
        Meta(sb, "property", "og:title", d.Title);
        Meta(sb, "property", "og:description", d.Description);
        Meta(sb, "property", "og:url", d.CanonicalUrl);
        Meta(sb, "property", "og:locale", d.Language.Replace('-', '_'));
        Meta(sb, "name", "twitter:card", d.ImageUrl is null ? "summary" : "summary_large_image");
        Meta(sb, "name", "twitter:title", d.Title);
        Meta(sb, "name", "twitter:description", d.Description);
        if (d.ImageUrl is { } image)
        {
            Meta(sb, "property", "og:image", image);
            Meta(sb, "name", "twitter:image", image);
            if (d.ImageAlt is { } alt)
            {
                Meta(sb, "property", "og:image:alt", alt);
                Meta(sb, "name", "twitter:image:alt", alt);
            }
        }
        if (d.LastModified is { } m) Meta(sb, "property", "og:updated_time", m.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        foreach (var json in d.JsonLd) sb.Append("    <script type=\"application/ld+json\" data-seo>").Append(json).Append("</script>\n");
        return sb.ToString();
    }

    public string Body(SeoPage page)
    {
        var sb = new StringBuilder("<main class=\"container-page py-8\" data-prerendered>");
        if (page.Document.Breadcrumbs.Count > 1)
        {
            sb.Append("<nav aria-label=\"Breadcrumb\" class=\"mb-3 text-xs text-muted\"><ol class=\"flex flex-wrap gap-1\">");
            foreach (var c in page.Document.Breadcrumbs) sb.Append("<li><a href=\"").Append(E(c.Url)).Append("\">").Append(E(c.Name)).Append("</a></li>");
            sb.Append("</ol></nav>");
        }
        if (page.Business is { } b) BusinessBody(sb, b, page.Content);
        else if (page.Landing is { } l) LandingBody(sb, l, page.Content);
        else
        {
            sb.Append("<h1 class=\"text-3xl font-bold\">").Append(E(page.Content.Heading ?? page.Document.Title)).Append("</h1>");
            if (page.Content.Summary is { } s) sb.Append("<p class=\"mt-3 max-w-3xl text-ink-2\">").Append(E(s)).Append("</p>");
            Links(sb, page.Content.Links);
            Faq(sb, page.Content.Faq);
        }
        return sb.Append("</main>").ToString();
    }

    private static void BusinessBody(StringBuilder sb, BusinessDetailDto b, SeoPageContent content)
    {
        var c = b.Card;
        sb.Append("<article>");
        sb.Append("<header class=\"flex items-center gap-4\">");
        if (!string.IsNullOrWhiteSpace(c.LogoUrl))
            sb.Append("<img src=\"").Append(E(c.LogoUrl)).Append("\" alt=\"").Append(E($"{c.Name} logo")).Append("\" width=\"96\" height=\"96\" class=\"h-24 w-24 rounded-xl\" />");
        sb.Append("<div><h1 class=\"text-3xl font-bold\">").Append(E(c.Name)).Append("</h1>");
        sb.Append("<p class=\"text-muted\">").Append(E(c.SubCategoryName ?? c.CategoryName));
        if (!string.IsNullOrWhiteSpace(c.Area)) sb.Append(" · ").Append(E(c.Area));
        sb.Append(", ").Append(E(c.City));
        if (c.ReviewCount > 0)
            sb.Append(" · Rated ").Append(c.AverageRating.ToString("0.0", CultureInfo.InvariantCulture)).Append(" out of 5 from ").Append(c.ReviewCount)
                .Append(c.ReviewCount == 1 ? " review" : " reviews");
        sb.Append("</p>");
        if (!string.IsNullOrWhiteSpace(c.Tagline)) sb.Append("<p>").Append(E(c.Tagline)).Append("</p>");
        sb.Append("</div></header>");

        sb.Append("<section class=\"mt-6\"><h2 class=\"text-xl font-semibold\">").Append(E(content.Heading ?? $"About {c.Name}")).Append("</h2>");
        if (content.Summary is { } summary) sb.Append("<p class=\"mt-2\">").Append(E(summary)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(b.Description)) sb.Append("<p class=\"mt-2\">").Append(E(b.Description)).Append("</p>");
        sb.Append("</section>");

        sb.Append("<section class=\"mt-6\"><h2 class=\"text-xl font-semibold\">Contact and location</h2><address class=\"mt-2 not-italic\">");
        var street = SeoText.JoinAddress(b.AddressLine, c.Area, c.City, b.State, b.Pincode);
        if (street.Length > 0) sb.Append(E(street)).Append("<br />");
        if (!string.IsNullOrWhiteSpace(b.Landmark)) sb.Append(E(SeoText.Near(b.Landmark))).Append("<br />");
        if (!string.IsNullOrWhiteSpace(b.PhoneNumber))
            sb.Append("Phone: <a href=\"tel:").Append(E(b.PhoneNumber.Replace(" ", ""))).Append("\">").Append(E(b.PhoneNumber)).Append("</a><br />");
        if (!string.IsNullOrWhiteSpace(b.Email)) sb.Append("Email: <a href=\"mailto:").Append(E(b.Email)).Append("\">").Append(E(b.Email)).Append("</a><br />");
        if (!string.IsNullOrWhiteSpace(b.Website) && Uri.TryCreate(b.Website, UriKind.Absolute, out _))
            sb.Append("Website: <a href=\"").Append(E(b.Website)).Append("\" rel=\"noopener\">").Append(E(b.Website)).Append("</a><br />");
        foreach (var s in b.SocialLinks.Where(s => Uri.TryCreate(s.Url, UriKind.Absolute, out _)))
            sb.Append("<a href=\"").Append(E(s.Url)).Append("\" rel=\"noopener nofollow ugc\">").Append(E(s.Platform)).Append("</a> ");
        sb.Append("</address></section>");

        if (b.Hours.Count > 0)
        {
            sb.Append("<section class=\"mt-6\"><h2 class=\"text-xl font-semibold\">Opening hours</h2><table class=\"mt-2\"><tbody>");
            foreach (var h in b.Hours)
                sb.Append("<tr><th scope=\"row\" class=\"pr-4 text-left font-normal\">").Append(E(h.Day)).Append("</th><td>")
                    .Append(h.IsClosed || h.Open is null || h.Close is null ? "Closed" : E($"{h.Open[..5]} - {h.Close[..5]}")).Append("</td></tr>");
            sb.Append("</tbody></table></section>");
        }

        if (b.Services.Count > 0)
        {
            sb.Append("<section class=\"mt-6\"><h2 class=\"text-xl font-semibold\">Services</h2><ul class=\"mt-2 list-disc pl-5\">");
            foreach (var s in b.Services) sb.Append("<li><strong>").Append(E(s.Name)).Append("</strong>")
                .Append(string.IsNullOrWhiteSpace(s.Description) ? "" : " - " + E(s.Description)).Append("</li>");
            sb.Append("</ul></section>");
        }

        if (b.RecentReviews.Count > 0)
        {
            sb.Append("<section class=\"mt-6\"><h2 class=\"text-xl font-semibold\">Reviews</h2><ul class=\"mt-2 space-y-3\">");
            foreach (var r in b.RecentReviews.Take(5))
                sb.Append("<li><p><strong>").Append(r.Rating).Append(" out of 5</strong>")
                    .Append(string.IsNullOrWhiteSpace(r.Title) ? "" : " - " + E(r.Title)).Append("</p><p>").Append(E(r.Comment)).Append("</p><p class=\"text-xs text-muted\">")
                    .Append(E(r.CustomerName)).Append(", <time datetime=\"").Append(r.CreatedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\">")
                    .Append(r.CreatedOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)).Append("</time></p></li>");
            sb.Append("</ul></section>");
        }

        Faq(sb, content.Faq);
        Links(sb, content.Links);
        if (content.UpdatedOn is { } updated)
            sb.Append("<p class=\"mt-6 text-xs text-muted\">Business information last updated: <time datetime=\"")
                .Append(updated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\">")
                .Append(updated.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)).Append("</time></p>");
        sb.Append("</article>");
    }

    private static void LandingBody(StringBuilder sb, LandingPageDto l, SeoPageContent content)
    {
        sb.Append("<header><h1 class=\"text-3xl font-bold\">").Append(E(l.Heading)).Append("</h1>");
        sb.Append("<p class=\"mt-3 max-w-3xl\">").Append(E(l.Summary)).Append("</p></header>");
        if (l.Businesses.Count > 0)
        {
            sb.Append("<section class=\"mt-6\"><h2 class=\"text-xl font-semibold\">").Append(E(l.CategoryName is null ? "Businesses" : l.CategoryName))
                .Append("</h2><ol class=\"mt-2 space-y-2\">");
            foreach (var b in l.Businesses)
            {
                sb.Append("<li><a href=\"").Append(E(SeoPaths.Business(b.Slug))).Append("\"><strong>").Append(E(b.Name)).Append("</strong></a> - ")
                    .Append(E(b.SubCategoryName ?? b.CategoryName)).Append(", ")
                    .Append(E(string.IsNullOrWhiteSpace(b.Area) ? b.City : $"{b.Area}, {b.City}"));
                if (b.ReviewCount > 0)
                    sb.Append(" · ").Append(b.AverageRating.ToString("0.0", CultureInfo.InvariantCulture)).Append(" out of 5 (").Append(b.ReviewCount)
                        .Append(b.ReviewCount == 1 ? " review)" : " reviews)");
                sb.Append("</li>");
            }
            sb.Append("</ol></section>");
        }
        Links(sb, content.Links);
        Faq(sb, content.Faq);
    }

    private static void Links(StringBuilder sb, IReadOnlyList<SeoLinkGroup> groups)
    {
        foreach (var g in groups.Where(g => g.Links.Count > 0))
        {
            sb.Append("<nav class=\"mt-6\" aria-label=\"").Append(E(g.Title)).Append("\"><h2 class=\"text-lg font-semibold\">").Append(E(g.Title))
                .Append("</h2><ul class=\"mt-2 flex flex-wrap gap-x-4 gap-y-1\">");
            foreach (var link in g.Links)
                sb.Append("<li><a href=\"").Append(E(link.Url)).Append("\">").Append(E(link.Name)).Append("</a>")
                    .Append(link.Count is { } n ? $" ({n})" : "").Append("</li>");
            sb.Append("</ul></nav>");
        }
    }

    private static void Faq(StringBuilder sb, IReadOnlyList<SeoFaq> faq)
    {
        if (faq.Count == 0) return;
        sb.Append("<section class=\"mt-6\"><h2 class=\"text-xl font-semibold\">Frequently asked questions</h2><dl class=\"mt-2 space-y-3\">");
        foreach (var f in faq) sb.Append("<dt class=\"font-semibold\">").Append(E(f.Question)).Append("</dt><dd>").Append(E(f.Answer)).Append("</dd>");
        sb.Append("</dl></section>");
    }

    private static void Meta(StringBuilder sb, string attribute, string key, string value) =>
        sb.Append("    <meta ").Append(attribute).Append("=\"").Append(key).Append("\" content=\"").Append(E(value)).Append("\" data-seo />\n");

    private static string E(string? value) => Html.Encode(value ?? "");
}
