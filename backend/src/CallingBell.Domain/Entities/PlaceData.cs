using CallingBell.Domain.Common;

namespace CallingBell.Domain.Entities;

/// <summary>
/// A post office from India Post's All India Pincode Directory (data.gov.in, Government Open Data License - India), imported by the PIN
/// code directory agent. Gives every PIN code its district and state; areas take their district from it.
/// </summary>
public class PostOffice : AuditableEntity
{
    public string OfficeName { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    /// <summary>BO (branch), SO (sub) or HO (head office).</summary>
    public string? OfficeType { get; set; }
    /// <summary>"Delivery" or "Non Delivery".</summary>
    public string? Delivery { get; set; }
    public string? Division { get; set; }
    public string? Region { get; set; }
    public string? Circle { get; set; }
    public string District { get; set; } = string.Empty;
    public string StateName { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

/// <summary>
/// A named place (shop, clinic, office…) from OpenStreetMap, stored by the OpenStreetMap catalogue agent so searches don't wait on
/// Overpass. Data © OpenStreetMap contributors, under the Open Database License (ODbL): it must be shown with attribution.
/// </summary>
public class OsmPlace : AuditableEntity
{
    /// <summary>The OpenStreetMap element, e.g. "node/123" or "way/456".</summary>
    public string OsmRef { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>What it is, from its tags ("Dentist", "Hairdresser").</summary>
    public string? Kind { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? OpeningHours { get; set; }
    public string? Email { get; set; }
    public string? Description { get; set; }
    /// <summary>Short facts from its tags ("Wheelchair accessible", "Cuisine: South Indian"), '|' separated.</summary>
    public string? Features { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    /// <summary>The city whose import found it (null when found from another city's import first).</summary>
    public Guid? CityId { get; set; }
    /// <summary>When an import last found it; places no longer found are left out of searches after a while.</summary>
    public DateTimeOffset LastSeenOn { get; set; }

    public City? City { get; set; }
    public ICollection<OsmPlaceSubCategory> SubCategories { get; set; } = new List<OsmPlaceSubCategory>();
}

/// <summary>Which sub-categories an OpenStreetMap place was found for (by the sub-category's OpenStreetMap tags).</summary>
public class OsmPlaceSubCategory
{
    public Guid OsmPlaceId { get; set; }
    public Guid SubCategoryId { get; set; }

    public OsmPlace OsmPlace { get; set; } = null!;
    public SubCategory SubCategory { get; set; } = null!;
}

/// <summary>One import of a city's places for a sub-category: when it ran and what it found. The agent refreshes the oldest first.</summary>
public class OsmCityImport
{
    public Guid CityId { get; set; }
    public Guid SubCategoryId { get; set; }
    public DateTimeOffset ImportedOn { get; set; }
    public int PlaceCount { get; set; }
    /// <summary>Error from the last attempt, when it failed.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// A Google Maps place id seen in search results, with where and for what it was found. Google's terms allow storing place ids
/// indefinitely, but no other Places content (names, addresses, ratings, photos, hours): those are always fetched live.
/// </summary>
public class GooglePlaceRef
{
    public string PlaceId { get; set; } = string.Empty;
    public Guid? CityId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public DateTimeOffset FirstSeenOn { get; set; }
    public DateTimeOffset LastSeenOn { get; set; }
    public int SeenCount { get; set; }
}

/// <summary>
/// An Overture Maps place (open data: CDLA-Permissive 2.0, Apache 2.0 or CC0, which allow storing it): imported for a whole country from
/// Overture's releases (tools/overture/import_places.py, searched locally), or kept when the Open Places API returned it, so its details
/// and "Join CallInBell" work later.
/// </summary>
public class OpenPlace
{
    /// <summary>Overture's place id, without the "overture:" prefix.</summary>
    public string PlaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Overture category slug, e.g. "electrician".</summary>
    public string? Category { get; set; }
    /// <summary>Other categories, as ",slug,slug,".</summary>
    public string? AltCategories { get; set; }
    /// <summary>The category's place in Overture's taxonomy, e.g. "services_and_business/home_service/electrician".</summary>
    public string? CategoryPath { get; set; }
    /// <summary>The whole address on one line.</summary>
    public string? Address { get; set; }
    public string? Locality { get; set; }
    public string? Region { get; set; }
    public string? Postcode { get; set; }
    public string? CountryCode { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    /// <summary>0.1 degree grid cell of the position (<see cref="CellOf"/>), so searches read only the cells around a point.</summary>
    public int Cell { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    /// <summary>Overture's confidence that the place exists, 0 to 1.</summary>
    public decimal? Confidence { get; set; }
    /// <summary>Where Overture got it, e.g. "meta,Microsoft" (for attribution).</summary>
    public string? Datasets { get; set; }
    /// <summary>The Overture release a bulk import came from; null for places kept from API results.</summary>
    public string? Release { get; set; }
    public DateTimeOffset FirstSeenOn { get; set; }
    public DateTimeOffset LastSeenOn { get; set; }

    public const double CellSize = 0.1;

    /// <summary>The grid cell of a position; the import tool computes the same value.</summary>
    public static int CellOf(double latitude, double longitude) =>
        (int)Math.Floor((latitude + 90) / CellSize) * 10000 + (int)Math.Floor((longitude + 180) / CellSize);
}

/// <summary>A country's Overture places imported into <see cref="OpenPlace"/>, and the area they cover (searches inside it are local).</summary>
public class OpenPlacesImport
{
    public string CountryCode { get; set; } = string.Empty;
    public string Release { get; set; } = string.Empty;
    public int Places { get; set; }
    public decimal MinLatitude { get; set; }
    public decimal MaxLatitude { get; set; }
    public decimal MinLongitude { get; set; }
    public decimal MaxLongitude { get; set; }
    public DateTimeOffset ImportedOn { get; set; }
}
