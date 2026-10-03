using System.Text.Json;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

/// <summary>
/// Local Ollama model helping searches that go beyond the platform. It only ever chooses from what it is given: catalogue sub-categories
/// for a free-text search, and which real nearby places (from OpenStreetMap) fit the search. Answers outside those lists are ignored.
/// </summary>
internal sealed class OllamaSearchAssistant(
    AiWorkQueue queue, AiResultCache<AiSearchIntent> intents, AiResultCache<AiPlaceRanking> rankings, OllamaChatClient chat,
    IOptions<AiOptions> options, ILogger<OllamaSearchAssistant> logger) : ISearchAssistant
{
    public bool IsEnabled => options.Value.Enabled;

    public AiSearchIntent? GetIntent(string key) => intents.Get(key);

    public bool IsIntentPending(string key) => intents.IsPending(key);

    public AiPlaceRanking? GetRanking(string key) => rankings.Get(key);

    public bool IsRankingPending(string key) => rankings.IsPending(key);

    public void RequestIntent(string key, string query, IReadOnlyList<(string Slug, string Name)> subCategories)
    {
        if (!IsEnabled || subCategories.Count == 0) return;
        AiJobs.Enqueue(queue, intents, logger, key, $"search intent for \"{query}\"", ct => IntentAsync(query, subCategories, ct), _ => true);
    }

    public void RequestRanking(string key, string query, string place, IReadOnlyList<AiPlaceCandidate> candidates)
    {
        if (!IsEnabled || candidates.Count == 0) return;
        AiJobs.Enqueue(queue, rankings, logger, key, $"place picks for \"{query}\" near {place}", ct => RankAsync(query, place, candidates, ct), _ => true);
    }

    private async Task<AiSearchIntent?> IntentAsync(string query, IReadOnlyList<(string Slug, string Name)> subs, CancellationToken ct)
    {
        var list = string.Join('\n', subs.Select(s => $"{s.Slug}: {s.Name}"));
        var prompt =
            $"A customer in India searched a local services app for: \"{Plain(query, 80)}\"\n\nService types:\n{list}\n\n" +
            "Which service types (at most 3, best first) is the customer looking for? Use only slugs from the list; return an empty list if none fit. " +
            "Reply as JSON: {\"slugs\":[\"...\"]}";
        var content = await chat.ChatJsonAsync("You map search queries to service types. Reply with JSON only.", prompt, ct, maxOutputTokens: 80);
        if (content is null) return null;
        var allowed = subs.Select(s => s.Slug).ToHashSet(StringComparer.Ordinal);
        var slugs = (JsonSerializer.Deserialize<IntentReply>(content, OllamaChatClient.Json)?.Slugs ?? [])
            .Select(s => s?.Trim().ToLowerInvariant() ?? "").Where(allowed.Contains).Distinct().Take(3).ToList();
        return new AiSearchIntent(slugs, chat.Model, DateTimeOffset.UtcNow);
    }

    private async Task<AiPlaceRanking?> RankAsync(string query, string place, IReadOnlyList<AiPlaceCandidate> candidates, CancellationToken ct)
    {
        var lines = string.Join('\n', candidates.Select((c, i) => $"{i + 1}. {Plain(c.Name, 70)}{(c.Kind is null ? "" : $" ({c.Kind})")}"));
        var prompt =
            $"A customer near {place} is looking for: {Plain(query, 80)}\n\nPlaces nearby (name and type from OpenStreetMap):\n{lines}\n\n" +
            "Which of these places most likely provide what the customer wants? Leave out places that clearly don't (e.g. a shop that only shares " +
            "a word in its name). List their numbers, most relevant first. Reply as JSON: {\"relevant\":[1,2]}";
        var content = await chat.ChatJsonAsync("You judge whether local businesses match a search. Reply with JSON only.", prompt, ct,
            maxOutputTokens: 40 + candidates.Count * 4);
        if (content is null) return null;
        var ids = (JsonSerializer.Deserialize<RankReply>(content, OllamaChatClient.Json)?.Relevant ?? [])
            .Where(n => n >= 1 && n <= candidates.Count).Distinct().Select(n => candidates[n - 1].Id).ToList();
        return new AiPlaceRanking(ids, chat.Model, DateTimeOffset.UtcNow);
    }

    /// <summary>User or map text made safe to quote in a prompt: one line, no quotes, at most <paramref name="max"/> characters.</summary>
    private static string Plain(string text, int max)
    {
        var t = new string(text.ReplaceLineEndings(" ").Where(c => c is not ('"' or '\\' or '`') && !char.IsControl(c)).ToArray()).Trim();
        return t.Length <= max ? t : t[..max].TrimEnd();
    }

    private sealed record IntentReply(List<string?>? Slugs);
    private sealed record RankReply(List<int>? Relevant);
}
