using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Geo;

/// <summary>An address the visitor can pick, with the CallInBell city and area it falls in (when listed).</summary>
/// <param name="AddressLine">The street-level part ("12, Road No. 5"); null when the suggestion is a whole locality.</param>
/// <param name="Locality">The suburb or neighbourhood Geoapify names, shown when no listed area matched.</param>
public sealed record AddressSuggestionDto(string Formatted, string? AddressLine, string? Postcode, double Latitude, double Longitude,
    string? CitySlug, string? CityName, string? AreaSlug, string? AreaName, string? Locality, string? District, string? State);

/// <param name="Enabled">False when Geoapify isn't configured (or is out of credits for today): the form works without suggestions.</param>
public sealed record AddressSuggestionsDto(bool Enabled, IReadOnlyList<AddressSuggestionDto> Items);

/// <param name="Text">What was typed; empty only asks whether suggestions are available.</param>
/// <param name="Country">ISO 3166-1 alpha-2; the default country when missing.</param>
/// <param name="Lat">Where to look first (e.g. the chosen city), with <paramref name="Lng"/>.</param>
public sealed record GetAddressSuggestionsQuery(string? Text, string? Country, double? Lat, double? Lng) : IRequest<AddressSuggestionsDto>;

/// <summary>
/// Address autocomplete (Geoapify) for the business sign-up form. Each suggestion is matched to the listed city and area it lies in
/// (<see cref="ReverseGeocoder"/>, from our own records), so picking one fills the city, area, PIN code and map position together.
/// </summary>
public sealed class GetAddressSuggestionsHandler(IGeoapify geoapify, IGeoLocationService geo, ReverseGeocoder reverse)
    : IRequestHandler<GetAddressSuggestionsQuery, AddressSuggestionsDto>
{
    /// <summary>A suggestion farther than this from every listed city centre gets no city.</summary>
    private const double CityRadiusKm = 30;

    public async Task<AddressSuggestionsDto> Handle(GetAddressSuggestionsQuery r, CancellationToken ct)
    {
        if (!geoapify.IsEnabled) return new(false, []);
        var text = (r.Text ?? "").Trim();
        if (text.Length < 3) return new(true, []);
        if (text.Length > 200) text = text[..200];
        var country = r.Country is { Length: 2 } c2 && c2.All(char.IsAsciiLetter) ? c2.ToUpperInvariant() : geo.DefaultCountryCode;
        var lat = r.Lat is { } la && Math.Abs(la) <= 90 ? la : (double?)null;
        var lng = r.Lng is { } lo && Math.Abs(lo) <= 180 ? lo : (double?)null;

        var found = await geoapify.AutocompleteAsync(text, country, lat, lng, ct);
        if (found is null) return new(true, []);
        var items = new List<AddressSuggestionDto>();
        foreach (var s in found)
        {
            var place = await reverse.ReverseAsync(s.Latitude, s.Longitude, (s.CountryCode ?? country).ToUpperInvariant(), CityRadiusKm, ct);
            // The first line is the street address unless the suggestion is a whole locality, city or district.
            var line = s.AddressLine1 is { } l1 && !new[] { s.Suburb, s.City, s.District, s.State }.Any(n => string.Equals(n, l1, StringComparison.OrdinalIgnoreCase))
                ? l1 : null;
            var pin = s.Postcode is { } pc && pc.Count(char.IsDigit) == 6 ? new string(pc.Where(char.IsDigit).ToArray()) : s.Postcode;
            items.Add(new AddressSuggestionDto(s.Formatted, line, pin, s.Latitude, s.Longitude, place?.City.Slug, place?.City.Name,
                place?.Area?.Slug, place?.Area?.Name, s.Suburb, s.District, s.State));
        }
        return new(true, items);
    }
}

/// <summary>The map image of a listed business's location (Geoapify), or null when it has no position or maps are off.</summary>
public sealed record GetBusinessMapQuery(string Slug) : IRequest<StaticMapImage?>;

/// <summary>
/// Only listed businesses' own positions are drawn, so the endpoint can't be used to spend the map credits on arbitrary places; each
/// image is cached by the Geoapify client.
/// </summary>
public sealed class GetBusinessMapHandler(IUnitOfWork uow, IGeoapify geoapify) : IRequestHandler<GetBusinessMapQuery, StaticMapImage?>
{
    public const int Width = 600, Height = 300, Zoom = 15;

    public async Task<StaticMapImage?> Handle(GetBusinessMapQuery r, CancellationToken ct)
    {
        if (!geoapify.IsEnabled) return null;
        var point = await uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => b.Slug == r.Slug && b.Latitude != null && b.Longitude != null)
            .Select(b => new { Lat = (double)b.Latitude!, Lng = (double)b.Longitude! })
            .FirstOrDefaultAsync(ct);
        return point is null ? null : await geoapify.StaticMapAsync(point.Lat, point.Lng, Zoom, Width, Height, ct);
    }
}
