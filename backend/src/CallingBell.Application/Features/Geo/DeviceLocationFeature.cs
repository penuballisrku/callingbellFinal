using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Search;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Geo;

/// <summary>
/// Where the visitor's device is, as listed places: state / province / region, city and area (with its PIN code), so the location
/// picker can select them. <paramref name="MatchedBy"/> is "area" (within an area's radius) or "city" (the nearest city centre).
/// </summary>
public sealed record DeviceLocationDto(string CountryCode, string? StateSlug, string? StateName, string CitySlug, string CityName,
    Guid? AreaId, string? AreaSlug, string? AreaName, string? Locality, string? Postcode, string MatchedBy, double DistanceKm);

/// <param name="Latitude">From the browser's Geolocation API, shared with the visitor's permission.</param>
/// <param name="Country">The visitor's country (ISO code); defaults to the IP's.</param>
public sealed record LocateDeviceQuery(double Latitude, double Longitude, string? Country, string? ClientIp, string? CdnCountry, double CityRadiusKm)
    : IRequest<DeviceLocationDto?>;

public sealed class LocateDeviceValidator : AbstractValidator<LocateDeviceQuery>
{
    public LocateDeviceValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Country).Matches("^[A-Za-z]{2}$").When(x => !string.IsNullOrEmpty(x.Country)).WithMessage("country must be a 2-letter ISO code.");
    }
}

/// <summary>
/// Device coordinates to the listed area they fall in (else the nearest listed city within range), using the same in-memory spatial
/// index as the IP lookup (<see cref="ReverseGeocoder"/>): no outside geocoding service, no cost, and the coordinates are neither stored
/// nor logged. Null when the device isn't near any listed city; the visitor keeps their current location.
/// </summary>
public sealed class LocateDeviceHandler(ReverseGeocoder geocoder, IUnitOfWork uow, ISender sender) : IRequestHandler<LocateDeviceQuery, DeviceLocationDto?>
{
    /// <summary>Device positions are precise (unlike an IP's), so an area a few kilometres away still counts as where the visitor is.</summary>
    private const double AreaKm = 5;

    public async Task<DeviceLocationDto?> Handle(LocateDeviceQuery r, CancellationToken ct)
    {
        var country = SuggestionText.CountryCode(r.Country)
            ?? (await sender.Send(new GetCountryCatalogQuery(null, r.CdnCountry, r.ClientIp), ct)).CountryCode;
        if (country is null) return null;
        var found = await geocoder.ReverseAsync(r.Latitude, r.Longitude, country, r.CityRadiusKm, ct, AreaKm);
        if (found is null) return null;
        var postcode = found.Area is { } area
            ? await uow.Repository<Area>().QueryNoTracking().Where(a => a.Id == area.Id).Select(a => a.Pincode).FirstOrDefaultAsync(ct)
            : null;
        return new DeviceLocationDto(found.CountryCode, found.Region?.Slug, found.Region?.Name, found.City.Slug, found.City.Name,
            found.Area?.Id, found.Area?.Slug, found.Area?.Name, found.Locality, string.IsNullOrWhiteSpace(postcode) ? null : postcode,
            found.MatchedBy, Math.Round(found.Area is not null ? found.AreaKm ?? found.CityKm : found.CityKm, 1));
    }
}
