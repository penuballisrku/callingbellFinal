using System.Globalization;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CallingBell.Application.Features.Search;

/// <param name="Url">Served through <c>GET /api/places/photo</c>, so the Google API key stays on the server.</param>
/// <param name="Attributions">Photo authors; Google requires them to be shown with the photo.</param>
public sealed record GooglePlacePhotoDto(string Url, int? Width, int? Height, IReadOnlyList<GooglePhotoAttribution> Attributions);

/// <param name="DistanceKm">From the searched point.</param>
/// <param name="DirectionsUrl">Google Maps directions to the place.</param>
/// <param name="Id">Google place id, for <c>GET /api/places/details?source=google&amp;id=</c>.</param>
/// <param name="MapsUrl">The place's own Google Maps page (or a map search for it).</param>
/// <param name="Phone">As written locally; <paramref name="InternationalPhone"/> has the country code (for calls and WhatsApp).</param>
public sealed record GooglePlaceDto(string Name, string? Address, double? Rating, int? UserRatingCount, double? Latitude, double? Longitude,
    double? DistanceKm, string DirectionsUrl, IReadOnlyList<GooglePlacePhotoDto> Photos,
    string MapsUrl, string? Phone, string? InternationalPhone, bool? OpenNow, string? Id);

/// <summary>Where the search was centred.</summary>
/// <param name="Source">"coordinates" (lat/lon given), "area" (selected area), "city" (city centre) or "place" (a place found by name).</param>
public sealed record GooglePlacesLocationDto(double Lat, double Lon, string Source, string? AreaName, string? CityName, string? CitySlug);

/// <param name="Query">The text sent to Google, e.g. "Electricians in Madhapur, Hyderabad".</param>
/// <param name="NextPageToken">Pass back as <c>pageToken</c> (with the same query and location) for the next page; null on the last page.</param>
public sealed record GooglePlacesSearchDto(string Query, GooglePlacesLocationDto Location, IReadOnlyList<GooglePlaceDto> Places, string? NextPageToken);

/// <summary>Search near <paramref name="Lat"/>/<paramref name="Lon"/>, or near an area (<paramref name="AreaId"/>) or city (slug) from the database.</summary>
/// <param name="Place">A place by name that is not a listed city or area (e.g. "Nellore"); used when no coordinates are given.</param>
public sealed record GooglePlacesSearchQuery(string? TextQuery, double? Lat, double? Lon, string? City, Guid? AreaId, string? PageToken, string? Place = null)
    : IRequest<GooglePlacesSearchDto>;

public sealed class GooglePlacesSearchValidator : AbstractValidator<GooglePlacesSearchQuery>
{
    public GooglePlacesSearchValidator()
    {
        RuleFor(x => x.TextQuery).NotEmpty().WithMessage("Enter what to search for.").MaximumLength(200);
        RuleFor(x => x).Must(x => x.Lat != null || x.Lon != null || !string.IsNullOrWhiteSpace(x.City) || x.AreaId != null || !string.IsNullOrWhiteSpace(x.Place))
            .WithName("location").WithMessage("Provide lat and lon, or a city, area or place.");
        RuleFor(x => x.Place).MaximumLength(120);
        When(x => x.Lat != null || x.Lon != null, () =>
        {
            RuleFor(x => x.Lat).NotNull().WithMessage("Latitude is required.")
                .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");
            RuleFor(x => x.Lon).NotNull().WithMessage("Longitude is required.")
                .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");
        });
        RuleFor(x => x.City).MaximumLength(120);
        RuleFor(x => x.PageToken).MaximumLength(2048);
    }
}

/// <summary>
/// Google Places Text Search, one page at a time. With a city or area the search is centred on its coordinates from the database and
/// the place is named in the query ("Electricians in Madhapur, Hyderabad"), since Google only biases results towards the location.
/// </summary>
public sealed class GooglePlacesSearchHandler(IGooglePlacesSearch google, IUnitOfWork uow, IPlaceGeocoder geocoder, IPopularSearchCounter counter,
    ILogger<GooglePlacesSearchHandler> logger)
    : IRequestHandler<GooglePlacesSearchQuery, GooglePlacesSearchDto>
{
    private const int PhotoWidthPx = 640;
    private const int MaxPhotos = 5;

    public async Task<GooglePlacesSearchDto> Handle(GooglePlacesSearchQuery r, CancellationToken ct)
    {
        var location = await ResolveAsync(r, ct);
        var text = r.TextQuery!.Trim();
        var place = string.Join(", ", new[] { location.AreaName, location.CityName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (location.Source != "coordinates" && place.Length > 0) text = $"{text} in {place}";

        double lat = location.Lat, lon = location.Lon;
        var page = await google.TextSearchAsync(text, lat, lon, r.PageToken, ct);
        var places = page.Places.Select(p => new GooglePlaceDto(
                p.Name, p.FormattedAddress, p.Rating is { } rating ? Math.Round(rating, 1) : null, p.UserRatingCount, p.Latitude, p.Longitude,
                p is { Latitude: { } plat, Longitude: { } plon } ? Math.Round(GeoMath.HaversineKm(lat, lon, plat, plon), 1) : null,
                DirectionsUrl(p),
                p.Photos.Take(MaxPhotos).Select(ph => new GooglePlacePhotoDto(
                    $"/api/places/photo?name={Uri.EscapeDataString(ph.Name)}&maxWidth={PhotoWidthPx}", ph.WidthPx, ph.HeightPx, ph.Attributions)).ToList(),
                MapsUrl(p), p.Phone ?? p.InternationalPhone, p.InternationalPhone, p.OpenNow, p.Id))
            .ToList();
        // A new search (not "show more") counts towards Popular searches. Never fails the search.
        if (string.IsNullOrEmpty(r.PageToken))
        {
            try { await counter.RecordAsync(r.TextQuery!.Trim(), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Couldn't count the search for Popular searches"); }
        }
        return new GooglePlacesSearchDto(text, location, places, page.NextPageToken);
    }

    /// <summary>Explicit coordinates first, then the selected area, then the city centre, then a place found by name.</summary>
    private async Task<GooglePlacesLocationDto> ResolveAsync(GooglePlacesSearchQuery r, CancellationToken ct)
    {
        if (r is { Lat: { } lat, Lon: { } lon }) return new GooglePlacesLocationDto(lat, lon, "coordinates", null, null, null);

        if (r.AreaId is { } areaId)
        {
            var area = await uow.Repository<Area>().QueryNoTracking()
                .Where(a => a.Id == areaId && a.IsActive && a.City.IsActive)
                .Select(a => new { a.Name, a.Latitude, a.Longitude, CityName = a.City.Name, CitySlug = a.City.Slug, CityLat = a.City.Latitude, CityLng = a.City.Longitude })
                .FirstOrDefaultAsync(ct) ?? throw new BadRequestException("The selected area was not found.");
            if (area is { Latitude: { } alat, Longitude: { } alng })
                return new GooglePlacesLocationDto((double)alat, (double)alng, "area", area.Name, area.CityName, area.CitySlug);
            if (area is { CityLat: { } clat, CityLng: { } clng })
                return new GooglePlacesLocationDto((double)clat, (double)clng, "area", area.Name, area.CityName, area.CitySlug);
            throw new BadRequestException($"{area.Name} has no map location yet.");
        }

        if (string.IsNullOrWhiteSpace(r.City))
        {
            var found = await geocoder.GeocodeAsync(r.Place!, ct) ?? throw new BadRequestException($"We couldn’t find a place called “{r.Place!.Trim()}”.");
            return new GooglePlacesLocationDto(found.Latitude, found.Longitude, "place", null, found.Name, null);
        }

        var slug = r.City.Trim();
        var city = await uow.Repository<City>().QueryNoTracking()
            .Where(c => c.Slug == slug && c.IsActive)
            .Select(c => new { c.Name, c.Slug, c.Latitude, c.Longitude })
            .FirstOrDefaultAsync(ct) ?? throw new BadRequestException("The selected city was not found.");
        return city is { Latitude: { } cLat, Longitude: { } cLng }
            ? new GooglePlacesLocationDto((double)cLat, (double)cLng, "city", null, city.Name, city.Slug)
            : throw new BadRequestException($"{city.Name} has no map location yet.");
    }

    private static string MapsUrl(GooglePlace p) => !string.IsNullOrWhiteSpace(p.MapsUrl) ? p.MapsUrl
        : p is { Latitude: { } lat, Longitude: { } lon }
            ? string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps/search/?api=1&query={lat:0.######},{lon:0.######}")
            : $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString($"{p.Name} {p.FormattedAddress}")}";

    private static string DirectionsUrl(GooglePlace p) => p is { Latitude: { } lat, Longitude: { } lon }
        ? string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps/dir/?api=1&destination={lat:0.######},{lon:0.######}")
        : $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString($"{p.Name} {p.FormattedAddress}")}";
}

/// <param name="Name">Photo resource name: "places/{placeId}/photos/{photoRef}".</param>
public sealed record GooglePlacePhotoQuery(string? Name, int MaxWidth) : IRequest<string>;

public sealed class GooglePlacePhotoValidator : AbstractValidator<GooglePlacePhotoQuery>
{
    public GooglePlacePhotoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(1000)
            .Matches("^places/[A-Za-z0-9_-]+/photos/[A-Za-z0-9_-]+$").WithMessage("Invalid photo name.");
        RuleFor(x => x.MaxWidth).InclusiveBetween(1, 4800);
    }
}

public sealed class GooglePlacePhotoHandler(IGooglePlacesSearch google) : IRequestHandler<GooglePlacePhotoQuery, string>
{
    public Task<string> Handle(GooglePlacePhotoQuery r, CancellationToken ct) => google.GetPhotoUriAsync(r.Name!, r.MaxWidth, ct);
}

/// <summary>Contact details for a place found outside Google (e.g. an AI-recommended OpenStreetMap place).</summary>
/// <param name="Found">False when Google Maps has no place with that name there; the other fields are then null.</param>
/// <param name="Phone">National format, e.g. "098480 12345".</param>
/// <param name="MapsUrl">The matched place on Google Maps (Google requires its data to be attributed).</param>
/// <param name="OpenNow">Whether it is open right now, by Google's opening hours; null when unknown.</param>
/// <param name="PhotoUrl">Served through <c>GET /api/places/photo</c>; shown with <paramref name="PhotoCredit"/>, as Google requires.</param>
public sealed record PlaceContactDto(bool Found, string? Phone, string? InternationalPhone, string? Website, string? MapsUrl,
    decimal? Rating = null, int? RatingCount = null, bool? OpenNow = null, string? PhotoUrl = null, string? PhotoCredit = null, string? PhotoCreditUrl = null);

/// <param name="Name">The place's name as shown in the results.</param>
public sealed record PlaceContactQuery(string? Name, double? Lat, double? Lon) : IRequest<PlaceContactDto>;

public sealed class PlaceContactValidator : AbstractValidator<PlaceContactQuery>
{
    public PlaceContactValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("The place name is required.").MaximumLength(200);
        RuleFor(x => x.Lat).NotNull().WithMessage("Latitude is required.").InclusiveBetween(-90, 90);
        RuleFor(x => x.Lon).NotNull().WithMessage("Longitude is required.").InclusiveBetween(-180, 180);
    }
}

/// <summary>
/// Looks a place's phone number up on Google Maps when the visitor asks for it (OpenStreetMap, the AI tier's source, rarely has phone
/// numbers). Runs per click rather than for every result, since each lookup is a billed Google request; nothing is stored.
/// </summary>
public sealed class PlaceContactHandler(IGooglePlacesSearch google) : IRequestHandler<PlaceContactQuery, PlaceContactDto>
{
    public async Task<PlaceContactDto> Handle(PlaceContactQuery r, CancellationToken ct)
    {
        if (!google.IsEnabled) throw new ExternalServiceException(503, "Phone lookup is not available right now.");
        var c = await google.FindContactAsync(r.Name!.Trim(), r.Lat!.Value, r.Lon!.Value, ct);
        if (c is null) return new PlaceContactDto(false, null, null, null, null);
        var credit = c.Photo?.Attributions.FirstOrDefault();
        return new PlaceContactDto(true, c.Phone ?? c.InternationalPhone, c.InternationalPhone, c.Website, c.MapsUrl, c.Rating, c.RatingCount, c.OpenNow,
            c.Photo is null ? null : $"/api/places/photo?name={Uri.EscapeDataString(c.Photo.Name)}&maxWidth={PhotoWidthPx}",
            credit?.DisplayName, credit?.Uri);
    }

    private const int PhotoWidthPx = 320;
}
