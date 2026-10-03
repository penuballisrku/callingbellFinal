using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

/// <summary>Suggests extra catalogue categories suited to a city (climate, economy, culture, season) with the local Ollama model.</summary>
internal sealed class OllamaCityCategoryRecommender(
    AiWorkQueue queue, AiResultCache<AiCityCategories> cache, OllamaChatClient chat, IOptions<AiOptions> options, ILogger<OllamaCityCategoryRecommender> logger)
    : ICityCategoryRecommender
{
    public bool IsEnabled => options.Value.Enabled;
    public string? Model => options.Value.Model;

    public AiCityCategories? GetCached(string key) => cache.Get(key);

    public bool IsPending(string key) => cache.IsPending(key);

    public void Enqueue(string key, AiCityCategoriesRequest request)
    {
        if (!IsEnabled || request.Candidates.Count == 0) return;
        AiJobs.Enqueue(queue, cache, logger, key, $"city categories for {request.City}", ct => SuggestAsync(request, ct), r => r.Picks.Count > 0);
    }

    private async Task<AiCityCategories?> SuggestAsync(AiCityCategoriesRequest request, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var candidates = string.Join('\n', request.Candidates.Select((c, i) => string.Create(inv,
            $"{i + 1} | {c.Name} | {c.Category} | {c.BusinessesInCity} businesses in city | {c.BookingsInCity} bookings")));
        var pickCount = Math.Min(request.PickCount, request.Candidates.Count);

        var prompt =
            $"City: {request.City}, {request.State} (visitor near {request.Place})\nLocal time: {AiText.When(request.LocalTime)}\n" +
            $"Season: {AiText.SeasonOf(request.LocalTime)}\n" +
            $"Already shown (most booked here): {string.Join(", ", request.AlreadyShown)}\n\n" +
            $"CANDIDATES (id | sub-category | category | businesses in city | bookings in 90 days):\n{candidates}\n\n" +
            $"Pick exactly {pickCount} candidate ids, best first, that residents of this city are most likely to need beyond what is already shown. " +
            "Consider the city's climate, economy, lifestyle and culture, and the season. Strongly prefer candidates with businesses in the city. " +
            "For each give a reason of at most 14 words specific to this city. Vary the wording; don't mention prices or invent facts about businesses.\n" +
            "Reply as JSON: {\"picks\":[{\"id\":1,\"reason\":\"...\"}]}";

        var content = await chat.ChatJsonAsync(
            "You are a local-market analyst for Calling Bell, an Indian local services marketplace. You know Indian cities well. " +
            "Only use the ids provided. Reply with JSON only.",
            prompt, ct);
        if (content is null) return null;

        var reply = JsonSerializer.Deserialize<Reply>(content, OllamaChatClient.Json);
        var picks = (reply?.Picks ?? [])
            .Where(p => p.Id >= 1 && p.Id <= request.Candidates.Count)
            .DistinctBy(p => p.Id)
            .Take(pickCount)
            .Select(p => new AiServicePick(request.Candidates[p.Id - 1].Key, AiText.Clean(p.Reason)))
            .ToList();
        return new AiCityCategories(picks, chat.Model, DateTimeOffset.UtcNow);
    }

    private sealed record Reply(List<Item>? Picks);
    private sealed record Item([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("reason")] string? Reason);
}
