using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CallingBell.Application.Features.Seo;

/// <summary>
/// Cache for SEO output (page metadata, landing pages, sitemaps). Every entry is dropped at once by <see cref="Reset"/>, which runs after
/// any command that changes public data (see PublicCacheInvalidationBehaviour) - a renamed business, a moved address or a new review is
/// what crawlers see next. Entries also expire after their lifetime, which bounds staleness from changes made outside the API.
/// </summary>
public sealed class SeoCache(IMemoryCache cache)
{
    private CancellationTokenSource _generation = new();

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan lifetime, Func<Task<T>> load)
    {
        var full = "seo|" + key;
        if (cache.TryGetValue(full, out T? hit) && hit is not null) return hit;
        var token = _generation.Token;
        var value = await load();
        if (value is not null && !token.IsCancellationRequested)
            cache.Set(full, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }.AddExpirationToken(new CancellationChangeToken(token)));
        return value;
    }

    /// <summary>Drops every SEO entry.</summary>
    public void Reset()
    {
        var old = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        old.Cancel();
    }
}
