using CallingBell.Application.Features.Catalog;
using CallingBell.Application.Features.Home;
using MediatR;

namespace CallingBell.Api.Infrastructure;

/// <summary>Output-cache policy names.</summary>
public static class CachePolicies
{
    /// <summary>Public, non-personalised catalogue reads (home, categories, cities, lookups, banners, ...): 60 seconds, varied by query string.</summary>
    public const string PublicCatalog = "public-catalog";
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
