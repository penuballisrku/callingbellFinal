using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Search;

// ===================== Contracts =====================

/// <summary>
/// The search the assistant ran, in the same terms as <c>GET /api/businesses</c> and the search page's URL. The client sends it back with
/// the next message so follow-ups ("only verified ones", "cheapest first") refine it instead of starting over.
/// </summary>
public sealed record AssistantFiltersDto(
    string? Q = null, string? Category = null, string? Sub = null, string? City = null, Guid? AreaId = null, decimal? MinRating = null,
    string? Availability = null, bool OpenNow = false, bool VerifiedOnly = false, bool HomeService = false, bool VideoConsultation = false,
    bool OnlineBooking = false, string? Sort = null);

/// <summary>An applied filter, shown as a removable chip. <see cref="Key"/> names the <see cref="AssistantFiltersDto"/> property.</summary>
public sealed record AssistantChipDto(string Key, string Label);

/// <param name="Message">The assistant's answer, one or two sentences.</param>
/// <param name="Understood">The service type the request was matched to (e.g. "AC Repair"), when one was found.</param>
/// <param name="Results">The best matches (a few); <paramref name="Total"/> counts them all.</param>
/// <param name="Suggestions">Follow-up requests the visitor can tap, e.g. "Only verified".</param>
/// <param name="Relaxed">Filters dropped because nothing matched them all, e.g. ["4★ & up"].</param>
/// <param name="AiPending">The local AI is still working out what the request means; send the same request again shortly for a better answer.</param>
/// <param name="AiUsed">The service type was chosen by the AI.</param>
public sealed record AssistantReplyDto(
    string Message, string? Understood, AssistantFiltersDto Filters, IReadOnlyList<AssistantChipDto> Chips, IReadOnlyList<BusinessCardDto> Results,
    int Total, IReadOnlyList<string> Suggestions, IReadOnlyList<string> Relaxed, bool AiPending, bool AiUsed);

/// <param name="Message">What the visitor typed. May be empty when only <paramref name="Context"/> changed (a chip was removed).</param>
/// <param name="Context">The filters of the previous answer in this conversation, if any.</param>
/// <param name="City">The visitor's selected city, used when the request names no place.</param>
/// <param name="AreaId">The visitor's selected area within <paramref name="City"/>.</param>
public sealed record AskSearchAssistantCommand(string? Message, AssistantFiltersDto? Context, string? City, Guid? AreaId, double? Lat, double? Lng)
    : IRequest<AssistantReplyDto>;

public sealed class AskSearchAssistantValidator : AbstractValidator<AskSearchAssistantCommand>
{
    public AskSearchAssistantValidator()
    {
        RuleFor(x => x.Message).MaximumLength(300);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Message) || x.Context is not null)
            .OverridePropertyName("message").WithMessage("Tell me what you are looking for.");
        RuleFor(x => x.City).MaximumLength(120);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null);
    }
}

/// <param name="CityName">The visitor's city, when one was given and found.</param>
/// <param name="CityHasListings">False when the city has no listed businesses yet; the prompts then name cities that do.</param>
/// <param name="Prompts">Example requests built from the services actually listed there.</param>
public sealed record AssistantStartersDto(string? CityName, bool CityHasListings, IReadOnlyList<string> Prompts);

public sealed record GetAssistantStartersQuery(string? City) : IRequest<AssistantStartersDto>;

/// <summary>Example requests for the assistant's welcome screen, so every example finds businesses for the visitor.</summary>
public sealed class GetAssistantStartersHandler(IUnitOfWork uow, ReferenceDataCache reference) : IRequestHandler<GetAssistantStartersQuery, AssistantStartersDto>
{
    private static readonly string[] Templates =
        ["Verified {0}{1} available right now", "Top rated {0}{1} who can visit my home", "Cheapest {0}{1}", "{0}{1} with online booking, 4 stars and up"];

    public async Task<AssistantStartersDto> Handle(GetAssistantStartersQuery r, CancellationToken ct)
    {
        var listed = uow.Repository<Business>().QueryNoTracking().Listed().Where(b => b.SubCategory != null);
        var cityName = string.IsNullOrWhiteSpace(r.City) ? null
            : await uow.Repository<City>().QueryNoTracking().Where(c => c.Slug == r.City).Select(c => c.Name).FirstOrDefaultAsync(ct);

        // The services with the most listings in the visitor's city.
        var local = cityName is null ? [] : await listed.Where(b => b.CityRef != null && b.CityRef.Slug == r.City)
            .GroupBy(b => b.SubCategory!.Name).OrderByDescending(g => g.Count()).Take(Templates.Length).Select(g => g.Key).ToListAsync(ct);
        if (local.Count > 0)
            return new AssistantStartersDto(cityName, true, local.Select((s, i) => Prompt(i, s, null)).ToList());

        // None there (or no city): the busiest service and city pairs elsewhere, naming the city so each example finds results.
        var pairs = await listed.GroupBy(b => new { Sub = b.SubCategory!.Name, b.City })
            .Select(g => new { g.Key.Sub, g.Key.City, Count = g.Count() }).ToListAsync(ct);
        // Prefer the cities nearest the visitor's, then the busiest services there.
        var all = await reference.ActiveCitiesAsync(ct);
        var from = all.FirstOrDefault(c => c.Slug == r.City);
        double Distance(string name) =>
            all.FirstOrDefault(c => c.Name == name) is { Latitude: { } lat, Longitude: { } lng } && from is { Latitude: { } fLat, Longitude: { } fLng }
                ? Math.Pow(lat - fLat, 2) + Math.Pow((lng - fLng) * Math.Cos(fLat * Math.PI / 180), 2)
                : double.MaxValue;
        var picks = pairs.OrderBy(p => Distance(p.City)).ThenByDescending(p => p.Count).DistinctBy(p => p.Sub).Take(Templates.Length).ToList();
        // Without a city (not chosen or detected yet) the examples name none; the search then runs nationwide.
        return new AssistantStartersDto(cityName, cityName is null, picks.Select((p, i) => Prompt(i, p.Sub, cityName is null ? null : p.City)).ToList());
    }

    private static string Prompt(int i, string sub, string? city)
    {
        var place = city is null ? "" : $" in {city}";
        return string.Format(CultureInfo.InvariantCulture, Templates[i % Templates.Length], i % Templates.Length == 3 ? sub : sub.ToLowerInvariant(), place);
    }
}

// ===================== Handler =====================

/// <summary>
/// Conversational search: turns a request such as "my AC is leaking, need someone today in Madhapur" into the platform's search filters,
/// runs the search and explains the result.
/// <list type="number">
/// <item>Rules read the instant parts: urgency, verified, home visit, video, booking, rating, sort, and the place (via <see cref="ParseSearchHandler"/>).</item>
/// <item>The service type is matched from the database: sub-category and category names, then the services businesses list.</item>
/// <item>Only when that fails does the local AI pick the service type from the catalogue (<see cref="ISearchAssistant"/>, in the background;
/// the answer says <see cref="AssistantReplyDto.AiPending"/> and the client asks again). It never invents businesses or categories.</item>
/// <item>When nothing matches every filter, filters are relaxed one at a time and the answer says which.</item>
/// </list>
/// </summary>
public sealed partial class AskSearchAssistantHandler(ISender sender, IUnitOfWork uow, ReferenceDataCache reference, ISearchAssistant ai,
    ISemanticCatalog semanticCatalog)
    : IRequestHandler<AskSearchAssistantCommand, AssistantReplyDto>
{
    private const int ResultCount = 5;

    /// <summary>Conversation filler, removed before the remaining words are matched to a service.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "im", "i'm", "we", "need", "needs", "want", "wanted", "looking", "look", "for", "find", "me", "show", "get", "can", "could", "you",
        "please", "pls", "plz", "someone", "somebody", "anyone", "a", "an", "the", "to", "who", "which", "that", "is", "are", "was", "my", "our",
        "some", "good", "best", "top", "nice", "great", "help", "with", "and", "or", "of", "it", "this", "do", "does", "will", "would", "like",
        "only", "ones", "one", "just", "also", "service", "services", "provider", "providers", "professional", "professionals", "expert", "experts",
        "near", "nearby", "around", "here", "now", "today", "hi", "hello", "hey", "thanks", "thank", "instead", "there", "any", "more",
        "what", "about", "how", "else", "other", "yes", "no", "ok", "okay", "then", "too", "in", "at", "on", "from", "got", "has", "have", "be",
        "not", "isnt", "isn't", "working", "broken", "issue", "issues", "problem", "problems", "fix", "fixed", "fixing", "having",
    };

    public async Task<AssistantReplyDto> Handle(AskSearchAssistantCommand r, CancellationToken ct)
    {
        var message = Regex.Replace((r.Message ?? string.Empty).Trim(), @"\s+", " ");
        var previous = r.Context;

        // "Start over" forgets the conversation; an empty message just re-runs the given filters (a chip was removed).
        if (StartOver().IsMatch(message)) { previous = null; message = StartOver().Replace(message, " ").Trim(); }
        if (message.Length == 0 && previous is not null) return await AnswerAsync(previous, null, aiPending: false, aiUsed: false, r, ct);
        if (message.Length == 0) return await AnswerAsync(new AssistantFiltersDto(City: r.City, AreaId: r.AreaId), null, false, false, r, ct);

        // A question ("Give me 3 popular services in Nellore", "what do AC repairs cost?") is answered in words, from live data.
        if (Question().IsMatch(message)) return await AnswerQuestionAsync(message, previous, r, ct);

        var f = previous ?? new AssistantFiltersDto();
        var text = " " + message.ToLowerInvariant() + " ";

        // ---- Refinements (instant, rule-based) ----
        if (ClearFilters().IsMatch(text))
        {
            f = f with { MinRating = null, Availability = null, OpenNow = false, VerifiedOnly = false, HomeService = false, VideoConsultation = false,
                OnlineBooking = false, Sort = null };
            text = ClearFilters().Replace(text, " ");
        }
        text = Apply(OpenNow(), text, () => f = f with { OpenNow = true });
        text = Apply(Urgent(), text, () => f = f with { Availability = "now" });
        text = Apply(Verified(), text, () => f = f with { VerifiedOnly = true });
        text = Apply(HomeVisit(), text, () => f = f with { HomeService = true });
        text = Apply(Video(), text, () => f = f with { VideoConsultation = true });
        text = Apply(Booking(), text, () => f = f with { OnlineBooking = true });
        if (Stars().Match(text) is { Success: true } stars && decimal.TryParse(stars.Groups["n"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var min))
        {
            f = f with { MinRating = Math.Clamp(min, 1, 5) };
            text = Stars().Replace(text, " ");
        }
        text = Apply(TopRated(), text, () => f = f with { MinRating = f.MinRating ?? 4m, Sort = "rating" });
        text = Apply(Cheap(), text, () => f = f with { Sort = "price" });
        text = Apply(MostReviewed(), text, () => f = f with { Sort = "reviews" });
        text = Apply(Nearest(), text, () => f = f with { Sort = "distance" });

        // ---- Where: a place named in the request, else the conversation's, else the visitor's selected location ----
        var rest = Regex.Replace(text, @"\s+", " ").Trim();
        if (rest.Length > 200) rest = rest[..200]; // the parser's limit
        var parsed = rest.Length == 0 ? null : await sender.Send(new ParseSearchQuery(rest, f.City ?? r.City, ByMeaning: false), ct);
        if (parsed?.Place is { } place) f = f with { City = place.CitySlug, AreaId = place.AreaId };
        else if (previous is null) f = f with { City = r.City, AreaId = r.AreaId };
        var what = parsed?.PlaceText is not null ? parsed.Query : text;
        var turn = new Turn(message, previous, PlaceFound: parsed?.Place is not null);

        // ---- What: the service type ----
        var words = Words(what).Where(w => !Filler.Contains(w)).ToList();
        if (words.Count == 0)
        {
            // A greeting or small talk ("hello") on its own: ask what they need instead of listing every business.
            if (previous is null && f == new AssistantFiltersDto(City: f.City, AreaId: f.AreaId)) return await GreetAsync(f, ct);
            // A pure refinement ("only verified ones", "in Kukatpally instead"): keep what was being searched for.
            return await UnderstandAsync(turn, f, null, false, null, r, ct);
        }

        // Something new is being searched for: the service changes; where and how are kept.
        f = f with { Q = null, Category = null, Sub = null };
        var subs = await reference.ActiveSubCategoriesAsync(ct);
        var core = string.Join(' ', words);

        if (parsed is { SubCategorySlug: not null } or { CategorySlug: not null })
            return await UnderstandAsync(turn, f with { Sub = parsed.SubCategorySlug, Category = parsed.CategorySlug }, null, false, null, r, ct);
        // A service named outright ("cars", "plumbers") is taken as said.
        var byName = MatchByName(words, subs);
        if (byName is { Full: true })
            return await UnderstandAsync(turn, f with { Sub = byName.Sub?.Slug, Category = byName.CategorySlug }, byName.Options, false, null, r, ct);

        // Fast local AI (embeddings, a few hundred milliseconds): what the request means, even when it shares no words with the
        // catalogue ("water dripping from my ceiling" gives Plumbers).
        var semantic = await semanticCatalog.MatchAsync(what, 8, ct);
        string? Name(string slug) => subs.FirstOrDefault(s => s.Slug == slug)?.Name;
        // The service types the chat model may choose from when the rules are unsure: the closest in meaning (or else the whole catalogue),
        // plus the rules' own guess.
        var choices = semantic is { Top.Count: > 0 }
            ? semantic.Top.Select(m => subs.FirstOrDefault(s => s.Slug == m.SubCategorySlug)).OfType<SubCategoryRef>().ToList()
            : subs.ToList();
        if (byName?.Sub is { } guess && !choices.Contains(guess)) choices.Add(guess);
        if (semantic is { Confident: true })
        {
            // Close runners-up are offered as alternatives ("Clinics" for a child's fever also offers Doctors).
            var runnersUp = semantic.Top.Skip(1).Where(m => semantic.Top[0].Score - m.Score <= 0.04).Select(m => Name(m.SubCategorySlug)).OfType<string>().ToList();
            return await UnderstandAsync(turn, f with { Sub = semantic.Top[0].SubCategorySlug }, runnersUp, aiUsed: true, null, r, ct);
        }
        // A partial name match ("dog haircut" shares "dog" with Dog Trainers) only counts when the AI isn't sure.
        if (byName is not null)
            return await UnderstandAsync(turn, f with { Sub = byName.Sub?.Slug, Category = byName.CategorySlug }, byName.Options, false, choices, r, ct);

        if (await MatchByServicesAsync(words, ct) is { } byService)
            return await UnderstandAsync(turn, f with { Sub = byService }, null, false, choices, r, ct);

        // Unclear: the chat model decides (in the background) among the closest service types; meanwhile they are offered as suggestions.
        var offered = semantic is { Plausible: true } ? choices.Take(3).Select(s => s.Name).ToList() : null;
        return await UnderstandAsync(turn, f with { Q = core }, offered, false, choices, r, ct);
    }

    // ===================== Answer =====================

    private async Task<AssistantReplyDto> GreetAsync(AssistantFiltersDto f, CancellationToken ct)
    {
        var starters = await sender.Send(new GetAssistantStartersQuery(f.City), ct);
        return new AssistantReplyDto("Hi! What do you need help with? Name a service, or describe the problem in your own words.",
            null, f, [], [], 0, starters.Prompts.Take(3).ToList(), [], false, false);
    }

    /// <param name="PlaceFound">The rules already found a place in the message.</param>
    private sealed record Turn(string Message, AssistantFiltersDto? Previous, bool PlaceFound);

    /// <summary>
    /// The chat model reads the message in natural language: the service (from <paramref name="choices"/>, when the rules weren't sure of
    /// it) and what else the visitor asked for ("not too expensive", "someone who can come over"). Its reading is added to the rules'
    /// filters, then the reply is written by the model from the results. While it reads, the rules' answer is shown and the client polls.
    /// </summary>
    /// <param name="choices">Service types the model may pick from; null when the rules matched the service with confidence.</param>
    private async Task<AssistantReplyDto> UnderstandAsync(Turn turn, AssistantFiltersDto f, IReadOnlyList<string>? alternatives, bool aiUsed,
        IReadOnlyList<SubCategoryRef>? choices, AskSearchAssistantCommand r, CancellationToken ct)
    {
        if (!ai.IsEnabled) return await AnswerAsync(f, alternatives, false, aiUsed, r, ct);
        // A short, clear request (a tapped suggestion such as "Show Plumbers" or "Only verified") is fully read by the rules.
        if (choices is null && Words(turn.Message).Count() <= 4) return await AnswerAsync(f, alternatives, false, aiUsed, r, ct, turn);

        var services = (choices ?? []).Select(s => (s.Slug, $"{s.Name} ({s.CategoryName})")).ToList();
        var current = turn.Previous is null ? null : await DescribeAsync(turn.Previous, ct);
        var key = $"read|{string.Join(',', services.Select(s => s.Slug))}|{current}|{Normalize(turn.Message)}";
        var reading = ai.GetReading(key);
        if (reading is null)
        {
            ai.RequestReading(key, turn.Message, current, services);
            if (ai.IsReadingPending(key)) return await AnswerAsync(f, alternatives, aiPending: true, aiUsed, r, ct);
            // The model is unavailable: the rules' reading stands.
            return await AnswerAsync(f, alternatives, false, aiUsed, r, ct, turn);
        }

        if (choices is not null && reading.ServiceSlug is { } slug && slug != f.Sub)
        {
            // The model's reading of the service wins over a guess from shared words; the guess is offered instead.
            var guess = f.Sub is null ? null : choices.FirstOrDefault(c => c.Slug == f.Sub)?.Name;
            alternatives = guess is null ? alternatives?.Where(a => a != choices.FirstOrDefault(c => c.Slug == slug)?.Name).ToList() : [guess];
            f = f with { Sub = slug, Category = null, Q = null };
            aiUsed = true;
        }
        if (!turn.PlaceFound && reading.Place is { } placeText)
        {
            var parsed = await sender.Send(new ParseSearchQuery(placeText, f.City ?? r.City, ByMeaning: false), ct);
            if (parsed.Place is { } place) f = f with { City = place.CitySlug, AreaId = place.AreaId };
        }
        // Filters the visitor asked for in their own words are added to the ones the rules found.
        f = f with
        {
            VerifiedOnly = f.VerifiedOnly || reading.Verified, HomeService = f.HomeService || reading.HomeVisit, OpenNow = f.OpenNow || reading.OpenNow,
            VideoConsultation = f.VideoConsultation || reading.Video, OnlineBooking = f.OnlineBooking || reading.Booking,
            Availability = f.Availability ?? (reading.Urgent ? "now" : null), MinRating = f.MinRating ?? reading.MinRating, Sort = f.Sort ?? reading.Sort,
        };
        return await AnswerAsync(f, alternatives, false, aiUsed, r, ct, turn);
    }

    /// <summary>The search so far in plain words, e.g. "AC Repair in hyderabad, verified, cheapest first", for the model to refine.</summary>
    private async Task<string> DescribeAsync(AssistantFiltersDto p, CancellationToken ct)
    {
        var subs = await reference.ActiveSubCategoriesAsync(ct);
        var what = p.Sub is { } s ? subs.FirstOrDefault(x => x.Slug == s)?.Name
            : p.Category is { } c ? subs.FirstOrDefault(x => x.CategorySlug == c)?.CategoryName : p.Q;
        var parts = new List<string?> { what ?? "businesses", p.City is null ? null : $"in {p.City}" };
        parts.AddRange(Traits(p));
        if (p.Sort is { } sort) parts.Add($"sorted by {sort}");
        return string.Join(", ", parts.OfType<string>());
    }

    private static string Normalize(string message) => Regex.Replace(message.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    /// <param name="turn">Set when the model should write the reply in its own words from the results (the composed answer shows meanwhile).</param>
    private async Task<AssistantReplyDto> AnswerAsync(AssistantFiltersDto f, IReadOnlyList<string>? alternatives, bool aiPending, bool aiUsed,
        AskSearchAssistantCommand r, CancellationToken ct, Turn? turn = null)
    {
        // Nearest first needs a location: the visitor's coordinates, the selected area or the city centre.
        if (f.Sort == "distance" && f.AreaId is null && f.City is null && r.Lat is null) f = f with { Sort = null };

        var requested = f;
        var relaxed = new List<string>();
        var result = await SearchAsync(f, r, ct);
        // Nothing matches everything: drop the narrowest filters one at a time, and say so.
        foreach (var (label, loosen) in Relaxations(f))
        {
            if (result.Pagination.TotalCount > 0) break;
            var next = loosen(f);
            if (next == f) continue;
            f = next;
            relaxed.Add(label);
            result = await SearchAsync(f, r, ct);
        }

        // Still nothing: the city has none at all. Keep the visitor's filters (dropping them didn't help) and say where they are listed.
        Elsewhere? elsewhere = null;
        if (result.Pagination.TotalCount == 0)
        {
            var loosest = f;
            (f, relaxed) = (requested, []);
            if (f.City is not null) elsewhere = await FindElsewhereAsync(loosest, r, ct);
        }

        var subs = await reference.ActiveSubCategoriesAsync(ct);
        var sub = f.Sub is null ? null : subs.FirstOrDefault(s => s.Slug == f.Sub);
        var categoryName = f.Category is null ? null : subs.FirstOrDefault(s => s.CategorySlug == f.Category)?.CategoryName;
        var understood = sub?.Name ?? categoryName;
        var applied = result.Applied;
        var where = applied.AreaName is not null && applied.CityName is not null ? $"{applied.AreaName}, {applied.CityName}" : applied.CityName;

        var suggestions = Suggestions(f, alternatives, result.Pagination.TotalCount);
        if (elsewhere is not null && (elsewhere.Related ?? understood ?? f.Q) is { } service)
            suggestions.InsertRange(0, elsewhere.Cities.Select(c => $"Show {service} in {c}"));

        var message = Compose(f, understood, applied, result.Pagination.TotalCount, relaxed, aiPending, aiUsed, elsewhere);
        if (turn is not null && ai.IsEnabled)
        {
            var facts = ReplyFacts(f, understood, where, result, relaxed, elsewhere);
            var key = $"reply|{Normalize(turn.Message)}|{f}|{result.Pagination.TotalCount}|{string.Join(',', result.Items.Select(b => b.Id))}";
            if (ai.GetAnswer(key) is { } reply) (message, aiUsed) = (reply.Text, true);
            else
            {
                ai.RequestReply(key, turn.Message, facts);
                aiPending = ai.IsAnswerPending(key);
            }
        }

        return new AssistantReplyDto(message, understood, f, Chips(f, understood, where), result.Items, result.Pagination.TotalCount,
            suggestions.Take(5).ToList(), relaxed, aiPending, aiUsed);
    }

    /// <summary>The search and its results in plain words: all the model may use when it writes the reply.</summary>
    private static string ReplyFacts(AssistantFiltersDto f, string? understood, string? where, SearchResultDto result, List<string> relaxed,
        Elsewhere? elsewhere)
    {
        var inr = CultureInfo.GetCultureInfo("en-IN");
        string Rupees(decimal? p) => p is > 0 ? "₹" + p.Value.ToString("N0", inr) : "price on request";
        var facts = new StringBuilder();
        facts.AppendLine($"Searched for: {understood ?? f.Q ?? "businesses"}{(where is null ? "" : $" in {where}")}");
        if (Traits(f) is { Count: > 0 } applied) facts.AppendLine($"Filters applied: {string.Join(", ", applied)}");
        if (f.Sort is { } sort) facts.AppendLine($"Sorted by: {sort switch { "price" => "lowest price first", "rating" => "highest rated first", "reviews" => "most reviewed first", _ => "nearest first" }}");
        if (relaxed.Count > 0) facts.AppendLine($"Nothing matched every filter, so these were dropped: {string.Join(", ", relaxed)}");
        var total = result.Pagination.TotalCount;
        facts.AppendLine($"Businesses on Calling Bell matching: {total}");
        if (result.Applied is { AreaName: { } area, AreaMatches: { } inArea }) facts.AppendLine($"Of these, {inArea} are in {area} itself");
        foreach (var c in result.Items)
        {
            var traits = new[] { c.IsVerified ? "verified" : null, c.OffersHomeService ? "home visits" : null, c.AcceptsOnlineBooking ? "online booking" : null,
                c.OffersVideoConsultation ? "video consultation" : null, c.IsOpenNow ? "open now" : null }.OfType<string>();
            facts.AppendLine($"- {c.Name} ({c.SubCategoryName ?? c.CategoryName}, {c.Area ?? c.City}): " +
                $"{(c.ReviewCount > 0 ? $"rated {c.AverageRating:0.0} from {c.ReviewCount} reviews" : "new, no reviews yet")}, from {Rupees(c.StartingPrice)}" +
                (traits.Any() ? $"; {string.Join(", ", traits)}" : ""));
        }
        if (total == 0 && elsewhere is not null)
        {
            if (!elsewhere.CityHasListings) facts.AppendLine("No businesses of any kind are listed in this city on Calling Bell yet.");
            if (elsewhere.Cities.Count > 0)
                facts.AppendLine($"{elsewhere.Related ?? understood ?? "Matching businesses"} are listed on Calling Bell in: {JoinAnd(elsewhere.Cities)}");
            if (where is not null) facts.AppendLine("Real places nearby from the map (not on Calling Bell) are shown below the reply.");
        }
        return facts.ToString();
    }

    /// <param name="CityHasListings">Whether the searched city has any listed businesses at all.</param>
    /// <param name="Cities">The nearest cities that do list matches.</param>
    /// <param name="Related">Set when the service itself is listed nowhere and <paramref name="Cities"/> list its wider category instead.</param>
    private sealed record Elsewhere(bool CityHasListings, IReadOnlyList<string> Cities, string? Related);

    private async Task<Elsewhere> FindElsewhereAsync(AssistantFiltersDto loosest, AskSearchAssistantCommand r, CancellationToken ct)
    {
        var cityHasListings = await uow.Repository<Business>().QueryNoTracking().Listed().AnyAsync(b => b.CityRef != null && b.CityRef.Slug == loosest.City, ct);
        var anywhere = await SearchAsync(loosest with { City = null, AreaId = null, Sort = "rating" }, r, ct, pageSize: 100);
        // The nearest cities that list matches (by straight-line distance), so Nellore suggests Chennai before Ahmedabad.
        var all = await reference.ActiveCitiesAsync(ct);
        var from = all.FirstOrDefault(c => c.Slug == loosest.City);
        double Distance(string name) =>
            all.FirstOrDefault(c => c.Name == name) is { Latitude: { } lat, Longitude: { } lng } && from is { Latitude: { } fLat, Longitude: { } fLng }
                ? Math.Pow(lat - fLat, 2) + Math.Pow((lng - fLng) * Math.Cos(fLat * Math.PI / 180), 2)
                : double.MaxValue;
        List<string> Nearest(SearchResultDto found) => found.Items.Select(b => b.City).Distinct().OrderBy(Distance).ThenBy(n => n).Take(3).ToList();

        var cities = Nearest(anywhere);
        // Not listed anywhere yet (e.g. Car Repair): look for the wider category instead (Automotive).
        if (cities.Count == 0 && loosest.Sub is not null
            && (await reference.ActiveSubCategoriesAsync(ct)).FirstOrDefault(s => s.Slug == loosest.Sub) is { } sub)
        {
            var related = await SearchAsync(loosest with { Sub = null, Category = sub.CategorySlug, City = null, AreaId = null, Sort = "rating" }, r, ct, pageSize: 100);
            if (Nearest(related) is { Count: > 0 } relatedCities) return new Elsewhere(cityHasListings, relatedCities, sub.CategoryName);
        }
        return new Elsewhere(cityHasListings, cities, null);
    }

    private Task<SearchResultDto> SearchAsync(AssistantFiltersDto f, AskSearchAssistantCommand r, CancellationToken ct, int pageSize = ResultCount) =>
        sender.Send(new SearchBusinessesQuery
        {
            Q = f.Q, Category = f.Category, Sub = f.Sub, City = f.City, AreaId = f.AreaId, MinRating = f.MinRating, Availability = f.Availability,
            OpenNow = f.OpenNow, VerifiedOnly = f.VerifiedOnly, HomeService = f.HomeService, VideoConsultation = f.VideoConsultation,
            OnlineBooking = f.OnlineBooking, Sort = f.Sort ?? "relevance", Lat = r.Lat, Lng = r.Lng, Page = 1, PageSize = pageSize,
        }, ct);

    private static IEnumerable<(string Label, Func<AssistantFiltersDto, AssistantFiltersDto> Loosen)> Relaxations(AssistantFiltersDto f)
    {
        yield return ($"{f.MinRating:0.#}★ & up", x => x with { MinRating = null });
        yield return ("open now", x => x with { OpenNow = false });
        yield return ("available now", x => x with { Availability = null });
        yield return ("online booking", x => x with { OnlineBooking = false });
        yield return ("verified only", x => x with { VerifiedOnly = false });
    }

    private static string Compose(AssistantFiltersDto f, string? understood, AppliedFiltersDto applied, int total, List<string> relaxed,
        bool aiPending, bool aiUsed, Elsewhere? elsewhere)
    {
        var what = understood ?? (f.Q is null ? "businesses" : $"businesses matching \"{f.Q}\"");
        if (f.Sub is null && f.Category is not null && understood is not null) what = $"{understood} services"; // a whole category
        var traits = Traits(f);
        var place = applied.CityName is null ? "" : $" in {applied.CityName}";
        var count = total == 1 ? "1 match" : $"{total:N0} matches";

        string answer;
        if (total == 0)
        {
            var understoodSomething = understood is not null || f.Q is not null;
            answer = elsewhere is { CityHasListings: false } && applied.CityName is not null
                ? $"No businesses are listed on Calling Bell in {applied.CityName} yet."
                : elsewhere is { Cities.Count: 0, Related: null } && understood is not null
                    ? $"There are no listings for {what} on Calling Bell yet."
                    : $"There are no listings for {what}{place} on Calling Bell yet.";
            if (elsewhere is { Cities.Count: > 0 })
                answer += elsewhere.Related is { } related
                    ? $" Related {related} services are listed in {JoinAnd(elsewhere.Cities)}."
                    : $" You'll find {what} in {JoinAnd(elsewhere.Cities)}.";
            // The panel shows real places nearby (Google Maps / OpenStreetMap) below this answer.
            if (applied.CityName is not null && understoodSomething) answer += $" Here are places near {applied.AreaName ?? applied.CityName} from the map:";
            else if (!understoodSomething || understood is null) answer += " Try describing the job differently.";
        }
        else
        {
            answer = $"I found {count} for {what}{place}{(traits.Count > 0 ? $" ({string.Join(", ", traits)})" : "")}.";
            // The selected area's businesses are listed first; the rest of the city follows.
            if (applied is { AreaName: { } area, AreaMatches: { } inArea })
                answer += inArea == 0 ? $" None are in {area} itself, so the nearest ones come first."
                    : inArea < total ? $" {inArea:N0} {(inArea == 1 ? "is" : "are")} in {area} and {(inArea == 1 ? "is" : "are")} listed first." : "";
        }

        if (relaxed.Count > 0 && total > 0)
            answer += $" Nothing matched every filter, so I dropped {string.Join(" and ", relaxed)}.";
        if (total > 0) answer += f.Sort switch
        {
            "rating" => " Highest rated first.",
            "price" => " Lowest starting price first.",
            "reviews" => " Most reviewed first.",
            "distance" => " Nearest first.",
            _ => "",
        };
        // While the model works the panel says so; the answer itself stays factual.
        if (aiUsed && understood is not null) answer = $"Sounds like you need {understood}. " + answer;
        else if (understood is null && f.Q is not null && total > 0) answer += " Tell me the type of service for more precise results.";
        return answer;
    }

    private static string JoinAnd(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Concat(items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    private static List<string> Traits(AssistantFiltersDto f)
    {
        var t = new List<string>();
        if (f.VerifiedOnly) t.Add("verified");
        if (f.Availability == "now") t.Add("available now");
        if (f.OpenNow) t.Add("open now");
        if (f.HomeService) t.Add("home visits");
        if (f.VideoConsultation) t.Add("video consultation");
        if (f.OnlineBooking) t.Add("online booking");
        if (f.MinRating is { } m) t.Add($"rated {m:0.#}★ & up");
        return t;
    }

    private static List<AssistantChipDto> Chips(AssistantFiltersDto f, string? understood, string? where)
    {
        var chips = new List<AssistantChipDto>();
        if (understood is not null) chips.Add(new(f.Sub is not null ? "sub" : "category", understood));
        if (f.Q is not null) chips.Add(new("q", $"\"{f.Q}\""));
        if (where is not null) chips.Add(new(f.AreaId is not null ? "areaId" : "city", where));
        if (f.VerifiedOnly) chips.Add(new("verifiedOnly", "Verified"));
        if (f.Availability == "now") chips.Add(new("availability", "Available now"));
        if (f.OpenNow) chips.Add(new("openNow", "Open now"));
        if (f.HomeService) chips.Add(new("homeService", "Home visit"));
        if (f.VideoConsultation) chips.Add(new("videoConsultation", "Video consultation"));
        if (f.OnlineBooking) chips.Add(new("onlineBooking", "Online booking"));
        if (f.MinRating is { } m) chips.Add(new("minRating", $"{m:0.#}★ & up"));
        if (f.Sort is "rating" or "price" or "reviews" or "distance")
            chips.Add(new("sort", f.Sort switch { "rating" => "Top rated", "price" => "Cheapest first", "reviews" => "Most reviewed", _ => "Nearest first" }));
        return chips;
    }

    private static List<string> Suggestions(AssistantFiltersDto f, IReadOnlyList<string>? alternatives, int total)
    {
        var s = new List<string>();
        // "instead" only once something was matched; before that the alternatives are the candidates themselves.
        var matched = f.Sub is not null || f.Category is not null;
        foreach (var alt in alternatives ?? []) s.Add(matched ? $"Show {alt} instead" : $"Show {alt}");
        if (total == 0)
        {
            s.Add("Start over");
            return s;
        }
        if (!f.VerifiedOnly) s.Add("Only verified");
        if (f.Availability != "now") s.Add("Available right now");
        if (!f.HomeService) s.Add("Can visit my home");
        if (f.Sort != "rating") s.Add("Top rated");
        if (f.Sort != "price") s.Add("Cheapest first");
        if (!f.VideoConsultation) s.Add("Video consultation");
        return s.Take(5).ToList();
    }

    // ===================== Questions =====================

    /// <summary>Words that say how to answer rather than what about ("give me 3 popular ...", "what do ... cost").</summary>
    private static readonly HashSet<string> QuestionFiller = new(StringComparer.OrdinalIgnoreCase)
    {
        "give", "list", "tell", "name", "suggest", "recommend", "compare", "explain", "what", "what's", "whats", "which", "where", "when", "why",
        "popular", "trending", "demand", "booked", "most", "much", "many", "cost", "costs", "price", "prices", "charge", "charges", "rate", "rates",
        "should", "available", "categories", "category", "businesses", "business", "kind", "kinds", "type", "types", "list", "find",
    };

    private sealed record ServiceFact(string Name, string Slug, string Category, int Listings, int Bookings, int Enquiries, decimal Rating, decimal? From);

    /// <summary>
    /// Answers a question in words. The figures (popular services by recent bookings and enquiries, most booked services, top rated
    /// businesses and their prices) are read live from the database for the visitor's city; the local AI then phrases the answer from
    /// those facts only. Until the AI has answered (or when it is off), the reply is composed from the same facts.
    /// </summary>
    private async Task<AssistantReplyDto> AnswerQuestionAsync(string message, AssistantFiltersDto? previous, AskSearchAssistantCommand r, CancellationToken ct)
    {
        var parsed = await sender.Send(new ParseSearchQuery(message.Length > 200 ? message[..200] : message, previous?.City ?? r.City, ByMeaning: false), ct);
        var (citySlug, areaId) = parsed.Place is { } place ? (place.CitySlug, place.AreaId)
            : previous is not null ? (previous.City, previous.AreaId) : (r.City, r.AreaId);

        // What the question is about: a service it names, "them" (the previous answer's), or nothing in particular.
        var subs = await reference.ActiveSubCategoriesAsync(ct);
        string? subSlug = parsed.SubCategorySlug, categorySlug = parsed.CategorySlug;
        if (subSlug is null && categorySlug is null)
        {
            var words = Words(parsed.PlaceText is not null ? parsed.Query : message)
                .Where(w => !Filler.Contains(w) && !QuestionFiller.Contains(w) && !w.All(char.IsDigit)).ToList();
            if (words.Count > 0 && MatchByName(words, subs) is { Full: true } byName) (subSlug, categorySlug) = (byName.Sub?.Slug, byName.CategorySlug);
            else if (previous is not null && FollowUp().IsMatch(message)) (subSlug, categorySlug) = (previous.Sub, previous.Category);
        }
        var sub = subSlug is null ? null : subs.FirstOrDefault(s => s.Slug == subSlug);
        var focus = sub?.Name ?? (categorySlug is null ? null : subs.FirstOrDefault(s => s.CategorySlug == categorySlug)?.CategoryName);

        // Where: the city when it has listings, otherwise the whole country (and the answer says so).
        var city = citySlug is null ? null : (await reference.ActiveCitiesAsync(ct)).FirstOrDefault(c => c.Slug == citySlug);
        var all = uow.Repository<Business>().QueryNoTracking().Listed();
        var inCity = city is null ? all : all.Where(b => b.CityRef != null && b.CityRef.Slug == city.Slug);
        var cityCount = city is null ? 0 : await inCity.CountAsync(ct);
        var local = city is not null && cityCount > 0;
        var scope = local ? inCity : all;
        var placeText = local ? $"in {city!.Name}" : "across India";

        // Popularity of each service: bookings and enquiries in the last six months, then the number of businesses offering it.
        var since = DateTimeOffset.UtcNow.AddDays(-180);
        var rows = await scope.Where(b => b.SubCategory != null).Select(b => new
        {
            Sub = b.SubCategory!.Name, b.SubCategory.Slug, Category = b.Category.Name, b.AverageRating, b.ReviewCount,
            Bookings = b.Bookings.Count(k => k.CreatedOn >= since), Enquiries = b.Enquiries.Count(e => e.CreatedOn >= since),
            From = b.Services.Where(s => s.IsActive && s.Price > 0).Min(s => (decimal?)s.Price),
        }).ToListAsync(ct);
        var services = rows.GroupBy(x => (x.Sub, x.Slug, x.Category))
            .Select(g => new ServiceFact(g.Key.Sub, g.Key.Slug, g.Key.Category, g.Count(), g.Sum(x => x.Bookings), g.Sum(x => x.Enquiries),
                g.Where(x => x.ReviewCount > 0).Select(x => x.AverageRating).DefaultIfEmpty().Average(), g.Min(x => x.From)))
            .OrderByDescending(s => s.Bookings + s.Enquiries).ThenByDescending(s => s.Listings).ThenByDescending(s => s.Rating)
            .Take(10).ToList();

        // The individual services booked most (e.g. "AC gas refill"), within the asked-about service when there is one.
        var subId = sub?.Id;
        var scopeIds = scope.Select(b => b.Id);
        var booked = await uow.Repository<Booking>().QueryNoTracking()
            .Where(k => k.CreatedOn >= since && scopeIds.Contains(k.BusinessId))
            .Where(k => subId == null || k.Business.SubCategoryId == subId)
            .Where(k => subId != null || categorySlug == null || k.Business.Category.Slug == categorySlug)
            .GroupBy(k => k.Service.Name).Select(g => new { Name = g.Key, Count = g.Count(), From = g.Min(k => k.Amount) })
            .OrderByDescending(g => g.Count).Take(6).ToListAsync(ct);

        var top = await SearchAsync(new AssistantFiltersDto(Category: sub is null ? categorySlug : null, Sub: subSlug,
            City: local ? citySlug : null, AreaId: local ? areaId : null, Sort: "rating"), r, ct);

        // ---- The facts the AI may use ----
        var inr = CultureInfo.GetCultureInfo("en-IN");
        string Rupees(decimal? p) => p is > 0 ? "₹" + p.Value.ToString("N0", inr) : "price on request";
        var facts = new StringBuilder();
        facts.AppendLine(CultureInfo.InvariantCulture, $"Today: {DateTime.UtcNow.AddHours(5.5):dddd d MMMM yyyy}");
        if (city is not null && !local) facts.AppendLine($"No businesses are listed on Calling Bell in {city.Name} yet; the figures below are for all of India.");
        facts.AppendLine(local ? $"Location: {city!.Name}, {city.State} ({cityCount:N0} businesses listed)"
            : $"Location: all of India ({await all.CountAsync(ct):N0} businesses listed)");
        if (top.Applied.AreaName is { } area) facts.AppendLine($"Customer's area: {area}");
        if (focus is not null) facts.AppendLine($"The customer is asking about: {focus}");
        if (services.Count == 0) facts.AppendLine("No services are listed yet.");
        else
        {
            facts.AppendLine("Services by popularity (bookings and enquiries in the last 6 months):");
            for (var i = 0; i < services.Count; i++)
            {
                var s = services[i];
                facts.AppendLine($"{i + 1}. {s.Name} ({s.Category}): {s.Bookings} bookings, {s.Enquiries} enquiries, {s.Listings} businesses, " +
                    $"{(s.Rating > 0 ? $"average rating {s.Rating:0.0}" : "no ratings yet")}, from {Rupees(s.From)}");
            }
        }
        if (booked.Count > 0)
        {
            facts.AppendLine(focus is null ? "Most booked individual services:" : $"Most booked {focus} services:");
            foreach (var b in booked) facts.AppendLine($"- {b.Name}: {b.Count} bookings, from {Rupees(b.From)}");
        }
        if (top.Items.Count > 0)
        {
            facts.AppendLine(focus is null ? "Top rated businesses:" : $"Top rated {focus}:");
            foreach (var c in top.Items)
            {
                var traits = new[] { c.IsVerified ? "verified" : null, c.OffersHomeService ? "home visits" : null, c.AcceptsOnlineBooking ? "online booking" : null,
                    c.OffersVideoConsultation ? "video consultation" : null, c.IsOpenNow ? "open now" : null }.OfType<string>();
                facts.AppendLine($"- {c.Name} ({c.SubCategoryName ?? c.CategoryName}, {c.Area ?? c.City}): " +
                    $"{(c.ReviewCount > 0 ? $"rated {c.AverageRating:0.0} from {c.ReviewCount} reviews" : "new, no reviews yet")}, from {Rupees(c.StartingPrice)}" +
                    (traits.Any() ? $"; {string.Join(", ", traits)}" : ""));
            }
        }

        // ---- The AI's answer, or one composed from the facts while it works ----
        var key = $"answer|{(local ? citySlug : "")}|{(local ? areaId : null)}|{subSlug ?? categorySlug}|" +
            Regex.Replace(message.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        var answer = ai.IsEnabled ? ai.GetAnswer(key) : null;
        var pending = false;
        if (ai.IsEnabled && answer is null)
        {
            ai.RequestAnswer(key, message, facts.ToString());
            pending = ai.IsAnswerPending(key);
        }

        var n = HowMany().Match(message) is { Success: true } m ? Math.Clamp(int.Parse(m.Value, CultureInfo.InvariantCulture), 1, 10) : 3;
        var composed = focus is not null && top.Items.Count > 0
            ? $"Top rated {focus} {placeText}: " + JoinAnd(top.Items.Take(n).Select(c => c.ReviewCount > 0 ? $"{c.Name} ({c.AverageRating:0.0}★)" : c.Name).ToList()) + "."
            : services.Count > 0
                ? $"The most popular services {placeText} on Calling Bell right now are " + JoinAnd(services.Take(n).Select(s => s.Name).ToList()) + "."
                : "There aren't enough listings yet to answer that. Try searching for a service instead.";
        if (city is not null && !local) composed = $"No businesses are listed in {city.Name} yet. " + composed;
        if (pending) composed += " Let me put together a fuller answer…";

        var filters = new AssistantFiltersDto(Category: sub is null ? categorySlug : null, Sub: subSlug, City: local ? citySlug : null,
            AreaId: local ? areaId : null, Sort: focus is null ? null : "rating");
        var where = top.Applied.AreaName is not null && top.Applied.CityName is not null ? $"{top.Applied.AreaName}, {top.Applied.CityName}" : top.Applied.CityName;
        var suggestions = services.Where(s => s.Slug != subSlug).Take(3).Select(s => local ? $"Show {s.Name} in {city!.Name}" : $"Show {s.Name}").ToList();
        if (focus is not null) suggestions.AddRange(["Cheapest first", "Only verified"]);

        return new AssistantReplyDto(answer?.Text ?? composed, focus, filters, Chips(filters, focus, where),
            focus is null ? [] : top.Items, focus is null ? 0 : top.Pagination.TotalCount, suggestions.Take(5).ToList(), [], pending, answer is not null);
    }

    // ===================== Matching the service type =====================

    /// <summary>
    /// The sub-category whose name (or category's name) shares the most words with the request, e.g. "ac leaking" gives AC Repair.
    /// A sub-category name word counts double, so "home cleaning" prefers Home Cleaning over another Home Services entry.
    /// </summary>
    private static NameMatch? MatchByName(List<string> words, IReadOnlyList<SubCategoryRef> subs)
    {
        var stemmed = words.Select(Stem).ToList();
        // A sub-category's full name inside the request wins outright ("show home cleaning instead"), the longest first.
        var phrase = " " + string.Join(' ', stemmed) + " ";
        if (subs.Where(s => phrase.Contains(" " + string.Join(' ', Words(s.Name).Select(Stem)) + " "))
                .MaxBy(s => s.Name.Length) is { } exact) return new NameMatch(exact, null, [], Full: true);

        // Otherwise shared words, ignoring generic ones such as "repair" or "classes" that appear in many names.
        var nameWords = subs.Select(s => Words(s.Name).Select(Stem).Distinct().ToList()).ToList();
        var frequency = nameWords.SelectMany(w => w).GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var stems = stemmed.Where(w => w.Length >= 2 && frequency.GetValueOrDefault(w) <= 3).ToHashSet();
        if (stems.Count == 0) return null;
        var ranked = subs
            .Select((s, i) => new { Sub = s, Words = nameWords[i].Concat(Words(s.CategoryName).Select(Stem)).ToHashSet(),
                Score = nameWords[i].Count(stems.Contains) * 2 + Words(s.CategoryName).Select(Stem).Distinct().Count(stems.Contains) })
            .Where(x => x.Score >= 2)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Sub.Name.Length)
            .ToList();
        if (ranked.Count == 0) return null;
        var best = ranked[0];

        // One shared word in a longer description is a guess ("water dripping from the ceiling" is not RO Water Purifier):
        // the match must cover at least half of the request's words, otherwise the services and the AI decide.
        var content = stemmed.Where(w => w.Length >= 2).Distinct().ToList();
        var covered = content.Count(best.Words.Contains);
        if (covered * 2 < content.Count) return null;
        var full = covered == content.Count;

        // A broad word fits several services equally ("cars": Car Repair, Car Wash & Detailing). Within one category, search the whole
        // category and offer the specific services; across categories, take the closest and offer the others.
        var tied = ranked.TakeWhile(x => x.Score == best.Score).Select(x => x.Sub).ToList();
        var options = tied.Skip(1).Select(s => s.Name).ToList();
        return tied.Count > 1 && tied.All(s => s.CategorySlug == best.Sub.CategorySlug)
            ? new NameMatch(null, best.Sub.CategorySlug, tied.Select(s => s.Name).ToList(), full)
            : new NameMatch(best.Sub, null, options, full);
    }

    /// <summary>A sub-category, or a whole category when the request fits several of its services equally, plus those services.</summary>
    /// <param name="Full">Every word of the request is in the name ("cars", "plumbers"); a partial match ("dog haircut" for Dog Trainers) yields to a confident AI match.</param>
    private sealed record NameMatch(SubCategoryRef? Sub, string? CategorySlug, IReadOnlyList<string> Options, bool Full);

    /// <summary>
    /// The sub-category of the businesses whose listed services mention the request's words most, e.g. "tap leakage" finds plumbers
    /// offering "Tap & leakage repair".
    /// </summary>
    private async Task<string?> MatchByServicesAsync(List<string> words, CancellationToken ct)
    {
        // Each word has one vote, shared by the sub-categories in proportion to its hits: a specific word ("geyser") points to one
        // sub-category, while a generic one ("repair") spreads thinly across many.
        var votes = new Dictionary<string, double>();
        var considered = words.Select(Stem).Where(w => w.Length >= 3).Distinct().Take(4).ToList();
        foreach (var word in considered)
        {
            var hits = await uow.Repository<Business>().QueryNoTracking().Listed().Where(b => b.SubCategory != null)
                .SelectMany(b => b.Services.Where(s => s.IsActive && s.Name.Contains(word)).Select(_ => b.SubCategory!.Slug))
                .GroupBy(slug => slug).Select(g => new { Slug = g.Key, Count = g.Count() }).ToListAsync(ct);
            var total = hits.Sum(h => h.Count);
            foreach (var h in hits) votes[h.Slug] = votes.GetValueOrDefault(h.Slug) + (double)h.Count / total;
        }
        // The winner needs at least half of all the votes, so one shared word in a longer description is not enough.
        return votes.Count == 0 ? null : votes.MaxBy(v => v.Value) is var best && best.Value >= 0.5 * considered.Count ? best.Key : null;
    }

    private static IEnumerable<string> Words(string text) =>
        Regex.Split(text.ToLowerInvariant().Replace("&", " "), @"[^\p{L}\p{N}']+").Where(w => w.Length > 0);

    /// <summary>Simple plurals: "electricians" and "electrician" compare equal.</summary>
    private static string Stem(string w) =>
        w.Length > 4 && w.EndsWith("ies") ? w[..^3] + "y" : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w;

    private static string Apply(Regex pattern, string text, Action set)
    {
        if (!pattern.IsMatch(text)) return text;
        set();
        return pattern.Replace(text, " ");
    }

    // ===================== Phrases =====================
    // Each pattern is matched against " lower-case text " and removed once applied, so it doesn't count as part of the service.

    [GeneratedRegex(@"\b(start over|new search|reset|forget (that|it))\b", RegexOptions.IgnoreCase)]
    private static partial Regex StartOver();

    /// <summary>A question to answer in words rather than a request to search; the suggestion chips ("Show …", "Only verified") never match.</summary>
    [GeneratedRegex(@"^\s*(what|what's|whats|which|who|how|why|where|when|tell me|give me|list|suggest|recommend|compare|explain|is there|are there|do you|does|should i)\b|\?\s*$|\b(popular|trending|in[- ]demand|most booked)\s+(services|categories|businesses)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Question();

    /// <summary>A question about the previous answer's businesses ("which of them is cheapest?").</summary>
    [GeneratedRegex(@"\b(them|these|those|they)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FollowUp();

    /// <summary>How many items a question asks for ("give me 3 ...").</summary>
    [GeneratedRegex(@"\b([1-9]|10)\b")]
    private static partial Regex HowMany();

    [GeneratedRegex(@"\b(clear|remove|reset) (all )?(the )?filters\b|\bshow (me )?(all|everything)\b|\bany rating\b")]
    private static partial Regex ClearFilters();

    [GeneratedRegex(@"\bopen (right )?now\b|\bcurrently open\b|\bopen today\b")]
    private static partial Regex OpenNow();

    [GeneratedRegex(@"\b(urgent(ly)?|asap|emergency|immediately|right now|right away|available( now| today)?|today|tonight)\b")]
    private static partial Regex Urgent();

    [GeneratedRegex(@"\b(verified|trusted|certified|genuine)\b")]
    private static partial Regex Verified();

    [GeneratedRegex(@"\b(home (visit|service)s?|at (my )?(home|house|place|doorstep)|doorstep|come (to )?(my )?(home|house|place)|visit (my )?(home|house|place)|can visit)\b")]
    private static partial Regex HomeVisit();

    [GeneratedRegex(@"\b(video( call| consultation| consult)?|online consultation|teleconsult(ation)?|virtual(ly)?)\b")]
    private static partial Regex Video();

    [GeneratedRegex(@"\b(book online|online booking|book (an )?appointment|appointment|bookable)\b")]
    private static partial Regex Booking();

    [GeneratedRegex(@"\b(?:rated |rating )?(?:above |over |at least |min(?:imum)? )?(?<n>[1-5](?:\.\d)?)\s*\+?\s*(?:stars?|★)(?: (?:and|&) (?:up|above))?|\brated (?:above|over|at least) (?<n>[1-5](?:\.\d)?)\b")]
    private static partial Regex Stars();

    [GeneratedRegex(@"\b(top|best|highly|highest|well) ?rated\b|\bhighest rating\b|\bbest reviewed\b")]
    private static partial Regex TopRated();

    [GeneratedRegex(@"\b(cheap(er|est)?|affordable|budget|low(est)? (cost|price)|inexpensive|economical|reasonable)\b")]
    private static partial Regex Cheap();

    [GeneratedRegex(@"\b(most (reviewed|popular)|popular)\b")]
    private static partial Regex MostReviewed();

    [GeneratedRegex(@"\b(nearest|closest|near me|nearby|close to me|close by)\b")]
    private static partial Regex Nearest();
}
