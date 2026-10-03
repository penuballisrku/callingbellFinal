using CallingBell.Domain.Common;

namespace CallingBell.Domain.Entities;

public class State : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<City> Cities { get; set; } = new List<City>();
}

public class City : AuditableEntity
{
    public Guid StateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsPopular { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>When the area-discovery agent last completed for this city (null = never).</summary>
    public DateTimeOffset? AreasDiscoveredOn { get; set; }
    /// <summary>Short summary or error from the last discovery run.</summary>
    public string? AreaDiscoveryNote { get; set; }

    public State State { get; set; } = null!;
    public ICollection<Area> Areas { get; set; } = new List<Area>();
}

public class Area : AuditableEntity
{
    public Guid CityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Area | Locality | Suburb | Town | Village | Neighbourhood (sub-locality, see <see cref="ParentAreaId"/>).</summary>
    public string? AreaType { get; set; }
    /// <summary>The area a sub-locality belongs to; null for top-level areas.</summary>
    public Guid? ParentAreaId { get; set; }
    /// <summary>Alternate and old spellings, '|' separated (used by search).</summary>
    public string? AltNames { get; set; }
    /// <summary>Null for curated areas; "osm" for areas maintained by the discovery agent.</summary>
    public string? Source { get; set; }
    /// <summary>Source record, e.g. the OpenStreetMap element "node/123".</summary>
    public string? ExternalRef { get; set; }
    public DateTimeOffset? LastVerifiedOn { get; set; }

    public City City { get; set; } = null!;
    public Area? ParentArea { get; set; }
}

public class Category : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? IconUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? AltText { get; set; }
    public string? ColorHex { get; set; }
    public int SortOrder { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SubCategory> SubCategories { get; set; } = new List<SubCategory>();
    public ICollection<Business> Businesses { get; set; } = new List<Business>();
}

public class SubCategory : AuditableEntity
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? IconUrl { get; set; }
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// OpenStreetMap selectors for finding places of this kind outside the platform, separated by "|":
    /// "key=value" tags (e.g. "craft=electrician") or "name~words" (name contains the words).
    /// </summary>
    public string? OsmTags { get; set; }

    public Category Category { get; set; } = null!;
    public ICollection<Business> Businesses { get; set; } = new List<Business>();
}

/// <summary>
/// Curated "Popular services" entry for the home page. <see cref="ServiceName"/> matches BusinessServices.Name within the
/// sub-category, so starting price, provider count and booking count are computed live from listings and bookings.
/// </summary>
public class PopularService : AuditableEntity
{
    public Guid SubCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    public string? BadgeText { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public SubCategory SubCategory { get; set; } = null!;
}

/// <summary>Generic, database-driven lookup list (statuses, types) with display metadata.</summary>
public class LookupValue : AuditableEntity
{
    public string LookupType { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ColorHex { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Binary image store. Entities reference images through /api/media/{id} URLs.</summary>
public class Media : ISoftDeletable
{
    public Guid MediaId { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public byte[] FileData { get; set; } = Array.Empty<byte>();
    public byte[]? ThumbnailData { get; set; }
    public string? AltText { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public bool IsDeleted { get; set; }
}
