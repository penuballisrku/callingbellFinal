using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

/// <summary>
/// <see cref="Interactive"/>: someone is waiting on the page for this (search intent, place picks, an answer); runs before any
/// <see cref="Background"/> work (recommendations, review summaries) and is skipped once nobody is asking for it any more.
/// </summary>
internal enum AiPriority { Background, Interactive }

/// <summary>
/// Queue for all AI work. One local model serves every feature, so jobs run one at a time in <see cref="AiWorker"/>, interactive jobs
/// first; AI never runs inside an HTTP request.
/// </summary>
internal sealed class AiWorkQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _interactive =
        Channel.CreateBounded<Func<CancellationToken, Task>>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Channel<Func<CancellationToken, Task>> _background =
        Channel.CreateBounded<Func<CancellationToken, Task>>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>False when the queue is full; the caller should then give up on this request.</summary>
    public bool TryEnqueue(Func<CancellationToken, Task> job, AiPriority priority = AiPriority.Background) =>
        (priority == AiPriority.Interactive ? _interactive : _background).Writer.TryWrite(job);

    /// <summary>The next job, interactive ones first; waits when there is none.</summary>
    internal async Task<Func<CancellationToken, Task>> NextAsync(CancellationToken ct)
    {
        while (true)
        {
            if (_interactive.Reader.TryRead(out var job) || _background.Reader.TryRead(out job)) return job;
            await Task.WhenAny(_interactive.Reader.WaitToReadAsync(ct).AsTask(), _background.Reader.WaitToReadAsync(ct).AsTask());
            ct.ThrowIfCancellationRequested();
        }
    }
}

internal sealed class AiWorker(AiWorkQueue queue, ILogger<AiWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var job = await queue.NextAsync(stoppingToken);
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
    /// <summary>Pending keys and when a page last asked for each (pages keep asking while they wait).</summary>
    private readonly ConcurrentDictionary<string, long> _pending = new();

    public T? Get(string key)
    {
        if (_cache.TryGetValue(key, out T? value)) return value;
        Touch(key);
        return null;
    }

    public bool IsPending(string key)
    {
        Touch(key);
        return _pending.ContainsKey(key);
    }

    /// <summary>Marks <paramref name="key"/> pending; false when it is already cached, pending, or recently failed.</summary>
    public bool TryStart(string key) =>
        !_cache.TryGetValue(key, out _) && !_cache.TryGetValue(FailedKey(key), out _) && _pending.TryAdd(key, DateTime.UtcNow.Ticks);

    /// <summary>True when nobody has asked for <paramref name="key"/> for <paramref name="after"/> (the visitor left or searched again).</summary>
    public bool IsAbandoned(string key, TimeSpan after) =>
        _pending.TryGetValue(key, out var ticks) && DateTime.UtcNow.Ticks - ticks > after.Ticks;

    private void Touch(string key)
    {
        if (_pending.TryGetValue(key, out var old)) _pending.TryUpdate(key, DateTime.UtcNow.Ticks, old);
    }

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
    /// <param name="priority">Interactive jobs run first, and are skipped when no page has asked for them for <see cref="AbandonedAfter"/>.</param>
    public static void Enqueue<T>(AiWorkQueue queue, AiResultCache<T> cache, ILogger logger, string key, string what,
        Func<CancellationToken, Task<T?>> run, Func<T, bool> usable, AiPriority priority = AiPriority.Background) where T : class
    {
        if (!cache.TryStart(key)) return;
        var queued = queue.TryEnqueue(async ct =>
        {
            // Nobody is waiting any more (they left the page or searched for something else): skip it, so the model serves the
            // current visitors. Not recorded as a failure, so asking again later queues it afresh.
            if (priority == AiPriority.Interactive && cache.IsAbandoned(key, AbandonedAfter))
            {
                logger.LogDebug("AI {What} skipped: no longer requested", what);
                cache.Abandon(key);
                return;
            }
            T? result = null;
            try
            {
                result = await run(ct);
                if (result is not null && !usable(result)) result = null;
                logger.LogInformation("AI {What}: {Outcome}", what, result is null ? "no usable answer" : "done");
            }
            catch (AiUnavailableException ex)
            {
                // The client logs the outage once; no stack trace per place.
                logger.LogDebug("AI {What} skipped: {Reason}", what, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AI {What} failed; database results are shown instead", what);
            }
            finally
            {
                cache.Complete(key, result);
            }
        }, priority);
        if (!queued) cache.Abandon(key);
    }

    /// <summary>Pages poll every 5-8 seconds while they wait, so a minute without a request means nobody is waiting.</summary>
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromSeconds(60);
}
