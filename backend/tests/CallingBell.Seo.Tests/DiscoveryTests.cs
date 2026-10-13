using CallingBell.Application.Features.Seo;
using Microsoft.Extensions.Options;

namespace CallingBell.Seo.Tests;

/// <summary>Search and AI discovery: IndexNow settings and the sitemap index.</summary>
public sealed class DiscoveryTests
{
    [Theory]
    [InlineData(true, "3f9c2b7e8a1d4c6f9e0b5a7d2c4e6f81", true)]
    [InlineData(true, "a1b2-c3d4-e5f6", true)]
    [InlineData(false, "3f9c2b7e8a1d4c6f9e0b5a7d2c4e6f81", false)]   // switched off
    [InlineData(true, "short", false)]                                 // under 8 characters
    [InlineData(true, "has space in it", false)]
    [InlineData(true, "../../etc/passwd", false)]
    [InlineData(true, null, false)]
    public void IndexNow_needs_to_be_enabled_with_a_well_formed_key(bool enabled, string? key, bool valid) =>
        Assert.Equal(valid, new IndexNowOptions { Enabled = enabled, Key = key }.IsValid);

    [Fact]
    public void Sitemap_index_gives_each_file_its_own_last_change()
    {
        var service = new SitemapService(null!, Options.Create(new SeoOptions { SiteUrl = "https://www.callinbell.com" }), null!);
        var xml = service.IndexXml([("sitemap-static", null), ("sitemap-businesses-1", new DateTimeOffset(2026, 10, 2, 22, 30, 0, TimeSpan.FromHours(-5)))]);

        Assert.Contains("<sitemap><loc>https://www.callinbell.com/sitemap-static.xml</loc></sitemap>", xml);
        // Written in UTC: 22:30 at UTC-5 is the next day.
        Assert.Contains("<loc>https://www.callinbell.com/sitemap-businesses-1.xml</loc><lastmod>2026-10-03</lastmod>", xml);
    }
}
