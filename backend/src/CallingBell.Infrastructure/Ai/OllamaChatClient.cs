using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string Section = "Ai";
    /// <summary>Turns AI enrichment off entirely; database results are then shown on their own.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Ollama server (free, runs locally: https://ollama.com). Nothing is sent to a third-party service.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";
    /// <summary>Any installed Ollama chat model, e.g. qwen3-coder, llama3.1, qwen2.5.</summary>
    public string Model { get; set; } = "qwen3-coder";
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
}

/// <summary>Minimal client for Ollama's chat API in JSON mode (free local inference).</summary>
internal sealed class OllamaChatClient(IHttpClientFactory httpFactory, IOptions<AiOptions> options)
{
    public const string HttpClientName = "ollama";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Model => options.Value.Model;

    /// <summary>Sends one system + user message and returns the model's JSON reply text (null when empty).</summary>
    /// <param name="maxOutputTokens">Overrides <see cref="AiOptions.MaxOutputTokens"/> for tasks with longer answers.</param>
    public async Task<string?> ChatJsonAsync(string system, string user, CancellationToken ct, int? maxOutputTokens = null)
    {
        var o = options.Value;
        var body = new
        {
            model = o.Model, stream = false, format = "json", keep_alive = o.KeepAlive,
            // Caps the answer length so a runaway reply can't tie up the shared model.
            options = o.Threads > 0
                ? (object)new { temperature = 0.2, num_predict = maxOutputTokens ?? o.MaxOutputTokens, num_thread = o.Threads }
                : new { temperature = 0.2, num_predict = maxOutputTokens ?? o.MaxOutputTokens },
            messages = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
        using var response = await httpFactory.CreateClient(HttpClientName).PostAsJsonAsync($"{o.BaseUrl.TrimEnd('/')}/api/chat", body, Json, cts.Token);
        response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, cts.Token);
        return string.IsNullOrWhiteSpace(chat?.Message?.Content) ? null : chat.Message.Content;
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
