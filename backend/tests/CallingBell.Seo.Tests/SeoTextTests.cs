using CallingBell.Application.Features.Seo;

namespace CallingBell.Seo.Tests;

public class SeoTextTests
{
    [Fact]
    public void Business_title_names_business_category_and_place()
    {
        var title = SeoText.BusinessTitle("Mr. Electric of San Antonio", "Electricians", "San Antonio", "Texas", "Calling Bell");
        Assert.Equal("Mr. Electric of San Antonio | Electricians in San Antonio, Texas | Calling Bell", title);
        Assert.True(title.Length <= SeoText.MaxTitle);
    }

    [Fact]
    public void Business_title_keeps_the_region_when_it_fits()
    {
        Assert.Equal("Coastal Curry House | Restaurants in Mumbai, Maharashtra | Calling Bell",
            SeoText.BusinessTitle("Coastal Curry House", "Restaurants", "Mumbai", "Maharashtra", "Calling Bell"));
    }

    [Fact]
    public void Region_then_site_name_give_way_before_the_category()
    {
        var name = "Sri Venkateswara Electrical and Plumbing Works";
        Assert.Equal($"{name} | Electricians in Visakhapatnam", SeoText.BusinessTitle(name, "Electricians", "Visakhapatnam", "Andhra Pradesh", "Calling Bell"));
    }

    [Fact]
    public void Long_business_names_are_shortened_without_cutting_words()
    {
        var title = SeoText.BusinessTitle(new string('x', 20) + " " + string.Join(' ', Enumerable.Repeat("Name", 20)), "Electricians", "San Antonio", "Texas", "Calling Bell");
        Assert.True(title.Length <= SeoText.MaxTitle);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Category_and_location_titles_follow_the_patterns()
    {
        Assert.Equal("Electricians in Hyderabad, Telangana | Local Electricians | Calling Bell",
            SeoText.CategoryTitle("Electricians", "Hyderabad, Telangana", "Calling Bell"));
        Assert.Equal("Local Services in San Antonio, Texas | Calling Bell", SeoText.LocationTitle("San Antonio, Texas", "Calling Bell"));
    }

    [Fact]
    public void Business_description_lists_only_what_the_page_has()
    {
        var full = SeoText.BusinessDescription("Acme", "Plumbers", "Pune", true, true, true, true, "Calling Bell");
        Assert.Contains("contact details, services, hours, reviews and location", full);
        var bare = SeoText.BusinessDescription("Acme", "Plumbers", "Pune", false, false, false, false, "Calling Bell");
        Assert.DoesNotContain("reviews", bare);
        Assert.DoesNotContain("contact", bare);
        Assert.Contains("location", bare);
        Assert.True(full.Length <= SeoText.MaxDescription);
    }

    [Fact]
    public void Category_description_states_the_real_count_and_handles_none()
    {
        Assert.Contains("12 listed businesses", SeoText.CategoryDescription("Plumbers", "Pune, Maharashtra", 12, "Calling Bell"));
        Assert.Contains("1 listed business.", SeoText.CategoryDescription("Plumbers", "Pune, Maharashtra", 1, "Calling Bell"));
        Assert.DoesNotContain("0 listed", SeoText.CategoryDescription("Plumbers", "Pune", 0, "Calling Bell"));
    }

    [Fact]
    public void Clamp_cuts_at_a_word_boundary()
    {
        var text = SeoText.Clamp("one two three four five six seven", 15);
        Assert.Equal("one two three…", text);
    }

    [Fact]
    public void Address_parts_are_not_repeated()
    {
        Assert.Equal("Gokhale Road, Dadar West, Mumbai, 400028", SeoText.JoinAddress("Gokhale Road, Dadar West", "Dadar West", "Mumbai", null, " 400028 "));
    }

    [Fact]
    public void Landmark_does_not_double_near()
    {
        Assert.Equal("Near Portuguese Church", SeoText.Near("Near Portuguese Church"));
        Assert.Equal("Near City Mall", SeoText.Near("City Mall"));
    }

    [Theory]
    [InlineData("Côte-des-Neiges–Notre-Dame-de-Grâce", "cote-des-neiges-notre-dame-de-grace")]
    [InlineData("  San Antonio ", "san-antonio")]
    [InlineData("A & B Electricals!", "a-b-electricals")]
    public void Slugify_makes_ascii_hyphenated_slugs(string text, string slug) => Assert.Equal(slug, SeoPaths.Slugify(text));

    [Fact]
    public void Country_slugs_and_location_paths()
    {
        Assert.Equal("united-states", SeoPaths.CountrySlug("US"));
        Assert.Equal("india", SeoPaths.CountrySlug("in"));
        Assert.Equal("/location/india/telangana/hyderabad/kukatpally/electrical",
            SeoPaths.Location("india", "telangana", "hyderabad", "kukatpally", "electrical"));
        Assert.Equal("/location/canada/quebec/montreal", SeoPaths.Location("canada", "quebec", "montreal", null, null));
        Assert.Equal("https://www.callingbell.com/business/x", SeoPaths.Absolute("https://www.callingbell.com/", "/business/x"));
    }

    [Theory]
    [InlineData("/Business/Mr-Electric/?utm_source=google", "/business/mr-electric")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    public void Paths_are_normalised_without_query_strings(string path, string expected) => Assert.Equal(expected, SeoMetadataService.Normalize(path));
}
