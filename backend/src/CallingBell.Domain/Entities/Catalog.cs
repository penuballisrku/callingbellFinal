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

    public City City { get; set; } = null!;
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

    public Category Category { get; set; } = null!;
    public ICollection<Business> Businesses { get; set; } = new List<Business>();
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
