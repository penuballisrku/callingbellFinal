using System.Text.Json;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

/// <summary>
/// Summarises real customer reviews of a place in two sentences with the local Ollama model. The model only sees ratings and review
/// text (no names) and is told not to add anything the reviews don't say.
/// </summary>
internal sealed class OllamaReviewSummarizer(
    AiWorkQueue queue, AiResultCache<AiReviewSummary> cache, OllamaChatClient chat, IOptions<AiOptions> options, ILogger<OllamaReviewSummarizer> logger)
    : IReviewSummarizer
{
    public bool IsEnabled => options.Value.Enabled;

    public AiReviewSummary? GetCached(string key) => cache.Get(key);

    public bool IsPending(string key) => cache.IsPending(key);

    public void Enqueue(string key, AiReviewSummaryRequest request)
    {
        if (!IsEnabled || request.Reviews.Count == 0) return;
        AiJobs.Enqueue(queue, cache, logger, key, $"review summary for {request.Place}", ct => SummariseAsync(request, ct), r => r.Text.Length > 0);
    }

    private async Task<AiReviewSummary?> SummariseAsync(AiReviewSummaryRequest request, CancellationToken ct)
    {
        var lines = string.Join('\n', request.Reviews.Select((r, i) => $"{i + 1}. ({r.Rating}/5) {Trim(r.Text, 220)}"));
        var prompt =
            $"Customer reviews of local businesses in {request.Place}, {request.City}:\n{lines}\n\n" +
            "In at most 2 sentences (45 words), summarise what these customers most often praise. Use only what the reviews say; " +
            "do not name people or businesses, quote prices, or invent details. Reply as JSON: {\"summary\":\"...\"}";
        var content = await chat.ChatJsonAsync("You summarise customer reviews accurately and neutrally. Reply with JSON only.", prompt, ct,
            maxOutputTokens: 200);
        if (content is null) return null;
        var text = JsonSerializer.Deserialize<Reply>(content, OllamaChatClient.Json)?.Summary?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(text)) return null;
        return new AiReviewSummary(Trim(text.ReplaceLineEndings(" "), 320), chat.Model, DateTimeOffset.UtcNow);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    private sealed record Reply(string? Summary);
}
