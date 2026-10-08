using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Seo;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CallingBell.Seo.Tests;

/// <summary>Business records shaped like the API's, for SEO tests.</summary>
internal static class TestData
{
    public static SeoOptions Options => new() { SiteUrl = "https://www.callingbell.com", SiteName = "Calling Bell", Language = "en" };

    public static SeoMetadataService Service() =>
        new(null!, null!, Microsoft.Extensions.Options.Options.Create(Options), new SeoCache(new MemoryCache(new MemoryCacheOptions())));

    /// <summary>A complete record: contact, address, coordinates, hours, services (one priced), reviews and a website.</summary>
    public static BusinessDetailDto Full(string name = "Mr. Electric of San Antonio", string slug = "mr-electric-san-antonio") => new()
    {
        Card = new BusinessCardDto
        {
            Id = Guid.NewGuid(), Slug = slug, Name = name, Tagline = "Licensed electricians", CategoryName = "Home Services", CategorySlug = "home-services",
            SubCategoryName = "Electricians", SubCategorySlug = "electrical", City = "San Antonio", Area = "Alamo Heights", AverageRating = 4.6m, ReviewCount = 23,
            LogoUrl = "/api/media/logo-1", IsVerified = true, OffersHomeService = true,
        },
        Description = "Residential and commercial electrical repairs, panel upgrades and lighting.",
        AddressLine = "5130 Broadway, Alamo Heights", Landmark = "Near Alamo Quarry Market", Pincode = "78209",
        Latitude = 29.4894m, Longitude = -98.4597m, PhoneNumber = "+1 210 555 0142", Email = "hello@example.com", Website = "https://www.example.com",
        CitySlug = "san-antonio", State = "Texas", CountryCode = "US", VerifiedOn = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        Services =
        [
            new ServiceDto(Guid.NewGuid(), "Panel upgrade", "Replace an old breaker panel.", 1450m, "per job", 240, "OnSite", null, true),
            new ServiceDto(Guid.NewGuid(), "Inspection", "Whole-home safety inspection.", 0m, null, 60, "OnSite", null, false),
        ],
        Hours =
        [
            new HoursDto(1, "Monday", "08:00:00", "18:00:00", false, false),
            new HoursDto(0, "Sunday", null, null, true, false),
        ],
        SocialLinks = [new SocialLinkDto("Facebook", "https://facebook.com/example")],
        RecentReviews = [new ReviewDto(Guid.NewGuid(), 5, "Quick and tidy", "Fixed our panel the same day.", "Ana R.", DateTimeOffset.UtcNow, null, null, true, 2)],
    };

    /// <summary>A record with only a name, category and city: nothing else may appear in the structured data.</summary>
    public static BusinessDetailDto Minimal() => new()
    {
        Card = new BusinessCardDto
        {
            Id = Guid.NewGuid(), Slug = "sparkle-cleaners", Name = "Sparkle Cleaners", CategoryName = "Home Services", CategorySlug = "home-services",
            SubCategoryName = "Cleaning", SubCategorySlug = "cleaning", City = "Pune",
        },
        CitySlug = "pune",
    };
}
