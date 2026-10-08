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
    AiWorkQueue queue, AiResultCache<AiSearchIntent> intents, AiResultCache<AiPlaceRanking> rankings, AiResultCache<AiAnswer> answers,
    AiResultCache<AiRequestReading> readings, OllamaChatClient chat, IOptions<AiOptions> options, ILogger<OllamaSearchAssistant> logger) : ISearchAssistant
{
    private static readonly HashSet<string> Sorts = ["rating", "price", "reviews", "distance"];

    public AiRequestReading? GetReading(string key) => readings.Get(key);

    public bool IsReadingPending(string key) => readings.IsPending(key);

    public void RequestReading(string key, string message, string? current, IReadOnlyList<(string Slug, string Name)> services)
    {
        if (!IsEnabled) return;
        AiJobs.Enqueue(queue, readings, logger, key, $"reading of \"{Plain(message, 60)}\"", ct => ReadAsync(message, current, services, ct), _ => true,
            AiPriority.Interactive);
    }

    private async Task<AiRequestReading?> ReadAsync(string message, string? current, IReadOnlyList<(string Slug, string Name)> services, CancellationToken ct)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine($"A customer in India wrote to a local services app: \"{Plain(message, 300)}\"");
        if (current is not null) prompt.AppendLine($"Their current search, which the message may refine: {current}");
        if (services.Count > 0)
        {
            prompt.AppendLine("\nService types:");
            foreach (var s in services) prompt.AppendLine($"{s.Slug}: {s.Name}");
            prompt.AppendLine("\n\"service\": the slug of the service type they need (they may describe a problem, e.g. \"fan not spinning\" needs an " +
                "electrician), only from the list; null if none fits or the message only refines the current search.");
        }
        prompt.AppendLine(
            "\"place\": a locality or city named in the message, as written, else null.\n" +
            "Set each of these true ONLY when the customer's own words ask for it, never because the service usually works that way: " +
            "\"verified\" (verified, trusted), \"homeVisit\" (they say someone should come to their home, house or place), " +
            "\"urgent\" (right now, today, emergency), \"openNow\" (open now), \"video\" (video or online consultation), \"booking\" (book online).\n" +
            "\"minRating\": a minimum star rating asked for (1-5), else null.\n" +
            "\"sort\": \"price\" if they want cheap, affordable or within a budget; \"rating\" for the best or top rated; \"reviews\" for the most " +
            "reviewed or popular; \"distance\" for the nearest; else null.\n" +
            "Reply as JSON: {\"service\":null,\"place\":null,\"verified\":false,\"homeVisit\":false,\"urgent\":false,\"openNow\":false," +
            "\"video\":false,\"booking\":false,\"minRating\":null,\"sort\":null}");
        var content = await chat.ChatJsonAsync("You turn customer messages into search filters. Reply with JSON only.", prompt.ToString(), ct,
            maxOutputTokens: 120, task: AiTasks.RequestReading);
        if (content is null) return null;
        var reply = JsonSerializer.Deserialize<ReadingReply>(content, OllamaChatClient.Json);
        if (reply is null) return null;
        var slug = reply.Service?.Trim().ToLowerInvariant();
        if (slug is not null && !services.Any(s => s.Slug == slug)) slug = null;
        var place = string.IsNullOrWhiteSpace(reply.Place) ? null : Plain(reply.Place, 80);
        // Only take a place the customer actually wrote, so the model can't move the search somewhere else.
        if (place is not null && !message.Contains(place, StringComparison.OrdinalIgnoreCase)) place = null;
        var sort = reply.Sort?.Trim().ToLowerInvariant();
        decimal? minRating = reply.MinRating is >= 1 and <= 5 ? reply.MinRating : null;
        return new AiRequestReading(slug, place, reply.Verified, reply.HomeVisit, reply.Urgent, reply.OpenNow, reply.Video, reply.Booking,
            minRating, sort is not null && Sorts.Contains(sort) ? sort : null, chat.ModelFor(AiTasks.RequestReading), DateTimeOffset.UtcNow);
    }

    public void RequestReply(string key, string message, string facts)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(facts)) return;
        AiJobs.Enqueue(queue, answers, logger, key, $"reply to \"{Plain(message, 60)}\"", ct => ReplyAsync(message, facts, ct),
            a => a.Text.Length > 0, AiPriority.Interactive);
    }

    private async Task<AiAnswer?> ReplyAsync(string message, string facts, CancellationToken ct)
    {
        const string system =
            "You are the friendly assistant of Calling Bell, a local services marketplace in India, chatting with a customer. Reply to their " +
            "message using ONLY the facts provided, which come live from the Calling Bell database. Never invent businesses, prices, ratings, " +
            "phone numbers or places, and never promise a day, time or availability the facts don't state. In 2 to 3 short sentences: show you understood what they need, say what you found, and point out the one " +
            "or two best options by name and why (rating, reviews, price, verified, home visits, availability). The business cards are shown " +
            "below your reply, so don't list them all. If nothing was found, say so kindly and use the facts to suggest what to try. Plain text, " +
            "no markdown, no greeting. Write prices exactly as the facts give them, with their currency (₹ in India).";
        var prompt = $"Facts from the Calling Bell database:\n{facts}\n\nCustomer's message: \"{Plain(message, 300)}\"";
        var text = await chat.ChatTextAsync(system, prompt, ct, maxOutputTokens: 160, task: AiTasks.AssistantReply);
        if (text is null) return null;
        text = text.Replace("**", "").Replace("__", "").Trim().Trim('"').Trim();
        return new AiAnswer(text, chat.ModelFor(AiTasks.AssistantReply), DateTimeOffset.UtcNow);
    }

    public AiAnswer? GetAnswer(string key) => answers.Get(key);

    public bool IsAnswerPending(string key) => answers.IsPending(key);

    public void RequestAnswer(string key, string question, string facts)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(facts)) return;
        AiJobs.Enqueue(queue, answers, logger, key, $"answer to \"{Plain(question, 60)}\"", ct => AnswerAsync(question, facts, ct),
            a => a.Text.Length > 0, AiPriority.Interactive);
    }

    private async Task<AiAnswer?> AnswerAsync(string question, string facts, CancellationToken ct)
    {
        const string system =
            "You are the assistant of Calling Bell, a local services marketplace in India. Answer the customer's question using ONLY the " +
            "facts provided, which come live from the Calling Bell database. Never invent businesses, services, prices, ratings, phone numbers " +
            "or places. If the facts don't answer the question, say so in one sentence and suggest what they could search for instead. " +
            "Be warm and concise: at most 100 words, plain text (no markdown, no bold), a short numbered list when listing several items. " +
            "Write prices exactly as the facts give them, with their currency (₹ in India).";
        var prompt = $"Facts from the Calling Bell database:\n{facts}\n\nCustomer's question: \"{Plain(question, 300)}\"";
        var text = await chat.ChatTextAsync(system, prompt, ct, maxOutputTokens: 220, task: AiTasks.AssistantAnswer);
        if (text is null) return null;
        // Light clean-up: models sometimes add markdown emphasis despite the instruction.
        text = text.Replace("**", "").Replace("__", "").Trim();
        return new AiAnswer(text, chat.ModelFor(AiTasks.AssistantAnswer), DateTimeOffset.UtcNow);
    }

    public bool IsEnabled => options.Value.Enabled;

    public AiSearchIntent? GetIntent(string key) => intents.Get(key);

    public bool IsIntentPending(string key) => intents.IsPending(key);

    public AiPlaceRanking? GetRanking(string key) => rankings.Get(key);

    public bool IsRankingPending(string key) => rankings.IsPending(key);

    public void RequestIntent(string key, string query, IReadOnlyList<(string Slug, string Name)> subCategories)
    {
        if (!IsEnabled || subCategories.Count == 0) return;
        AiJobs.Enqueue(queue, intents, logger, key, $"search intent for \"{query}\"", ct => IntentAsync(query, subCategories, ct), _ => true, AiPriority.Interactive);
    }

    public void RequestRanking(string key, string query, string place, IReadOnlyList<AiPlaceCandidate> candidates)
    {
        if (!IsEnabled || candidates.Count == 0) return;
        AiJobs.Enqueue(queue, rankings, logger, key, $"place picks for \"{query}\" near {place}", ct => RankAsync(query, place, candidates, ct), _ => true,
            AiPriority.Interactive);
    }

    private async Task<AiSearchIntent?> IntentAsync(string query, IReadOnlyList<(string Slug, string Name)> subs, CancellationToken ct)
    {
        var list = string.Join('\n', subs.Select(s => $"{s.Slug}: {s.Name}"));
        var prompt =
            $"A customer in India searched a local services app for: \"{Plain(query, 160)}\"\n" +
            $"(They may name a service, or describe a problem such as \"water dripping from the ceiling\".)\n\nService types:\n{list}\n\n" +
            "Which service types (at most 3, best first) is the customer looking for? Use only slugs from the list; return an empty list if none fit. " +
            "Reply as JSON: {\"slugs\":[\"...\"]}";
        var content = await chat.ChatJsonAsync("You map search queries to service types. Reply with JSON only.", prompt, ct, maxOutputTokens: 80, task: AiTasks.SearchIntent);
        if (content is null) return null;
        var allowed = subs.Select(s => s.Slug).ToHashSet(StringComparer.Ordinal);
        var slugs = (JsonSerializer.Deserialize<IntentReply>(content, OllamaChatClient.Json)?.Slugs ?? [])
            .Select(s => s?.Trim().ToLowerInvariant() ?? "").Where(allowed.Contains).Distinct().Take(3).ToList();
        return new AiSearchIntent(slugs, chat.ModelFor(AiTasks.SearchIntent), DateTimeOffset.UtcNow);
    }

    private async Task<AiPlaceRanking?> RankAsync(string query, string place, IReadOnlyList<AiPlaceCandidate> candidates, CancellationToken ct)
    {
        var lines = string.Join('\n', candidates.Select((c, i) =>
            $"{i + 1}. {Plain(c.Name, 70)}{(c.Kind is null ? "" : $" ({c.Kind})")}{(c.Details is null ? "" : $": {Plain(c.Details, 120)}")}"));
        var prompt =
            $"A customer near {place} is looking for: {Plain(query, 80)}\n\nPlaces nearby (from OpenStreetMap):\n{lines}\n\n" +
            $"Which of these places most likely provide what the customer wants? Leave out places that clearly don't (e.g. a shop that only shares " +
            $"a word in its name). Pick at most {MaxPicks}, most relevant first, and for each write the customer a reason of at most 15 words: what " +
            "the place offers and how near it is, e.g. \"Electrician, the closest option at 6.1 km\". Don't repeat its name, talk about its name, or mention details that are missing. " +
            "Mention only details listed on that place's own line (its type, distance, phone, hours, website, address); never say it has a " +
            "phone, hours or website unless its line lists them. Reply as JSON: {\"picks\":[{\"n\":<number>,\"why\":\"<reason>\"}]}";
        var content = await chat.ChatJsonAsync("You judge whether local businesses match a search and explain your picks. Reply with JSON only.",
            prompt, ct, maxOutputTokens: 60 + Math.Min(candidates.Count, MaxPicks) * 32, task: AiTasks.PlaceRanking);
        if (content is null) return null;
        var reply = JsonSerializer.Deserialize<RankReply>(content, OllamaChatClient.Json);
        // Older prompt shape ({"relevant":[1,2]}) is still accepted, without reasons.
        var picks = (reply?.Picks ?? []).Where(p => p is not null).Select(p => (p!.N, p.Why))
            .Concat((reply?.Relevant ?? []).Select(n => (n, (string?)null)))
            .Where(p => p.Item1 >= 1 && p.Item1 <= candidates.Count).DistinctBy(p => p.Item1).Take(MaxPicks).ToList();
        var ids = picks.Select(p => candidates[p.Item1 - 1].Id).ToList();
        var reasons = picks.Where(p => !string.IsNullOrWhiteSpace(p.Item2) && Supported(p.Item2!, candidates[p.Item1 - 1]))
            .ToDictionary(p => candidates[p.Item1 - 1].Id, p => AiText.Clean(WithoutName(p.Item2!, candidates[p.Item1 - 1].Name)));
        return new AiPlaceRanking(ids, chat.ModelFor(AiTasks.PlaceRanking), DateTimeOffset.UtcNow, reasons);
    }

    /// <summary>The card already shows the name: "Habibs Salon, a salon on Prenderghast Road" becomes "A salon on Prenderghast Road".</summary>
    private static string WithoutName(string why, string name)
    {
        var text = why.Trim();
        if (!text.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return text;
        text = text[name.Length..].TrimStart(',', ' ', '-', ':', '–', '—');
        if (text.StartsWith("is ", StringComparison.OrdinalIgnoreCase)) text = text[3..];
        return text.Length == 0 ? why : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A reason may only mention a phone, website or opening hours that the place actually lists; otherwise it is dropped.</summary>
    private static bool Supported(string why, AiPlaceCandidate c)
    {
        var details = c.Details ?? "";
        bool Says(params string[] words) => words.Any(w => why.Contains(w, StringComparison.OrdinalIgnoreCase));
        return !(Says("phone", "call") && !details.Contains("phone listed"))
            && !(Says("website", "online") && !details.Contains("website"))
            && !(Says("hours", "open") && !details.Contains("hours"));
    }

    /// <summary>The most places the AI picks (and explains) per search; the rest follow nearest first.</summary>
    private const int MaxPicks = 10;

    /// <summary>User or map text made safe to quote in a prompt: one line, no quotes, at most <paramref name="max"/> characters.</summary>
    private static string Plain(string text, int max)
    {
        var t = new string(text.ReplaceLineEndings(" ").Where(c => c is not ('"' or '\\' or '`') && !char.IsControl(c)).ToArray()).Trim();
        return t.Length <= max ? t : t[..max].TrimEnd();
    }

    private sealed record IntentReply(List<string?>? Slugs);
    private sealed record RankReply(List<RankPick?>? Picks, List<int>? Relevant);
    private sealed record RankPick(int N, string? Why);

    private sealed record ReadingReply(string? Service, string? Place, bool Verified, bool HomeVisit, bool Urgent, bool OpenNow, bool Video, bool Booking,
        decimal? MinRating, string? Sort);
}
