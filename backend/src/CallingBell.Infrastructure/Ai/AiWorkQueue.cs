using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

/// <summary>
/// Single queue for all AI work. One local model serves every feature, so jobs run one at a time in <see cref="AiWorker"/>;
/// AI never runs inside an HTTP request.
/// </summary>
internal sealed class AiWorkQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _jobs =
        Channel.CreateBounded<Func<CancellationToken, Task>>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite });

    internal ChannelReader<Func<CancellationToken, Task>> Reader => _jobs.Reader;

    /// <summary>False when the queue is full; the caller should then give up on this request.</summary>
    public bool TryEnqueue(Func<CancellationToken, Task> job) => _jobs.Writer.TryWrite(job);
}

internal sealed class AiWorker(AiWorkQueue queue, ILogger<AiWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Jobs record their own outcome; this only guards the worker loop.
                try { await job(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Unhandled error in an AI job");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown (including a failed start): nothing to report.
        }
    }
}

/// <summary>
/// Results of one AI feature, keyed by place: cached for <see cref="AiOptions.CacheHours"/>, tracked while pending, and suppressed for
/// <see cref="AiOptions.RetryAfterMinutes"/> after a failure so an unavailable model isn't hammered.
/// </summary>
internal sealed class AiResultCache<T>(IOptions<AiOptions> options) where T : class
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 5_000 });
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    public T? Get(string key) => _cache.TryGetValue(key, out T? value) ? value : null;

    public bool IsPending(string key) => _pending.ContainsKey(key);

    /// <summary>Marks <paramref name="key"/> pending; false when it is already cached, pending, or recently failed.</summary>
    public bool TryStart(string key) =>
        !_cache.TryGetValue(key, out _) && !_cache.TryGetValue(FailedKey(key), out _) && _pending.TryAdd(key, 0);

    public void Complete(string key, T? result)
    {
        var o = options.Value;
        if (result is not null)
            _cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(o.CacheHours) });
        else
            _cache.Set(FailedKey(key), true, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(o.RetryAfterMinutes) });
        _pending.TryRemove(key, out _);
    }

    /// <summary>Releases a pending key whose job could not be queued.</summary>
    public void Abandon(string key) => _pending.TryRemove(key, out _);

    private static string FailedKey(string key) => "failed:" + key;
}

internal static class AiJobs
{
    /// <summary>
    /// Queues <paramref name="run"/> for <paramref name="key"/> unless it is cached, pending or recently failed. The result (or a failure)
    /// is recorded in <paramref name="cache"/>; results with nothing usable count as failures.
    /// </summary>
    public static void Enqueue<T>(AiWorkQueue queue, AiResultCache<T> cache, ILogger logger, string key, string what,
        Func<CancellationToken, Task<T?>> run, Func<T, bool> usable) where T : class
    {
        if (!cache.TryStart(key)) return;
        var queued = queue.TryEnqueue(async ct =>
        {
            T? result = null;
            try
            {
                result = await run(ct);
                if (result is not null && !usable(result)) result = null;
                logger.LogInformation("AI {What}: {Outcome}", what, result is null ? "no usable answer" : "done");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AI {What} failed; database results are shown instead", what);
            }
            finally
            {
                cache.Complete(key, result);
            }
        });
        if (!queued) cache.Abandon(key);
    }
}
