using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

public sealed class EmbeddingOptions
{
    public const string Section = "Ai:Embeddings";
    /// <summary>Meaning-based matching of searches to categories. Needs <see cref="AiOptions.Enabled"/> as well.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>An Ollama embedding model, e.g. qwen3-embedding:0.6b (best for this) or nomic-embed-text (smaller, less accurate).</summary>
    public string Model { get; set; } = "qwen3-embedding:0.6b";
    /// <summary>Task description prepended to searches (instruction-aware models such as Qwen3-Embedding); empty for other models.</summary>
    public string QueryInstruction { get; set; } =
        "Given a customer request on a local services app in India, find the type of local business or professional that can help";
    /// <summary>
    /// Confidence is how far the best sub-category stands out from all the others, in standard deviations (z-score): unrelated text
    /// ("hello", "asdf") scores evenly across the catalogue, while a real request peaks. Calibrated on qwen3-embedding:0.6b.
    /// At or above this the match is applied directly.
    /// </summary>
    public double ConfidentZ { get; set; } = 3.2;
    /// <summary>Between this and <see cref="ConfidentZ"/> the top matches are only candidates (offered, or checked by the chat model).</summary>
    public double PlausibleZ { get; set; } = 2.6;
    /// <summary>
    /// CPU threads for embeddings. With the chat model's <see cref="AiOptions.Threads"/>, keep the total at or below the core count so
    /// searches stay fast while chat jobs run (4 cores: 2 + 2). Keep it fixed: Ollama reloads the model when it changes.
    /// </summary>
    public int Threads { get; set; } = 2;
    /// <summary>
    /// Context window per request (Ollama num_ctx), in tokens. Queries and catalogue entries are short; a small window keeps the loaded
    /// model small (embedding models default to long windows). Keep it fixed: Ollama reloads the model when it changes. 0 = model default.
    /// </summary>
    public int ContextLength { get; set; } = 1024;
    /// <summary>How long Ollama keeps the (small) embedding model loaded, so searches don't wait for it to load again.</summary>
    public string KeepAlive { get; set; } = "24h";
    /// <summary>A search waits at most this long for its vector, so a slow model never holds up a page.</summary>
    public int QueryTimeoutMs { get; set; } = 4000;
    /// <summary>How often the index is checked against the catalogue; only new or changed texts are embedded again.</summary>
    public int RefreshMinutes { get; set; } = 30;
    /// <summary>Vectors are kept here (relative to the API folder) so a restart doesn't embed the catalogue again.</summary>
    public string CacheFile { get; set; } = "App_Data/semantic-index.json";
}

/// <summary>
/// The catalogue as vectors: every active sub-category is described by its name and category, its description, and the services
/// businesses list under it. A search is embedded once (and cached) and scored against each sub-category's closest document.
/// </summary>
internal sealed class SemanticCatalog(
    IHttpClientFactory httpFactory, IOptions<AiOptions> ai, IOptions<EmbeddingOptions> options, ILogger<SemanticCatalog> logger) : ISemanticCatalog
{
    private readonly MemoryCache _queries = new(new MemoryCacheOptions { SizeLimit = 5_000 });
    private CatalogIndex? _index;
    private long _unavailableUntilTicks;

    /// <param name="Keywords">Words distinctive to one sub-category's name ("ac" for AC Repair), used to break near-ties.</param>
    private sealed record CatalogIndex(string[] Slugs, float[][] Vectors, string Model, IReadOnlyDictionary<string, string[]> Keywords);

    public bool IsEnabled => ai.Value.Enabled && options.Value.Enabled;

    public bool IsReady => IsEnabled && _index is not null;

    public async Task<SemanticResult?> MatchAsync(string text, int top, CancellationToken ct, TimeSpan? timeout = null)
    {
        var index = _index;
        var query = Normalize(text);
        if (!IsEnabled || index is null || query.Length < 3) return null;

        var vector = await QueryVectorAsync(query, timeout ?? TimeSpan.FromMilliseconds(options.Value.QueryTimeoutMs), ct);
        if (vector is null) return null;

        // Each sub-category scores by its closest document.
        var best = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < index.Vectors.Length; i++)
        {
            var score = Dot(vector, index.Vectors[i]);
            if (!best.TryGetValue(index.Slugs[i], out var current) || score > current) best[index.Slugs[i]] = score;
        }
        if (best.Count < 2) return null;

        // A word distinctive to one service's name settles near-ties: "ac not cooling" is AC Repair, not Refrigerator Repair.
        var words = query.Split(' ').Select(Stem).ToHashSet();
        foreach (var (slug, keywords) in index.Keywords)
            if (best.ContainsKey(slug) && keywords.Any(words.Contains)) best[slug] += KeywordBoost;

        var mean = best.Values.Average();
        var sd = Math.Sqrt(best.Values.Sum(v => (v - mean) * (v - mean)) / best.Count);
        var ranked = best.OrderByDescending(b => b.Value).Take(top).Select(b => new SemanticMatch(b.Key, Math.Round(b.Value, 3))).ToList();
        var z = sd == 0 ? 0 : (ranked[0].Score - mean) / sd;
        var o = options.Value;
        return new SemanticResult(ranked, z >= o.ConfidentZ, z >= o.PlausibleZ, Math.Round(z, 2));
    }

    private async Task<float[]?> QueryVectorAsync(string query, TimeSpan timeout, CancellationToken ct)
    {
        if (_queries.TryGetValue(query, out float[]? cached)) return cached;
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _unavailableUntilTicks)) return null;

        // One embedding per text at a time, shared by concurrent searches. It isn't tied to the request: when a busy model makes a
        // search give up, the vector still arrives and is cached, so asking again moments later is instant.
        var pending = _inFlight.GetOrAdd(query, q => Task.Run(() => EmbedQueryAsync(q)));
        try
        {
            return await pending.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            logger.LogDebug("Semantic search: no vector for \"{Query}\" within {Timeout} ms (model busy); searching without it", query, timeout.TotalMilliseconds);
            return null;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<float[]?>> _inFlight = new();

    private async Task<float[]?> EmbedQueryAsync(string query)
    {
        var o = options.Value;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // a stalled model gives up eventually
            var input = string.IsNullOrWhiteSpace(o.QueryInstruction) ? query : $"Instruct: {o.QueryInstruction}\nQuery: {query}";
            var vector = (await EmbedAsync([input], cts.Token))[0];
            _queries.Set(query, vector, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(12) });
            return vector;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // Ollama not running or the model not installed (not merely busy): skip semantic matching for a minute rather than
            // having every search try again.
            if (Interlocked.Exchange(ref _unavailableUntilTicks, DateTime.UtcNow.AddMinutes(1).Ticks) < DateTime.UtcNow.Ticks)
                logger.LogWarning("Semantic search unavailable: {Reason}. Searches work without it; retrying in a minute.", ex.Message);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove(query, out _);
        }
    }

    /// <summary>Embeds texts with the configured model; vectors are normalised so a dot product is the cosine similarity.</summary>
    internal async Task<float[][]> EmbedAsync(IReadOnlyList<string> input, CancellationToken ct)
    {
        var o = ai.Value;
        var body = new
        {
            model = options.Value.Model, input, keep_alive = options.Value.KeepAlive,
            options = EmbedOptions(options.Value),
        };
        using var response = await httpFactory.CreateClient(OllamaChatClient.HttpClientName)
            .PostAsJsonAsync($"{o.BaseUrl.TrimEnd('/')}/api/embed", body, OllamaChatClient.Json, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Ollama embedding model '{options.Value.Model}' is not installed. Run `ollama pull {options.Value.Model}`");
        response.EnsureSuccessStatusCode();
        var reply = await response.Content.ReadFromJsonAsync<EmbedResponse>(OllamaChatClient.Json, ct);
        if (reply?.Embeddings is not { } vectors || vectors.Length != input.Count) throw new InvalidOperationException("Ollama returned no embeddings");
        return vectors.Select(Normalised).ToArray();
    }

    internal static Dictionary<string, object> EmbedOptions(EmbeddingOptions o)
    {
        var options = new Dictionary<string, object>();
        if (o.Threads > 0) options["num_thread"] = o.Threads;
        if (o.ContextLength > 0) options["num_ctx"] = o.ContextLength;
        return options;
    }

    internal void Publish(string[] slugs, float[][] vectors, IReadOnlyDictionary<string, string[]> keywords) =>
        _index = new CatalogIndex(slugs, vectors, options.Value.Model, keywords);

    /// <summary>Enough to settle a near-tie (similarities of close candidates differ by a few hundredths), not to override meaning.</summary>
    private const double KeywordBoost = 0.03;

    /// <summary>Simple plurals: "plumbers" and "plumber" compare equal.</summary>
    internal static string Stem(string w) =>
        w.Length > 4 && w.EndsWith("ies") ? w[..^3] + "y" : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w;

    internal static string Normalize(string text) => string.Join(' ', text.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static float[] Normalised(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += x * x;
        var n = (float)Math.Sqrt(sum);
        return n == 0 ? v : v.Select(x => x / n).ToArray();
    }

    private static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (var i = 0; i < a.Length && i < b.Length; i++) s += a[i] * b[i];
        return s;
    }

    private sealed record EmbedResponse(float[][]? Embeddings);
}

/// <summary>
/// Builds the semantic index in the background: at start-up (from the vectors saved on disk, so only new or changed catalogue texts are
/// embedded), then every <see cref="EmbeddingOptions.RefreshMinutes"/>. Searches work normally until the first index is ready.
/// </summary>
internal sealed class SemanticCatalogWorker(
    SemanticCatalog catalog, IServiceScopeFactory scopes, IHostEnvironment env, IOptions<EmbeddingOptions> options,
    ILogger<SemanticCatalogWorker> logger) : BackgroundService
{
    private const int MaxServicesPerSubCategory = 12;
    private const int BatchSize = 32;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!catalog.IsEnabled) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); // let start-up finish first
            while (!stoppingToken.IsCancellationRequested)
            {
                var ok = await BuildAsync(stoppingToken);
                // Retry soon after a failure (e.g. Ollama not started yet); otherwise refresh on schedule.
                await Task.Delay(ok ? TimeSpan.FromMinutes(Math.Max(1, options.Value.RefreshMinutes)) : TimeSpan.FromMinutes(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task<bool> BuildAsync(CancellationToken ct)
    {
        var o = options.Value;
        try
        {
            var (documents, keywords) = await LoadDocumentsAsync(ct);
            var cachePath = Path.Combine(env.ContentRootPath, o.CacheFile);
            var cache = await ReadCacheAsync(cachePath, o.Model, ct);
            var missing = documents.Select(d => d.Text).Distinct().Where(t => !cache.ContainsKey(Key(t))).ToList();

            if (missing.Count > 0)
            {
                var started = DateTime.UtcNow;
                logger.LogInformation("Semantic search: embedding {Count} catalogue texts with {Model}…", missing.Count, o.Model);
                for (var i = 0; i < missing.Count; i += BatchSize)
                {
                    var batch = missing.Skip(i).Take(BatchSize).ToList();
                    using var batchTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    batchTimeout.CancelAfter(TimeSpan.FromMinutes(5)); // a stalled model must not stop refreshes for good
                    var vectors = await catalog.EmbedAsync(batch, batchTimeout.Token);
                    for (var k = 0; k < batch.Count; k++) cache[Key(batch[k])] = vectors[k];
                }
                await WriteCacheAsync(cachePath, o.Model, documents.Select(d => Key(d.Text)).ToHashSet(), cache, ct);
                logger.LogInformation("Semantic search: embedded {Count} texts in {Seconds:0} s", missing.Count, (DateTime.UtcNow - started).TotalSeconds);
            }

            var firstBuild = !catalog.IsReady;
            catalog.Publish(documents.Select(d => d.Slug).ToArray(), documents.Select(d => cache[Key(d.Text)]).ToArray(), keywords);
            if (firstBuild)
            {
                // Load the model into memory now (index loaded from disk), so the first visitor's search is as fast as the rest.
                await catalog.EmbedAsync(["warm up"], ct);
                logger.LogInformation("Semantic search ready: {Documents} documents for {SubCategories} sub-categories ({Model})",
                    documents.Count, documents.Select(d => d.Slug).Distinct().Count(), o.Model);
            }
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Semantic search index not built: {Reason}. Searches work without it; retrying in 2 minutes.", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// What describes each sub-category: its name and category, its description, and the services listed under it; plus the words
    /// distinctive to its name (in at most two names, so "repair" or "services" don't count).
    /// </summary>
    private async Task<(List<(string Slug, string Text)> Documents, Dictionary<string, string[]> Keywords)> LoadDocumentsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subs = await db.SubCategories.AsNoTracking().Where(s => s.IsActive && s.Category.IsActive)
            .Select(s => new { s.Slug, s.Name, s.Description, Category = s.Category.Name }).ToListAsync(ct);
        var services = await db.Businesses.AsNoTracking().Listed().Where(b => b.SubCategory != null)
            .SelectMany(b => b.Services.Where(s => s.IsActive).Select(s => new { Sub = b.SubCategory!.Slug, s.Name }))
            .Distinct().ToListAsync(ct);
        var bySub = services.GroupBy(s => s.Sub).ToDictionary(g => g.Key, g => g.Select(s => s.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n).Take(MaxServicesPerSubCategory).ToList());

        var documents = new List<(string, string)>();
        foreach (var s in subs)
        {
            documents.Add((s.Slug, $"{s.Name}, {s.Category}"));
            if (!string.IsNullOrWhiteSpace(s.Description)) documents.Add((s.Slug, s.Description.Trim()));
            foreach (var name in bySub.GetValueOrDefault(s.Slug) ?? []) documents.Add((s.Slug, $"{name} ({s.Name})"));
        }

        var nameWords = subs.ToDictionary(s => s.Slug, s => System.Text.RegularExpressions.Regex.Split(s.Name.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(w => w.Length >= 2).Select(SemanticCatalog.Stem).Distinct().ToArray());
        var frequency = nameWords.Values.SelectMany(w => w).GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var keywords = nameWords.ToDictionary(n => n.Key, n => n.Value.Where(w => frequency[w] <= 2).ToArray());
        return (documents, keywords);
    }

    private static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    private async Task<Dictionary<string, float[]>> ReadCacheAsync(string path, string model, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path)) return [];
            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync<CacheFile>(stream, cancellationToken: ct);
            // Vectors from another model are not comparable.
            return file?.Model == model && file.Vectors is not null ? file.Vectors : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning("Semantic search: ignoring unreadable vector cache {Path} ({Reason})", path, ex.Message);
            return [];
        }
    }

    private async Task WriteCacheAsync(string path, string model, HashSet<string> keep, Dictionary<string, float[]> cache, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, new CacheFile(model, cache.Where(c => keep.Contains(c.Key)).ToDictionary()), cancellationToken: ct);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException ex)
        {
            logger.LogWarning("Semantic search: could not save the vector cache to {Path} ({Reason}); it will be rebuilt after a restart", path, ex.Message);
        }
    }

    private sealed record CacheFile(string Model, Dictionary<string, float[]>? Vectors);
}
