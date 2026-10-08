using CallingBell.Application.Common;
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
/// <param name="StateSlug">The city's state / province / region, so a picker given only the city can select its state.</param>
public sealed record CityAreasDto(string CitySlug, string CityName, string State, bool Discovering, DateTimeOffset? DiscoveredOn,
    int SubLocalityCount, IReadOnlyList<CityAreaDto> Areas, string? StateSlug = null);

public sealed record GetCityAreasQuery(string CitySlug) : IRequest<CityAreasDto>;

/// <summary>
/// Every area of a city (top-level areas with their alternate names and sub-localities). Queues the discovery agent when the city
/// has never been discovered or its data is older than <see cref="RefreshAfter"/>; the current list is returned immediately.
/// </summary>
public sealed class GetCityAreasHandler(IUnitOfWork uow, IAreaDiscoveryService discovery, ReferenceDataCache reference)
    : IRequestHandler<GetCityAreasQuery, CityAreasDto>
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);
    /// <summary>A run that found no areas (sources busy, or an earlier rule rejected them all) is retried sooner.</summary>
    public static readonly TimeSpan RetryEmptyAfter = TimeSpan.FromHours(6);

    public async Task<CityAreasDto> Handle(GetCityAreasQuery request, CancellationToken ct)
    {
        // Kept in memory per city (one indexed query on Areas by CityId) and dropped whenever the area-discovery agent, the city import
        // or an admin changes cities or areas, so newly found areas show on the next request.
        var list = await reference.GetDerivedAsync($"city-areas|{request.CitySlug}", () => LoadAsync(request.CitySlug, ct), ct);

        var age = DateTimeOffset.UtcNow - list.DiscoveredOn;
        if (age is null || age > RefreshAfter || (list.Areas.Count == 0 && age > RetryEmptyAfter)) discovery.Request(list.CityId);

        return new CityAreasDto(list.CitySlug, list.CityName, list.State, discovery.IsRunning(list.CityId), list.DiscoveredOn,
            list.SubLocalityCount, list.Areas, list.StateSlug);
    }

    private sealed record CachedAreas(Guid CityId, string CitySlug, string CityName, string State, string StateSlug, DateTimeOffset? DiscoveredOn,
        int SubLocalityCount, IReadOnlyList<CityAreaDto> Areas);

    private async Task<CachedAreas> LoadAsync(string slug, CancellationToken ct)
    {
        var city = await uow.Repository<City>().QueryNoTracking()
            .Where(c => c.IsActive && c.Slug == slug)
            .Select(c => new { c.Id, c.Slug, c.Name, State = c.State.Name, StateSlug = c.State.Slug, c.AreasDiscoveredOn })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("City", slug);

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
        return new CachedAreas(city.Id, city.Slug, city.Name, city.State, city.StateSlug, city.AreasDiscoveredOn,
            areas.Count(a => a.ParentAreaId != null), top);
    }
}
