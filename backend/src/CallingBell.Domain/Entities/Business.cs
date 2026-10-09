using CallingBell.Domain.Common;

namespace CallingBell.Domain.Entities;

public class Business : AuditableEntity
{
    public string OwnerUserId { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    public string Description { get; set; } = string.Empty;

    // Location. City/Area text columns pre-date the location tables and are kept denormalised for display.
    public Guid? CityId { get; set; }
    public Guid? AreaId { get; set; }
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string? AddressLine { get; set; }
    public string? Landmark { get; set; }
    public string? Pincode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // Contact
    public string? PhoneNumber { get; set; }
    public string? WhatsAppNumber { get; set; }

    /// <summary>
    /// Where the listing was created from with "Join Calling Bell": "google" (Google Maps) or "osm" (OpenStreetMap), and the place's id
    /// there (a Google place id, or "node/123"). Only the id is kept; one place can become only one Calling Bell business.
    /// </summary>
    public string? SourceProvider { get; set; }
    public string? SourceExternalId { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }

    // Media
    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }

    // Profile
    public int? YearEstablished { get; set; }
    public int? TeamSize { get; set; }
    public string? Languages { get; set; }
    public int? ResponseTimeMinutes { get; set; }
    public bool AcceptsOnlineBooking { get; set; }
    public bool OffersVideoConsultation { get; set; }
    public bool OffersHomeService { get; set; }

    // Status
    public string Status { get; set; } = "PendingApproval";
    public string VerificationStatus { get; set; } = "Pending";
    public DateTimeOffset? VerifiedOn { get; set; }
    public bool IsFeatured { get; set; }

    // Real-time availability
    public string AvailabilityStatus { get; set; } = "Offline";
    public DateTimeOffset? LastSeenOn { get; set; }

    // Denormalised review aggregates, recalculated whenever a review changes.
    public decimal AverageRating { get; set; }
    public int ReviewCount { get; set; }

    public ApplicationUser Owner { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public SubCategory? SubCategory { get; set; }
    public City? CityRef { get; set; }
    public Area? AreaRef { get; set; }
    public ICollection<BusinessService> Services { get; set; } = new List<BusinessService>();
    public ICollection<BusinessHour> Hours { get; set; } = new List<BusinessHour>();
    public ICollection<BusinessImage> Images { get; set; } = new List<BusinessImage>();
    public ICollection<BusinessVideo> Videos { get; set; } = new List<BusinessVideo>();
    public ICollection<BusinessSocialLink> SocialLinks { get; set; } = new List<BusinessSocialLink>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Advertisement> Advertisements { get; set; } = new List<Advertisement>();
    public ICollection<BusinessSubscription> Subscriptions { get; set; } = new List<BusinessSubscription>();
}

public class BusinessService : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? PriceUnit { get; set; }
    public int DurationMinutes { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsPopular { get; set; }
    public bool IsActive { get; set; } = true;

    public Business Business { get; set; } = null!;
}

public class BusinessHour : BaseEntity
{
    public Guid BusinessId { get; set; }
    /// <summary>0 = Sunday … 6 = Saturday, matching <see cref="System.DayOfWeek"/>.</summary>
    public byte DayOfWeek { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public bool IsClosed { get; set; }

    public Business Business { get; set; } = null!;
}

public class BusinessImage : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? MobileImageUrl { get; set; }
    public string? DesktopImageUrl { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }

    public Business Business { get; set; } = null!;
}

/// <summary>Promotional video uploaded by the business. Bytes live in <see cref="Media"/> (EntityType BusinessVideo).</summary>
public class BusinessVideo : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int? DurationSeconds { get; set; }
    public int SortOrder { get; set; }

    public Business Business { get; set; } = null!;
}

/// <summary>A social media profile; <see cref="Platform"/> is a SocialPlatform lookup code.</summary>
public class BusinessSocialLink : AuditableEntity
{
    public Guid BusinessId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    public Business Business { get; set; } = null!;
}

public class BusinessDailyStat : BaseEntity
{
    public Guid BusinessId { get; set; }
    public DateTime StatDate { get; set; }
    public int ProfileViews { get; set; }
    public int SearchImpressions { get; set; }
    public int CallClicks { get; set; }
    public int WhatsAppClicks { get; set; }
    public int DirectionRequests { get; set; }

    public Business Business { get; set; } = null!;
}

public class PlatformDailyStat : BaseEntity
{
    public DateTime StatDate { get; set; }
    public int ActiveUsers { get; set; }
    /// <summary>Rolling 30-day distinct users, computed by the analytics pipeline.</summary>
    public int MonthlyActiveUsers { get; set; }
    public int NewUsers { get; set; }
    public int Sessions { get; set; }
    public int Searches { get; set; }
}

/// <summary>
/// A slug a business used to have. Its old address answers with a permanent redirect to the current one, so links and search results
/// that point at it keep working. Rows are written by a database trigger whenever Businesses.Slug changes (any code path).
/// </summary>
public class BusinessSlugHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; }

    public Business Business { get; set; } = null!;
}

/// <summary>
/// "This business is mine": someone asks to take over a listing that already exists (found as a duplicate while joining Calling Bell).
/// An administrator verifies the person before transferring the listing.
/// </summary>
public class BusinessClaimRequest : AuditableEntity
{
    public string RequestNumber { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public string? UserId { get; set; }
    public string ClaimantName { get; set; } = string.Empty;
    public string ClaimantPhone { get; set; } = string.Empty;
    public string? ClaimantEmail { get; set; }
    public string? Message { get; set; }
    public string? SourceProvider { get; set; }
    public string? SourceExternalId { get; set; }
    /// <summary>Pending, Approved or Rejected.</summary>
    public string Status { get; set; } = "Pending";
    public string? IpAddress { get; set; }

    public Business Business { get; set; } = null!;
}
