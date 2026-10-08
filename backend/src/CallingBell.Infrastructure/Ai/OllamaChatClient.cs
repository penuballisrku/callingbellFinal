using System.Globalization;
using System.Net.Http.Json;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string Section = "Ai";
    /// <summary>Turns AI enrichment off entirely; database results are then shown on their own.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Ollama server (free, runs locally: https://ollama.com). Nothing is sent to a third-party service.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";
    /// <summary>Any installed Ollama chat model, e.g. qwen3-coder, llama3.1, qwen2.5. Used for every task not listed in <see cref="TaskModels"/>.</summary>
    public string Model { get; set; } = "qwen3-coder";
    /// <summary>
    /// A different model for particular tasks (keys from <see cref="AiTasks"/>), e.g. a stronger model for picking and a faster one for
    /// writing replies. A model that isn't installed falls back to <see cref="Model"/>.
    /// </summary>
    public Dictionary<string, string> TaskModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Upper bound for one generation, including loading the model into memory on first use.</summary>
    public int TimeoutSeconds { get; set; } = 300;
    /// <summary>Cap on generated tokens per answer (Ollama num_predict).</summary>
    public int MaxOutputTokens { get; set; } = 700;
    /// <summary>How long an AI result is reused for the same place.</summary>
    public int CacheHours { get; set; } = 6;
    /// <summary>After a failure (e.g. Ollama not running), wait this long before trying the same place again.</summary>
    public int RetryAfterMinutes { get; set; } = 10;
    /// <summary>How long Ollama keeps the model loaded between requests.</summary>
    public string KeepAlive { get; set; } = "30m";
    /// <summary>
    /// CPU threads the model may use (Ollama num_thread). Keep below the machine's core count so the website stays responsive while
    /// AI jobs run; 0 lets Ollama use every core.
    /// </summary>
    public int Threads { get; set; } = 2;
    /// <summary>
    /// Context window per request (Ollama num_ctx), in tokens. The prompts here are short, and memory for the context is reserved up front,
    /// so a small window keeps each loaded model far smaller and quicker to load (models default to very long windows). 0 = model default.
    /// </summary>
    public int ContextLength { get; set; } = 4096;
}

/// <summary>Names of AI tasks that can have their own model (<see cref="AiOptions.TaskModels"/>).</summary>
internal static class AiTasks
{
    /// <summary>AI recommended: which catalogue services a free-text search means (JSON).</summary>
    public const string SearchIntent = "SearchIntent";
    /// <summary>AI recommended: which real nearby places fit the search, with a short reason for each (JSON).</summary>
    public const string PlaceRanking = "PlaceRanking";
    /// <summary>Ask AI: turns the customer's message into search filters (JSON).</summary>
    public const string RequestReading = "RequestReading";
    /// <summary>Ask AI: the short chat reply above the matching businesses (text).</summary>
    public const string AssistantReply = "AssistantReply";
    /// <summary>Ask AI answers to questions, and the AI overview on the AI recommended tab (text).</summary>
    public const string AssistantAnswer = "AssistantAnswer";
}

/// <summary>Ollama isn't reachable (not running) or the model isn't pulled; callers fall back to database results without a stack trace.</summary>
internal sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Minimal client for Ollama's chat API in JSON mode (free local inference).</summary>
internal sealed class OllamaChatClient(IHttpClientFactory httpFactory, IOptions<AiOptions> options, ILogger<OllamaChatClient> logger)
{
    public const string HttpClientName = "ollama";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Request bodies leave out unset fields (e.g. no "format" for a plain-text answer).</summary>
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// While Ollama is unavailable every call fails at once for <see cref="AiOptions.RetryAfterMinutes"/> instead of trying again,
    /// and the outage is logged once rather than per place and feature.
    /// </summary>
    private long _unavailableUntilTicks;

    public string Model => options.Value.Model;

    /// <summary>Task models found not to be installed; their tasks use <see cref="AiOptions.Model"/> until the API restarts.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _missingModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The model a task runs on: its own from <see cref="AiOptions.TaskModels"/> when set and installed, else the default.</summary>
    public string ModelFor(string? task)
    {
        var o = options.Value;
        return task is not null && o.TaskModels.TryGetValue(task, out var m) && !string.IsNullOrWhiteSpace(m) && !_missingModels.ContainsKey(m)
            ? m.Trim() : o.Model;
    }

    /// <summary>Sends one system + user message and returns the model's JSON reply text (null when empty).</summary>
    /// <param name="maxOutputTokens">Overrides <see cref="AiOptions.MaxOutputTokens"/> for tasks with longer answers.</param>
    /// <param name="task">One of <see cref="AiTasks"/>, to run on that task's model.</param>
    public Task<string?> ChatJsonAsync(string system, string user, CancellationToken ct, int? maxOutputTokens = null, string? task = null) =>
        ChatAsync(system, user, json: true, temperature: 0.2, maxOutputTokens, task, ct);

    /// <summary>Sends one system + user message and returns the model's plain-text reply (null when empty).</summary>
    public Task<string?> ChatTextAsync(string system, string user, CancellationToken ct, int? maxOutputTokens = null, string? task = null) =>
        ChatAsync(system, user, json: false, temperature: 0.3, maxOutputTokens, task, ct);

    private async Task<string?> ChatAsync(string system, string user, bool json, double temperature, int? maxOutputTokens, string? task,
        CancellationToken ct)
    {
        var o = options.Value;
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _unavailableUntilTicks) && !await CameBackAsync(o, ct))
            throw new AiUnavailableException($"Ollama at {o.BaseUrl} is unavailable");
        var model = ModelFor(task);
        // Caps the answer length so a runaway reply can't tie up the shared model.
        var modelOptions = LoadOptions(o);
        modelOptions["temperature"] = temperature;
        modelOptions["num_predict"] = maxOutputTokens ?? o.MaxOutputTokens;
        var body = new
        {
            model, stream = false, format = json ? "json" : null, keep_alive = o.KeepAlive,
            options = modelOptions,
            messages = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
        HttpResponseMessage sent;
        try
        {
            sent = await httpFactory.CreateClient(HttpClientName).PostAsJsonAsync($"{o.BaseUrl.TrimEnd('/')}/api/chat", body, RequestJson, cts.Token);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            // Connection refused / host not found: Ollama isn't running.
            throw Unavailable(o, $"no Ollama server at {o.BaseUrl} ({ex.Message}). Start it with `ollama serve`", ex);
        }
        using var response = sent;
        // Ollama answers 404 when the model hasn't been pulled. A task's own model falls back to the default model.
        if (response.StatusCode == HttpStatusCode.NotFound && model != o.Model)
        {
            if (_missingModels.TryAdd(model, true))
                logger.LogWarning("Ollama model '{Model}' for {Task} is not installed, so that task uses '{Default}' instead. Pull the model and restart the API to use it.",
                    model, task, o.Model);
            return await ChatAsync(system, user, json, temperature, maxOutputTokens, task, ct);
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw Unavailable(o, $"Ollama model '{o.Model}' is not installed. Run `ollama pull {o.Model}`", null);
        response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, cts.Token);
        return string.IsNullOrWhiteSpace(chat?.Message?.Content) ? null : chat.Message.Content;
    }

    /// <summary>
    /// Options that decide how Ollama loads a model (threads, context window). Every request and the start-up warm-up must send the same
    /// values, or Ollama reloads the model to apply them.
    /// </summary>
    internal static Dictionary<string, object> LoadOptions(AiOptions o)
    {
        var options = new Dictionary<string, object>();
        if (o.Threads > 0) options["num_thread"] = o.Threads;
        if (o.ContextLength > 0) options["num_ctx"] = o.ContextLength;
        return options;
    }

    /// <summary>Every chat model in use: the default one and each task's own.</summary>
    internal IEnumerable<string> ModelsInUse() =>
        new[] { options.Value.Model }.Concat(options.Value.TaskModels.Values).Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>When the last check during a back-off ran; checks are at most <see cref="ProbeEvery"/> apart.</summary>
    private long _lastProbeTicks;
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(15);

    /// <summary>
    /// During a back-off, a quick check (at most every <see cref="ProbeEvery"/>) of whether Ollama has been started since, so AI comes back
    /// within seconds instead of after <see cref="AiOptions.RetryAfterMinutes"/>. Ends the back-off when it answers.
    /// </summary>
    private async Task<bool> CameBackAsync(AiOptions o, CancellationToken ct)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastProbeTicks);
        if (now - last < ProbeEvery.Ticks || Interlocked.CompareExchange(ref _lastProbeTicks, now, last) != last) return false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await httpFactory.CreateClient(HttpClientName).GetAsync($"{o.BaseUrl.TrimEnd('/')}/api/version", cts.Token);
            if (!response.IsSuccessStatusCode) return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
        Interlocked.Exchange(ref _unavailableUntilTicks, 0);
        logger.LogInformation("AI is available again: Ollama at {BaseUrl} is answering.", o.BaseUrl);
        return true;
    }

    /// <summary>Starts the back-off, logging only when it wasn't already in effect.</summary>
    private AiUnavailableException Unavailable(AiOptions o, string reason, Exception? inner)
    {
        var minutes = Math.Max(1, o.RetryAfterMinutes);
        var now = DateTime.UtcNow.Ticks;
        if (Interlocked.Exchange(ref _unavailableUntilTicks, DateTime.UtcNow.AddMinutes(minutes).Ticks) < now)
            logger.LogWarning("AI is unavailable: {Reason}, or set Ai:Enabled to false. Database results are shown meanwhile; AI resumes as soon as Ollama answers (checked every 15 s, full retry in {Minutes} min).",
                reason, minutes);
        return new AiUnavailableException(reason, inner);
    }

    private sealed record ChatResponse(ChatMessage? Message);
    private sealed record ChatMessage(string? Content);
}

/// <summary>Prompt and reply helpers shared by the AI recommenders.</summary>
internal static class AiText
{
    /// <summary>
    /// Indian season for the month, given to the model as fact so it doesn't guess the weather
    /// (broad national pattern; the north-east monsoon makes Oct-Dec wet in Tamil Nadu).
    /// </summary>
    public static string SeasonOf(DateTimeOffset local) => local.Month switch
    {
        3 or 4 or 5 => "Summer: hot and dry, peak AC and cooling demand",
        6 or 7 or 8 or 9 => "South-west monsoon: rain, humidity, leaks, pests and water-borne illness",
        10 or 11 => "Post-monsoon festive season (Dussehra, Diwali): mild warmth, home cleaning, repairs, makeovers and celebrations",
        _ => "Winter and wedding season: cool mornings, weddings, events and travel",
    };

    public static string When(DateTimeOffset local) => local.ToString("dddd d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture);

    public static string Km(double? km) => km is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v:0.0} km") : "distance unknown";

    /// <summary>One tidy sentence: trimmed, unquoted, at most ~120 characters.</summary>
    public static string Clean(string? reason)
    {
        var text = (reason ?? string.Empty).Trim().Trim('"', '\'', ' ').ReplaceLineEndings(" ");
        if (text.Length > 120) text = text[..117].TrimEnd() + "...";
        return text.Length > 0 && !".!?".Contains(text[^1]) ? text + "." : text;
    }
}
