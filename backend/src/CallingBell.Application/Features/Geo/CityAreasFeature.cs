using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Geo;

/// <param name="AltNames">Alternate and old spellings, for search.</param>
/// <param name="SubLocalities">Neighbourhoods inside this area, for search ("Hydernagar" finds Kukatpally).</param>
public sealed record CityAreaDto(Guid Id, string Name, string Slug, string Pincode, string? AreaType,
    IReadOnlyList<string> AltNames, IReadOnlyList<string> SubLocalities);

/// <param name="Discovering">True while the discovery agent is working on this city; poll until false.</param>
/// <param name="DiscoveredOn">When the agent last completed for this city (null = never).</param>
public sealed record CityAreasDto(string CitySlug, string CityName, string State, bool Discovering, DateTimeOffset? DiscoveredOn,
    int SubLocalityCount, IReadOnlyList<CityAreaDto> Areas);

public sealed record GetCityAreasQuery(string CitySlug) : IRequest<CityAreasDto>;

/// <summary>
/// Every area of a city (top-level areas with their alternate names and sub-localities). Queues the discovery agent when the city
/// has never been discovered or its data is older than <see cref="RefreshAfter"/>; the current list is returned immediately.
/// </summary>
public sealed class GetCityAreasHandler(IUnitOfWork uow, IAreaDiscoveryService discovery) : IRequestHandler<GetCityAreasQuery, CityAreasDto>
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

    public async Task<CityAreasDto> Handle(GetCityAreasQuery request, CancellationToken ct)
    {
        var city = await uow.Repository<City>().QueryNoTracking()
            .Where(c => c.IsActive && c.Slug == request.CitySlug)
            .Select(c => new { c.Id, c.Slug, c.Name, State = c.State.Name, c.AreasDiscoveredOn })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("City", request.CitySlug);

        if (city.AreasDiscoveredOn is null || city.AreasDiscoveredOn < DateTimeOffset.UtcNow - RefreshAfter) discovery.Request(city.Id);

        var areas = await uow.Repository<Area>().QueryNoTracking()
            .Where(a => a.CityId == city.Id && a.IsActive)
            .Select(a => new { a.Id, a.Name, a.Slug, a.Pincode, a.AreaType, a.AltNames, a.ParentAreaId })
            .ToListAsync(ct);
        var subsByParent = areas.Where(a => a.ParentAreaId != null).ToLookup(a => a.ParentAreaId!.Value, a => a.Name);
        var top = areas
            .Where(a => a.ParentAreaId == null)
            .OrderBy(a => a.Name)
            .Select(a => new CityAreaDto(a.Id, a.Name, a.Slug, a.Pincode, a.AreaType,
                (a.AltNames ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                subsByParent[a.Id].OrderBy(n => n).ToList()))
            .ToList();

        return new CityAreasDto(city.Slug, city.Name, city.State, discovery.IsRunning(city.Id), city.AreasDiscoveredOn,
            areas.Count(a => a.ParentAreaId != null), top);
    }
}
