using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CallingBell.Application.Common;

/// <summary>An active city, as location lookups need it.</summary>
public sealed record CityRef(Guid Id, string Slug, string Name, string State, string CountryCode, double? Latitude, double? Longitude,
    string? Source, string? AltNames, int SortOrder);

/// <summary>An active sub-category with its category, as search needs it.</summary>
public sealed record SubCategoryRef(Guid Id, string Slug, string Name, string CategorySlug, string CategoryName, string? OsmTags);

/// <summary>
/// Reference data that nearly every request reads but that changes rarely (thousands of cities once a country is imported, the
/// category tree), kept in memory for <see cref="Lifetime"/> instead of being re-read from SQL Server per request.
/// Writers call <see cref="Invalidate"/> (city import, admin edits) so changes show immediately.
/// </summary>
public sealed class ReferenceDataCache(IUnitOfWork uow, IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string CitiesKey = "ref|cities";
    private const string SubCategoriesKey = "ref|subcategories";
    /// <summary>Prefix for other cached views of the same data (e.g. city lists per country), removed together.</summary>
    public const string DerivedPrefix = "ref|derived|";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> DerivedKeys = [];

    public Task<IReadOnlyList<CityRef>> ActiveCitiesAsync(CancellationToken ct) => GetAsync(CitiesKey, () =>
        uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CityRef(c.Id, c.Slug, c.Name, c.State.Name, c.State.CountryCode, (double?)c.Latitude, (double?)c.Longitude,
                c.Source, c.AltNames, c.SortOrder))
            .ToListAsync(ct), ct);

    public Task<IReadOnlyList<SubCategoryRef>> ActiveSubCategoriesAsync(CancellationToken ct) => GetAsync(SubCategoriesKey, () =>
        uow.Repository<SubCategory>().QueryNoTracking().Where(s => s.IsActive && s.Category.IsActive)
            .OrderBy(s => s.Category.SortOrder).ThenBy(s => s.SortOrder)
            .Select(s => new SubCategoryRef(s.Id, s.Slug, s.Name, s.Category.Slug, s.Category.Name, s.OsmTags))
            .ToListAsync(ct), ct);

    /// <summary>Caches any other view of the reference data under <paramref name="key"/>; it is dropped by <see cref="Invalidate"/> too.</summary>
    public async Task<T> GetDerivedAsync<T>(string key, Func<Task<T>> load, CancellationToken ct) where T : class
    {
        var full = DerivedPrefix + key;
        lock (DerivedKeys) DerivedKeys.Add(full);
        if (cache.TryGetValue(full, out T? hit) && hit is not null) return hit;
        await Gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(full, out hit) && hit is not null) return hit;
            var value = await load();
            cache.Set(full, value, Lifetime);
            return value;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Drops every cached list, so the next request reads the current data.</summary>
    public static void Invalidate(IMemoryCache cache)
    {
        cache.Remove(CitiesKey);
        cache.Remove(SubCategoriesKey);
        lock (DerivedKeys)
        {
            foreach (var key in DerivedKeys) cache.Remove(key);
            DerivedKeys.Clear();
        }
    }

    /// <summary>One load at a time per process: concurrent first requests wait for it instead of all querying SQL Server.</summary>
    private async Task<IReadOnlyList<T>> GetAsync<T>(string key, Func<Task<List<T>>> load, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out IReadOnlyList<T>? hit) && hit is not null) return hit;
        await Gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out hit) && hit is not null) return hit;
            IReadOnlyList<T> value = await load();
            cache.Set(key, value, Lifetime);
            return value;
        }
        finally { Gate.Release(); }
    }
}
