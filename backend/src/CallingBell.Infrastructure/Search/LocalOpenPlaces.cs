using System.Globalization;
using System.Text;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Search;

/// <summary>
/// Overture places searched in the local copy (dbo.OpenPlaces, imported per country by tools/overture/import_places.py): no API key, no
/// quota and no network. A search reads only the 0.1 degree grid cells around the point (the table is clustered on Cell), then filters
/// by category or name and orders by distance. Each query has its own database context: a search runs several at once, alongside the
/// request's own queries.
/// </summary>
internal sealed class LocalOpenPlaces(IServiceScopeFactory scopes, IMemoryCache cache, IOptions<OpenPlacesOptions> options)
{
    private const string ImportsKey = "open-places|imports";
    private static readonly TimeSpan ImportsLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Whether an imported country covers the point (the imports are re-read every few minutes, so a new import is picked up).</summary>
    public async Task<bool> CoversAsync(double lat, double lng, CancellationToken ct)
    {
        var imports = await cache.GetOrCreateAsync(ImportsKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = ImportsLifetime;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.OpenPlacesImports.AsNoTracking()
                .Select(i => new { i.MinLatitude, i.MaxLatitude, i.MinLongitude, i.MaxLongitude }).ToListAsync(ct);
        });
        var (la, lo) = ((decimal)lat, (decimal)lng);
        return imports!.Any(i => la >= i.MinLatitude && la <= i.MaxLatitude && lo >= i.MinLongitude && lo <= i.MaxLongitude);
    }

    /// <inheritdoc cref="IOpenPlaces.SearchAsync"/>
    public async Task<IReadOnlyList<ExternalPlace>> SearchAsync(string? category, string? text, double lat, double lng, int radiusM, int limit,
        bool namesOnly, CancellationToken ct)
    {
        category = category?.Trim().ToLowerInvariant();
        text = category is null ? text?.Trim() : null;
        if (category is null && string.IsNullOrEmpty(text)) return [];
        limit = Math.Clamp(limit, 1, 50);

        // The cells around the point: one range of Cell values per row of cells (each row's cells are consecutive numbers).
        var dLat = radiusM / 111_320.0;
        var dLng = dLat / Math.Max(0.01, Math.Cos(lat * Math.PI / 180));
        var (minLat, maxLat, minLng, maxLng) = (lat - dLat, lat + dLat, lng - dLng, lng + dLng);
        var sql = new StringBuilder("SELECT TOP (@limit) * FROM dbo.OpenPlaces WHERE (");
        var first = OpenPlace.CellOf(minLat, minLng);
        var last = OpenPlace.CellOf(maxLat, maxLng);
        for (var row = first / 10000; row <= last / 10000; row++)
        {
            if (row != first / 10000) sql.Append(" OR ");
            // Integers computed here, not input: safe to put in the statement.
            sql.Append(CultureInfo.InvariantCulture, $"Cell BETWEEN {row * 10000 + first % 10000} AND {row * 10000 + last % 10000}");
        }
        sql.Append(") AND Latitude BETWEEN @minLat AND @maxLat AND Longitude BETWEEN @minLng AND @maxLng")
            .Append(" AND (Confidence IS NULL OR Confidence >= @minConfidence)");

        var parameters = new List<SqlParameter>
        {
            new("@limit", limit), new("@minLat", minLat), new("@maxLat", maxLat), new("@minLng", minLng), new("@maxLng", maxLng),
            new("@minConfidence", options.Value.MinConfidence), new("@lat", lat), new("@lng", lng),
            new("@lngScale", Math.Pow(Math.Cos(lat * Math.PI / 180), 2)),
        };
        if (category is not null)
        {
            sql.Append(" AND (Category = @category OR AltCategories LIKE @categoryLike)");
            parameters.Add(new("@category", category));
            parameters.Add(new("@categoryLike", $"%,{category},%"));
        }
        else
        {
            // Free text that is also a category ("electrician") matches it too, as the API does; names only when asked.
            sql.Append(namesOnly ? " AND Name LIKE @name ESCAPE '\\'" : " AND (Name LIKE @name ESCAPE '\\' OR Category = @textCategory)");
            parameters.Add(new("@name", $"%{Escape(text!)}%"));
            if (!namesOnly) parameters.Add(new("@textCategory", text!.ToLowerInvariant().Replace(' ', '_')));
        }
        sql.Append(" ORDER BY (Latitude - @lat) * (Latitude - @lat) + (Longitude - @lng) * (Longitude - @lng) * @lngScale");

        List<OpenPlace> rows;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            rows = await db.OpenPlaces.FromSqlRaw(sql.ToString(), parameters.ToArray<object>()).AsNoTracking().ToListAsync(ct);
        }
        return rows
            .Where(o => GeoMath.HaversineKm(lat, lng, (double)o.Latitude, (double)o.Longitude) * 1000 <= radiusM)
            .Select(o => new ExternalPlace("overture", o.PlaceId, o.Name, OverturePlaceText.Label(o.Category), o.Address, (double)o.Latitude,
                (double)o.Longitude, OverturePlaceText.Phone(o.Phone), o.Website, null, null, null, null, category is null ? "name" : "tag",
                CountryCode: o.CountryCode, FromDatabase: true))
            .ToList();
    }

    /// <summary>LIKE wildcards in the text are matched literally.</summary>
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}

/// <summary>
/// Open Places for the search: the local copy first where an imported country covers the point, then the Open Places API's places that
/// the copy doesn't have (when it has a key). Outside imported countries, the API alone.
/// </summary>
internal sealed class OpenPlacesRouter(LocalOpenPlaces local, OpenPlacesService api, IOptions<OpenPlacesOptions> options) : IOpenPlaces
{
    public bool IsEnabled => options.Value.Enabled;

    public async Task<IReadOnlyList<ExternalPlace>?> SearchAsync(string? category, string? text, double lat, double lng, int radiusM, int limit,
        CancellationToken ct, bool namesOnly = false)
    {
        if (!options.Value.Enabled) return null;
        var fromApi = api.IsEnabled ? api.SearchAsync(category, text, lat, lng, radiusM, limit, ct, namesOnly) : null;
        if (!await local.CoversAsync(lat, lng, ct)) return fromApi is null ? null : await fromApi;
        var stored = await local.SearchAsync(category, text, lat, lng, radiusM, limit, namesOnly, ct);
        if (fromApi is null || await fromApi is not { } live) return stored;
        var known = stored.Select(p => p.Id).ToHashSet();
        return [.. stored, .. live.Where(p => !known.Contains(p.Id))];
    }
}
