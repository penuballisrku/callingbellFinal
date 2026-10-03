using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Ai;

/// <summary>Re-ranks the database shortlist of nearby services (and suggests related categories) with the local Ollama model.</summary>
internal sealed class OllamaServiceRecommender(
    AiWorkQueue queue, AiResultCache<AiServiceRanking> cache, OllamaChatClient chat, IOptions<AiOptions> options, ILogger<OllamaServiceRecommender> logger)
    : IServiceRecommender
{
    private const int RelatedIdBase = 101;

    public bool IsEnabled => options.Value.Enabled;
    public string? Model => options.Value.Model;

    public AiServiceRanking? GetCached(string key) => cache.Get(key);

    public bool IsPending(string key) => cache.IsPending(key);

    public void Enqueue(string key, AiServiceRankingRequest request)
    {
        if (!IsEnabled) return;
        AiJobs.Enqueue(queue, cache, logger, key, $"service ranking for {request.Place}, {request.City}",
            ct => RankAsync(request, ct), r => r.Picks.Count > 0);
    }

    private async Task<AiServiceRanking?> RankAsync(AiServiceRankingRequest request, CancellationToken ct)
    {
        // Compact one-line candidates keep the prompt short: local CPU inference spends most of its time reading the prompt.
        var inv = CultureInfo.InvariantCulture;
        var services = string.Join('\n', request.Candidates.Select((c, i) => string.Create(inv,
            $"{i + 1} | {c.Name} | {c.SubCategory} | {c.RecentBookingsNearby} bookings | {c.Rating:0.0} rating | {AiText.Km(c.NearestKm)}")));
        // Separate id range (101+) so service and category ids can't be confused.
        var categories = string.Join('\n', request.RelatedCandidates.Select((c, i) => string.Create(inv,
            $"{RelatedIdBase + i} | {c.Name} | {c.Category} | {c.BusinessesNearby} businesses | {c.RecentBookingsNearby} bookings | {AiText.Km(c.NearestKm)}")));
        var pickCount = Math.Min(request.PickCount, request.Candidates.Count);
        var relatedCount = Math.Min(request.RelatedCount, request.RelatedCandidates.Count);

        var prompt =
            $"Place: {request.Place}, {request.City}, {request.State}\nLocal time: {AiText.When(request.LocalTime)}\nSeason: {AiText.SeasonOf(request.LocalTime)}\n\n" +
            $"SERVICES (id | service | sub-category | bookings nearby in 90 days | rating | nearest provider):\n{services}\n\n" +
            $"CATEGORIES (id | sub-category | category | businesses nearby | bookings | nearest):\n{categories}\n\n" +
            $"1. Pick exactly {pickCount} SERVICES ids, best first, with a reason (max 14 words) why each is in demand here now.\n" +
            $"2. Pick {relatedCount} CATEGORIES ids that people booking those services are also likely to need here now, " +
            "with a reason (max 12 words) how each complements them.\n" +
            "Vary the wording, don't repeat the city name in every reason, base weather or festival remarks only on the given season, " +
            "don't mention prices or invent facts about businesses.\n" +
            "Reply as JSON: {\"picks\":[{\"id\":1,\"reason\":\"...\"}],\"related\":[{\"id\":101,\"reason\":\"...\"}]}";

        var content = await chat.ChatJsonAsync(
            "You are a local-services analyst for Calling Bell, an Indian local services marketplace. Choose what people near the " +
            "given place most likely need right now: weigh recent local bookings and ratings most, then the season, festivals and " +
            "how close the nearest provider is. Only use the ids provided. Reply with JSON only.",
            prompt, ct);
        if (content is null) return null;

        var reply = JsonSerializer.Deserialize<Reply>(content, OllamaChatClient.Json);
        var picks = (reply?.Picks ?? [])
            .Where(p => p.Id >= 1 && p.Id <= request.Candidates.Count)
            .DistinctBy(p => p.Id)
            .Take(request.PickCount)
            .Select(p => new AiServicePick(request.Candidates[p.Id - 1].Key, AiText.Clean(p.Reason)))
            .ToList();
        var related = (reply?.Related ?? [])
            .Where(p => p.Id >= RelatedIdBase && p.Id < RelatedIdBase + request.RelatedCandidates.Count)
            .DistinctBy(p => p.Id)
            .Take(relatedCount)
            .Select(p => new AiServicePick(request.RelatedCandidates[p.Id - RelatedIdBase].Key, AiText.Clean(p.Reason)))
            .ToList();
        return new AiServiceRanking(picks, related, chat.Model, DateTimeOffset.UtcNow);
    }

    private sealed record Reply(List<Item>? Picks, List<Item>? Related);
    private sealed record Item([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("reason")] string? Reason);
}
