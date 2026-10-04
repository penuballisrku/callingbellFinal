using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Geo;

/// <param name="CountryCode">The visitor's country (CDN header, then IP), or the one asked for.</param>
/// <param name="CountryName">Null until the country's cities have been imported once.</param>
/// <param name="CityCount">Cities listed for the country now.</param>
/// <param name="Importing">True while the city catalogue agent is importing the country's cities; poll, then reload the city list.</param>
/// <param name="ImportedOn">When the last import completed (null = never).</param>
public sealed record CountryCatalogDto(string CountryCode, string? CountryName, int StateCount, int CityCount, bool Importing,
    DateTimeOffset? ImportedOn, string? Note);

/// <param name="Country">Explicit ISO 3166-1 alpha-2 code; otherwise the visitor's country from <paramref name="CdnCountry"/> or <paramref name="ClientIp"/>.</param>
public sealed record GetCountryCatalogQuery(string? Country, string? CdnCountry, string? ClientIp) : IRequest<CountryCatalogDto>;

/// <summary>
/// The visitor's country and its city catalogue. Queues the city catalogue agent when the country's cities have never been imported, or
/// the import is older than <see cref="RefreshAfter"/>, so every city dropdown can list the whole country.
/// </summary>
public sealed class GetCountryCatalogHandler(IUnitOfWork uow, ISender sender, ICountryCatalogService catalogs)
    : IRequestHandler<GetCountryCatalogQuery, CountryCatalogDto>
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

    public async Task<CountryCatalogDto> Handle(GetCountryCatalogQuery r, CancellationToken ct)
    {
        var country = r.Country is { Length: 2 } c && c.All(char.IsAsciiLetter)
            ? c.ToUpperInvariant()
            : (await sender.Send(new GetVisitorCountryQuery(r.CdnCountry, r.ClientIp), ct)).CountryCode;

        var catalog = await uow.Repository<CountryCatalog>().QueryNoTracking().FirstOrDefaultAsync(x => x.CountryCode == country, ct);
        if (catalog?.ImportedOn is null || catalog.ImportedOn < DateTimeOffset.UtcNow - RefreshAfter) catalogs.Request(country);

        var cityCount = await uow.Repository<City>().QueryNoTracking().CountAsync(x => x.IsActive && x.State.CountryCode == country, ct);
        var stateCount = await uow.Repository<State>().QueryNoTracking().CountAsync(x => x.IsActive && x.CountryCode == country, ct);
        return new CountryCatalogDto(country, catalog?.CountryName, stateCount, cityCount, catalogs.IsRunning(country), catalog?.ImportedOn, catalog?.Note);
    }
}
