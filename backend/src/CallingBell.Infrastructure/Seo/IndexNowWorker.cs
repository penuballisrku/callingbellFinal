using System.Net.Http.Json;
using CallingBell.Application.Features.Seo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Seo;

/// <summary>
/// IndexNow: every few minutes, sends the public pages that changed (new or edited businesses, and the category and location pages they
/// appear on, as listed in the sitemaps) to the IndexNow endpoint, which shares them with Bing, Yandex, Seznam and Naver. Bing's index
/// also feeds ChatGPT search and Copilot, so changes reach AI answers sooner. The first run after a start sends the last day's changes
/// (a restart loses nothing important); off unless Seo:IndexNow is enabled with a key. Google doesn't use IndexNow; it reads the sitemaps.
/// </summary>
public sealed class IndexNowWorker(IServiceScopeFactory scopes, IHttpClientFactory http, IOptions<SeoOptions> options, ILogger<IndexNowWorker> logger)
    : BackgroundService
{
    public const string HttpClientName = "indexnow";
    /// <summary>The protocol's limit per request.</summary>
    private const int MaxUrlsPerRequest = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value.IndexNow;
        if (!o.IsValid)
        {
            if (o.Enabled) logger.LogWarning("IndexNow is enabled but Seo:IndexNow:Key is missing or invalid; not sending");
            return;
        }
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        // Let the site start (and the sitemap caches fill) first.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(o.IntervalMinutes, 5, 1440)));
        do
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                if (await SubmitAsync(since, stoppingToken)) since = started;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "IndexNow submission failed; retrying next round");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Sends the pages changed since <paramref name="since"/>. True when there was nothing to send or the endpoint accepted it.</summary>
    private async Task<bool> SubmitAsync(DateTimeOffset since, CancellationToken ct)
    {
        IReadOnlyList<string> urls;
        using (var scope = scopes.CreateScope())
            urls = await scope.ServiceProvider.GetRequiredService<SitemapService>().ChangedSinceAsync(since, ct);
        if (urls.Count == 0) return true;

        var o = options.Value;
        var site = new Uri(o.SiteUrl);
        var client = http.CreateClient(HttpClientName);
        foreach (var batch in urls.Chunk(MaxUrlsPerRequest))
        {
            using var response = await client.PostAsJsonAsync(o.IndexNow.Endpoint, new
            {
                host = site.Host,
                key = o.IndexNow.Key,
                keyLocation = new Uri(site, "/indexnow.txt").ToString(),
                urlList = batch,
            }, ct);
            // 200 OK and 202 Accepted are both success (202: key not yet verified, which happens on the first submission).
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("IndexNow rejected {Count} URL(s): {Status} {Reason}", batch.Length, (int)response.StatusCode, response.ReasonPhrase);
                return false;
            }
        }
        logger.LogInformation("IndexNow: sent {Count} changed page(s)", urls.Count);
        return true;
    }
}
