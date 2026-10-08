using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

/// <summary>
/// Loads every Ollama model the site uses (the chat models from <see cref="AiOptions"/> and the embedding model) into memory in the
/// background once the API has started. On a CPU-only machine loading a large model takes minutes; doing it up front means the first
/// visitor's "Ask AI" or AI recommended search doesn't wait for it. Models are loaded one after another to avoid a memory spike, with the
/// same load options the real requests use (otherwise Ollama would load them again), and stay loaded for the configured KeepAlive.
/// </summary>
internal sealed class AiModelWarmup(IHttpClientFactory httpFactory, IOptions<AiOptions> ai, IOptions<EmbeddingOptions> embeddings,
    OllamaChatClient chat, IHostApplicationLifetime lifetime, ILogger<AiModelWarmup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ai.Value.Enabled) return;
        // Let the site start answering first; the database warm-up runs at the same time.
        if (!await WaitForStartAsync(stoppingToken)) return;
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var http = httpFactory.CreateClient(OllamaChatClient.HttpClientName);
        var baseUrl = ai.Value.BaseUrl.TrimEnd('/');
        if (embeddings.Value.Enabled)
            await LoadAsync(embeddings.Value.Model, () => http.PostAsJsonAsync($"{baseUrl}/api/embed",
                new { model = embeddings.Value.Model, input = "warm-up", keep_alive = embeddings.Value.KeepAlive, options = SemanticCatalog.EmbedOptions(embeddings.Value) },
                OllamaChatClient.Json, stoppingToken));
        foreach (var model in chat.ModelsInUse())
        {
            // An empty prompt loads the model without generating anything.
            await LoadAsync(model, () => http.PostAsJsonAsync($"{baseUrl}/api/generate",
                new { model, prompt = "", keep_alive = ai.Value.KeepAlive, options = OllamaChatClient.LoadOptions(ai.Value) },
                OllamaChatClient.Json, stoppingToken));
            if (stoppingToken.IsCancellationRequested) return;
        }
    }

    private async Task LoadAsync(string model, Func<Task<HttpResponseMessage>> send)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await send();
            if (response.IsSuccessStatusCode) logger.LogInformation("AI model {Model} loaded in {Seconds:0} s", model, watch.Elapsed.TotalSeconds);
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                logger.LogWarning("AI model {Model} is not installed (run `ollama pull {Model}`); its tasks use the default model meanwhile", model, model);
            else logger.LogWarning("AI model {Model} could not be loaded ({Status}); it loads on first use instead", model, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Ollama not running: the chat client reports that once and keeps checking; nothing to do here.
            logger.LogDebug(ex, "AI model {Model} warm-up skipped", model);
        }
    }

    private async Task<bool> WaitForStartAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource();
        using var _ = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, ct));
        return !ct.IsCancellationRequested;
    }
}
