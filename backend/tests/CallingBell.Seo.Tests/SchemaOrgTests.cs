using System.Text.Json;
using CallingBell.Application.Features.Seo;

namespace CallingBell.Seo.Tests;

public class SchemaOrgTests
{
    private const string Url = "https://www.callingbell.com/business/mr-electric-san-antonio";

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Full_business_has_its_real_details()
    {
        var json = SchemaOrg.Serialize(SchemaOrg.LocalBusiness(TestData.Full(), Url, "United States", "Texas", ["https://www.callingbell.com/api/media/logo-1"]));
        var ld = Parse(json);
        Assert.Equal("https://schema.org", ld.GetProperty("@context").GetString());
        Assert.Equal("Electrician", ld.GetProperty("@type").GetString());
        Assert.Equal(Url + "#business", ld.GetProperty("@id").GetString());
        Assert.Equal("+1 210 555 0142", ld.GetProperty("telephone").GetString());
        var address = ld.GetProperty("address");
        Assert.Equal("5130 Broadway, Alamo Heights", address.GetProperty("streetAddress").GetString());
        Assert.Equal("San Antonio", address.GetProperty("addressLocality").GetString());
        Assert.Equal("Texas", address.GetProperty("addressRegion").GetString());
        Assert.Equal("78209", address.GetProperty("postalCode").GetString());
        Assert.Equal("US", address.GetProperty("addressCountry").GetString());
        Assert.Equal(29.4894m, ld.GetProperty("geo").GetProperty("latitude").GetDecimal());
        Assert.Equal(4.6m, ld.GetProperty("aggregateRating").GetProperty("ratingValue").GetDecimal());
        Assert.Equal(23, ld.GetProperty("aggregateRating").GetProperty("reviewCount").GetInt32());
        Assert.Equal(1, ld.GetProperty("review").GetArrayLength());
        // One open day only: the closed Sunday is left out.
        var hours = ld.GetProperty("openingHoursSpecification");
        Assert.Equal(1, hours.GetArrayLength());
        Assert.Equal("https://schema.org/Monday", hours[0].GetProperty("dayOfWeek").GetString());
        Assert.Equal("08:00", hours[0].GetProperty("opens").GetString());
        // The priced service carries its price in the business's currency; the unpriced one has no price.
        var offers = ld.GetProperty("hasOfferCatalog").GetProperty("itemListElement");
        Assert.Equal("1450", offers[0].GetProperty("price").GetString());
        Assert.Equal("USD", offers[0].GetProperty("priceCurrency").GetString());
        Assert.False(offers[1].TryGetProperty("price", out _));
        var sameAs = ld.GetProperty("sameAs").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("https://www.example.com", sameAs);
        Assert.Contains("https://facebook.com/example", sameAs);
    }

    [Fact]
    public void Missing_data_is_never_invented()
    {
        var ld = Parse(SchemaOrg.Serialize(SchemaOrg.LocalBusiness(TestData.Minimal(), Url, null, null, [])));
        foreach (var key in new[] { "telephone", "email", "geo", "aggregateRating", "review", "openingHoursSpecification", "hasOfferCatalog", "image", "sameAs", "foundingDate" })
            Assert.False(ld.TryGetProperty(key, out _), $"{key} must not be present without data");
        Assert.Equal("Sparkle Cleaners", ld.GetProperty("name").GetString());
        Assert.Equal("Pune", ld.GetProperty("address").GetProperty("addressLocality").GetString());
    }

    [Fact]
    public void Ratings_need_genuine_reviews()
    {
        var b = TestData.Full();
        var noReviews = b with { Card = b.Card with { ReviewCount = 0, AverageRating = 0 }, RecentReviews = [] };
        var ld = Parse(SchemaOrg.Serialize(SchemaOrg.LocalBusiness(noReviews, Url, null, null, [])));
        Assert.False(ld.TryGetProperty("aggregateRating", out _));
        Assert.False(ld.TryGetProperty("review", out _));
    }

    [Fact]
    public void Database_text_cannot_close_the_script_element()
    {
        var b = TestData.Full("</script><script>alert(1)</script> & \"Co\"");
        var json = SchemaOrg.Serialize(SchemaOrg.LocalBusiness(b, Url, null, null, []));
        Assert.DoesNotContain("</script", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", json);
        Assert.Equal("</script><script>alert(1)</script> & \"Co\"", Parse(json).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("Electricians", "Electrician")]
    [InlineData("Plumbers", "Plumber")]
    [InlineData("AC Repair", "HVACBusiness")]
    [InlineData("Dentists", "Dentist")]
    [InlineData("Restaurants", "Restaurant")]
    [InlineData("Beauty Salons", "BeautySalon")]
    [InlineData("Spa & Massage", "DaySpa")]
    [InlineData("Co-working Space", "LocalBusiness")]
    [InlineData("Car Repair", "AutoRepair")]
    [InlineData("Real Estate Agents", "RealEstateAgent")]
    [InlineData("Lawyers", "LegalService")]
    [InlineData("Astrologers", "LocalBusiness")]
    public void Business_type_is_the_most_specific_that_clearly_fits(string category, string type) =>
        Assert.Equal(type, SchemaOrg.BusinessType(category, null));

    [Fact]
    public void Organization_website_breadcrumbs_and_faq_are_valid()
    {
        var o = TestData.Options;
        var org = Parse(SchemaOrg.Serialize(SchemaOrg.Organization(o)));
        Assert.Equal("Organization", org.GetProperty("@type").GetString());
        Assert.False(org.TryGetProperty("sameAs", out _)); // no official profiles configured: none claimed
        var site = Parse(SchemaOrg.Serialize(SchemaOrg.WebSite(o)));
        Assert.Equal("https://www.callingbell.com/search?q={search_term_string}",
            site.GetProperty("potentialAction").GetProperty("target").GetProperty("urlTemplate").GetString());
        var crumbs = Parse(SchemaOrg.Serialize(SchemaOrg.Breadcrumbs([new SeoLink("Home", "/"), new SeoLink("India", "/location/india")], o.SiteUrl)));
        Assert.Equal(2, crumbs.GetProperty("itemListElement")[1].GetProperty("position").GetInt32());
        Assert.Equal("https://www.callingbell.com/location/india", crumbs.GetProperty("itemListElement")[1].GetProperty("item").GetString());
        var faq = Parse(SchemaOrg.Serialize(SchemaOrg.Faq([new SeoFaq("Q?", "A.")])));
        Assert.Equal("A.", faq.GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString());
    }
}
