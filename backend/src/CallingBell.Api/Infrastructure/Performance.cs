using CallingBell.Application.Features.Catalog;
using CallingBell.Application.Features.Home;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;

namespace CallingBell.Api.Infrastructure;

/// <summary>
/// Output-cache policy names. Both cache anonymous requests only (the default policy skips requests with an Authorization header, so
/// signed-in visitors always get their own favourites and so on), and both are cleared at once when data changes (<see cref="OutputCachePublicCache"/>).
/// </summary>
public static class CachePolicies
{
    /// <summary>Catalogue reads that rarely change (home, categories, cities, lookups, banners, page content, ...): 10 minutes, varied by query string.</summary>
    public const string PublicCatalog = "public-catalog";
    /// <summary>Business listings, business pages and their reviews: 60 seconds, so live availability and new reviews show quickly.</summary>
    public const string PublicListings = "public-listings";
    /// <summary>Tag on every public cached response, evicted after edits.</summary>
    public const string PublicTag = "public";
}

/// <summary>Clears the public output cache (by tag) when the Application layer reports that public data changed.</summary>
public sealed class OutputCachePublicCache(IOutputCacheStore store, ILogger<OutputCachePublicCache> logger) : CallingBell.Application.Common.Interfaces.IPublicCache
{
    public async Task InvalidateAsync(CancellationToken ct)
    {
        try { await store.EvictByTagAsync(CachePolicies.PublicTag, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not clear the public output cache"); }
    }
}

/// <summary>
/// Runs the main read queries once in the background after start-up, so .NET and EF Core compile them before the first visitor
/// arrives (otherwise the first home page load after a restart takes noticeably longer).
/// </summary>
public sealed class StartupWarmup(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<StartupWarmup> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            try
            {
                using var scope = scopes.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await sender.Send(new GetHomeQuery(null), stoppingToken);
                await sender.Send(new GetCategoriesQuery(), stoppingToken);
                await sender.Send(new GetCitiesQuery(), stoppingToken);
                await sender.Send(new GetLookupsQuery(), stoppingToken);
                // Location and search reference data most requests read (kept in memory afterwards).
                var reference = scope.ServiceProvider.GetRequiredService<CallingBell.Application.Common.ReferenceDataCache>();
                await reference.ActiveCitiesAsync(stoppingToken);
                await reference.ActiveSubCategoriesAsync(stoppingToken);
                logger.LogInformation("Warm-up finished in {Ms} ms", (int)(DateTime.UtcNow - started).TotalMilliseconds);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Warm-up failed; the first requests will just be slower");
            }
        }, stoppingToken));
        return Task.CompletedTask;
    }
}
