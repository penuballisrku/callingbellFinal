using System.Globalization;
using System.Text.RegularExpressions;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using FluentValidation;
using MediatR;

namespace CallingBell.Application.Features.Search;

/// <summary>Any other field the source returned that has no place of its own in the detail window (e.g. OpenStreetMap "wheelchair: yes").</summary>
public sealed record PlaceFieldDto(string Label, string Value);

/// <summary>
/// Everything known about a place found outside Calling Bell (a Google Maps or AI recommended search result), combined from its sources
/// for the result detail window. Fields a source doesn't have are null or empty; nothing is filled in by guesswork.
/// </summary>
/// <param name="Source">"google" or "osm" (the AI recommended places come from OpenStreetMap).</param>
/// <param name="SourceId">The place's id at its source: a Google place id, or "node/123" in OpenStreetMap.</param>
/// <param name="GooglePlaceId">For an OpenStreetMap place, the Google Maps place it was matched to (by name, within 300 m), if any.</param>
/// <param name="Phone">National format; <paramref name="Mobile"/> is a separately listed mobile number.</param>
/// <param name="OpeningHours">One line per day or rule, as the source gives them.</param>
/// <param name="Photos">Google Maps photos, served through <c>GET /api/places/photo</c>, each with the credit Google requires.</param>
/// <param name="DataSources">Where the details came from, for attribution, e.g. ["OpenStreetMap", "Google Maps"].</param>
public sealed record PlaceDetailsDto(string Source, string SourceId, string Name, string? Category, IReadOnlyList<string> Tags, string? Description,
    string? Address, string? City, string? State, string? Country, string? PostalCode, double? Latitude, double? Longitude,
    string? Phone, string? InternationalPhone, string? Mobile, string? Email, string? Website, IReadOnlyList<SocialLinkDto> SocialLinks,
    IReadOnlyList<string> OpeningHours, bool? OpenNow, decimal? Rating, int? RatingCount, string? PriceLevel, string? BusinessStatus,
    IReadOnlyList<GooglePlacePhotoDto> Photos, string? MapsUrl, string? SourceUrl, string? GooglePlaceId, IReadOnlyList<PlaceFieldDto> OtherFields,
    IReadOnlyList<string> DataSources);

/// <param name="Source">"google" or "osm".</param>
/// <param name="Id">Google place id, or an OpenStreetMap element ("node/123").</param>
/// <param name="Name">OpenStreetMap only: the place's name and position, to find the same place on Google Maps.</param>
public sealed record GetPlaceDetailsQuery(string? Source, string? Id, string? Name, double? Lat, double? Lon) : IRequest<PlaceDetailsDto>;

public sealed partial class GetPlaceDetailsValidator : AbstractValidator<GetPlaceDetailsQuery>
{
    [GeneratedRegex("^[A-Za-z0-9_-]{10,300}$")] private static partial Regex GoogleId();
    [GeneratedRegex("^(node|way|relation)/[0-9]{1,15}$")] private static partial Regex OsmId();

    public GetPlaceDetailsValidator()
    {
        RuleFor(x => x.Source).Must(s => s is "google" or "osm").WithMessage("source must be \"google\" or \"osm\".");
        RuleFor(x => x.Id).NotEmpty().Must((q, id) => q.Source == "google" ? GoogleId().IsMatch(id ?? "") : OsmId().IsMatch(id ?? ""))
            .WithMessage("Invalid place id.");
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
    }
}

/// <summary>
/// A Google Maps place: Google's Place Details. An OpenStreetMap place: all its OpenStreetMap tags, plus the details of the same place on
/// Google Maps when one matches (phone, rating, photos, hours), with OpenStreetMap's own values taking precedence. Nothing is stored:
/// Google's terms don't allow keeping its data, and OpenStreetMap elements are only cached in memory.
/// </summary>
public sealed class GetPlaceDetailsHandler(IGooglePlacesSearch google, IOsmPlaceSearch osm) : IRequestHandler<GetPlaceDetailsQuery, PlaceDetailsDto>
{
    private const int MaxPhotos = 8;
    private const int PhotoWidthPx = 960;

    public async Task<PlaceDetailsDto> Handle(GetPlaceDetailsQuery r, CancellationToken ct)
    {
        if (r.Source == "google")
        {
            if (!google.IsEnabled) throw new ExternalServiceException(503, "Google Maps details are not available right now.");
            var g = await google.GetDetailsAsync(r.Id!, ct) ?? throw new NotFoundException("Place", r.Id!);
            return FromGoogle(g);
        }

        var element = await osm.GetElementAsync(r.Id!, ct);
        // The same place on Google Maps, for what OpenStreetMap rarely has (phone, rating, photos).
        GooglePlaceDetails? match = null;
        var lat = element?.Latitude ?? r.Lat;
        var lon = element?.Longitude ?? r.Lon;
        var name = element?.Tags.GetValueOrDefault("name") ?? r.Name;
        if (google.IsEnabled && name is not null && lat is { } la && lon is { } lo)
        {
            try
            {
                if (await google.FindContactAsync(name, la, lo, ct) is { Id: { } placeId }) match = await google.GetDetailsAsync(placeId, ct);
            }
            catch (ExternalServiceException) { /* Google unavailable: OpenStreetMap's details alone */ }
        }
        if (element is null && match is null)
            throw new ExternalServiceException(503, "OpenStreetMap is not responding right now. Please try again in a moment.");
        return FromOsm(r.Id!, name ?? match?.Name ?? "Place", lat, lon, element?.Tags ?? new Dictionary<string, string>(), match);
    }

    private static PlaceDetailsDto FromGoogle(GooglePlaceDetails g) => new(
        "google", g.Id, g.Name, g.PrimaryType, TypeNames(g.Types), g.Summary, g.FormattedAddress, g.City, g.State, g.Country, g.PostalCode,
        g.Latitude, g.Longitude, g.Phone, g.InternationalPhone, null, null, g.Website, [], g.OpeningHours, g.OpenNow, g.Rating, g.RatingCount,
        PriceLevel(g.PriceLevel), Status(g.BusinessStatus), Photos(g), g.MapsUrl, g.MapsUrl, g.Id, [], ["Google Maps"]);

    private static PlaceDetailsDto FromOsm(string id, string name, double? lat, double? lon, IReadOnlyDictionary<string, string> tags, GooglePlaceDetails? g)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        string? Tag(params string[] keys)
        {
            foreach (var k in keys)
                if (tags.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) { used.UnionWith(keys); return v.Trim(); }
            used.UnionWith(keys);
            return null;
        }

        var street = string.Join(' ', new[] { Tag("addr:housenumber"), Tag("addr:street") }.Where(s => s is not null));
        var osmAddress = Tag("addr:full") ?? string.Join(", ", new[] { street, Tag("addr:suburb", "addr:neighbourhood", "addr:place") }.Where(s => !string.IsNullOrEmpty(s)));
        var city = Tag("addr:city");
        var state = Tag("addr:state", "addr:province");
        var country = Tag("addr:country");
        var postcode = Tag("addr:postcode");
        var kind = Kind(tags, used);
        var phone = Tag("phone", "contact:phone");
        var mobile = Tag("mobile", "contact:mobile");
        var email = Tag("email", "contact:email");
        var website = Tag("website", "contact:website", "url");
        var hours = Tag("opening_hours");
        var description = Tag("description");
        Tag("name");

        var social = new List<SocialLinkDto>();
        foreach (var (platform, keys) in SocialKeys)
            if (Tag(keys) is { } handle) social.Add(new SocialLinkDto(platform, SocialUrl(platform, handle)));

        var other = tags.Where(t => !used.Contains(t.Key) && !Ignored(t.Key) && !string.IsNullOrWhiteSpace(t.Value))
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new PlaceFieldDto(Label(t.Key), Value(t.Value)))
            .ToList();

        var tagsList = new List<string>();
        if (kind is not null) tagsList.Add(kind);
        if (g is not null) tagsList.AddRange(TypeNames(g.Types));

        return new PlaceDetailsDto("osm", id, name, kind ?? g?.PrimaryType, tagsList.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            description ?? g?.Summary,
            string.IsNullOrWhiteSpace(osmAddress) ? g?.FormattedAddress : JoinAddress(osmAddress, city, postcode),
            city ?? g?.City, state ?? g?.State, country is { Length: 2 } cc ? CountryName(cc) : country ?? g?.Country, postcode ?? g?.PostalCode,
            lat ?? g?.Latitude, lon ?? g?.Longitude, phone ?? g?.Phone, g?.InternationalPhone, mobile, email, website ?? g?.Website, social,
            hours is not null ? hours.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : g?.OpeningHours ?? [],
            g?.OpenNow, g?.Rating, g?.RatingCount, PriceLevel(g?.PriceLevel), Status(g?.BusinessStatus), g is null ? [] : Photos(g),
            g?.MapsUrl, $"https://www.openstreetmap.org/{id}", g?.Id, other,
            g is null ? ["OpenStreetMap"] : ["OpenStreetMap", "Google Maps"]);
    }

    private static readonly (string Platform, string[] Keys)[] SocialKeys =
    [
        ("Facebook", ["contact:facebook", "facebook"]), ("Instagram", ["contact:instagram", "instagram"]),
        ("X", ["contact:twitter", "twitter", "contact:x"]), ("YouTube", ["contact:youtube", "youtube"]),
        ("LinkedIn", ["contact:linkedin", "linkedin"]), ("WhatsApp", ["contact:whatsapp", "whatsapp"]),
    ];

    private static string SocialUrl(string platform, string handle)
    {
        if (handle.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return handle;
        var h = handle.TrimStart('@');
        return platform switch
        {
            "Facebook" => $"https://www.facebook.com/{h}", "Instagram" => $"https://www.instagram.com/{h}", "X" => $"https://x.com/{h}",
            "YouTube" => $"https://www.youtube.com/@{h}", "LinkedIn" => $"https://www.linkedin.com/company/{h}",
            "WhatsApp" => $"https://wa.me/{new string(h.Where(char.IsDigit).ToArray())}", _ => handle,
        };
    }

    /// <summary>What the place is, from its OpenStreetMap type tags ("craft=electrician" becomes "Electrician").</summary>
    private static string? Kind(IReadOnlyDictionary<string, string> tags, HashSet<string> used)
    {
        foreach (var key in new[] { "craft", "office", "shop", "amenity", "healthcare", "tourism", "leisure", "sport", "emergency" })
            if (tags.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && v != "yes")
            {
                used.Add(key);
                return Humanize(v.Split(';')[0]);
            }
        return null;
    }

    /// <summary>Bookkeeping tags that mean nothing to a visitor (mapping sources, survey dates, alternative-language names).</summary>
    private static bool Ignored(string key) =>
        key.StartsWith("source", StringComparison.Ordinal) || key.StartsWith("check_date", StringComparison.Ordinal) || key.StartsWith("survey", StringComparison.Ordinal)
        || key.StartsWith("name:", StringComparison.Ordinal) || key.StartsWith("old_name", StringComparison.Ordinal) || key.StartsWith("ref:", StringComparison.Ordinal)
        || key.StartsWith("note", StringComparison.Ordinal) || key.StartsWith("fixme", StringComparison.OrdinalIgnoreCase)
        || key is "created_by" or "building" or "building:levels" or "layer" or "level" or "wikidata" or "wikipedia" or "brand:wikidata" or "brand:wikipedia";

    private static string Label(string key) => Humanize(key.Replace("contact:", "").Replace("addr:", "address ").Replace(':', ' '));

    private static string Value(string value) => value.Trim() switch { "yes" => "Yes", "no" => "No", "limited" => "Limited", var v => v.Replace(";", ", ") };

    private static string Humanize(string s)
    {
        var t = s.Replace('_', ' ').Trim();
        return t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static string JoinAddress(string line, string? city, string? postcode) =>
        string.Join(", ", new[] { line, city, postcode }.Where(s => !string.IsNullOrWhiteSpace(s) && !line.Contains(s!, StringComparison.OrdinalIgnoreCase)).Prepend(line).Distinct());

    private static string CountryName(string code)
    {
        try { return new RegionInfo(code).EnglishName; } catch (ArgumentException) { return code; }
    }

    /// <summary>Google's types as words ("beauty_salon" becomes "Beauty salon"), without the generic ones.</summary>
    private static List<string> TypeNames(IReadOnlyList<string> types) =>
        types.Where(t => t is not ("point_of_interest" or "establishment" or "store" or "service" or "premise")).Select(Humanize).Distinct().ToList();

    private static string? PriceLevel(string? level) => level switch
    {
        "PRICE_LEVEL_FREE" => "Free", "PRICE_LEVEL_INEXPENSIVE" => "Inexpensive", "PRICE_LEVEL_MODERATE" => "Moderate",
        "PRICE_LEVEL_EXPENSIVE" => "Expensive", "PRICE_LEVEL_VERY_EXPENSIVE" => "Very expensive", _ => null,
    };

    private static string? Status(string? status) => status switch
    {
        "OPERATIONAL" => "Open for business", "CLOSED_TEMPORARILY" => "Temporarily closed", "CLOSED_PERMANENTLY" => "Permanently closed", _ => null,
    };

    private static List<GooglePlacePhotoDto> Photos(GooglePlaceDetails g) => g.Photos.Take(MaxPhotos)
        .Select(ph => new GooglePlacePhotoDto($"/api/places/photo?name={Uri.EscapeDataString(ph.Name)}&maxWidth={PhotoWidthPx}", ph.WidthPx, ph.HeightPx,
            ph.Attributions))
        .ToList();
}
