using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Geo;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Home;

/// <param name="BusinessCount">Active businesses in this sub-category in the visitor's city.</param>
/// <param name="BookingCount">Bookings in this sub-category in the city over the last 90 days.</param>
/// <param name="Source">"database" (ranked from the city's listings and bookings) or "ai" (added by the AI for this city).</param>
/// <param name="Reason">Why the AI suggests it for this city (AI picks only).</param>
public sealed record TopPickCategoryDto(string Name, string Slug, string CategoryName, string CategorySlug, string? ColorHex, string? IconUrl,
    int BusinessCount, decimal Rating, int BookingCount, string Source, string? Reason);

/// <param name="LocationSource">"ip" when detected from the visitor's IP address, "city" when the fallback city was used, "none" otherwise.</param>
/// <param name="TotalInCity">How many sub-categories have listings in the city (the list shows the top ones).</param>
/// <param name="AiEnriched">True once the AI's city-specific additions are included.</param>
/// <param name="AiPending">True while those additions are being generated; poll until it turns false.</param>
public sealed record TopPicksDto(string? CityName, string? CitySlug, string? PlaceName, string LocationSource, int TotalInCity,
    bool AiEnriched, bool AiPending, string? AiModel, IReadOnlyList<TopPickCategoryDto> Categories);

/// <param name="FallbackCitySlug">Used only when the IP can't be placed near a listed city.</param>
/// <param name="CitySlug">The city the visitor chose in the selector: takes precedence over the IP (whose coordinates are still used inside it).</param>
public sealed record GetTopPicksQuery(string? ClientIp, string? FallbackCitySlug, double RadiusKm, string? CitySlug = null) : IRequest<TopPicksDto>;

/// <summary>
/// Category list for the "Top picks" section, specific to the visitor's city. Database results come first and are returned immediately
/// (sub-categories ranked by the city's bookings, listings and ratings); the local AI model then adds categories suited to the city in
/// the background (see <see cref="ICityCategoryRecommender"/>).
/// </summary>
public sealed class GetTopPicksHandler(IUnitOfWork uow, VisitorOriginResolver origins, ICityCategoryRecommender ai)
    : IRequestHandler<GetTopPicksQuery, TopPicksDto>
{
    private const int DatabaseShow = 10;
    private const int AiPicks = 6;
    private const int AiCandidates = 30;

    public async Task<TopPicksDto> Handle(GetTopPicksQuery r, CancellationToken ct)
    {
        // Always the IP address's city; the fallback city only when the IP can't be located near a listed city.
        var origin = !string.IsNullOrWhiteSpace(r.CitySlug)
            ? await origins.ResolveAsync(r.ClientIp, r.CitySlug, null, r.RadiusKm, ct)
            : await origins.ResolveAsync(r.ClientIp, null, null, r.RadiusKm, ct)
              ?? await origins.ResolveAsync(null, r.FallbackCitySlug, null, r.RadiusKm, ct);
        if (origin is null) return new TopPicksDto(null, null, null, "none", 0, false, false, null, []);

        var cityId = origin.CityId;
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var bookings = uow.Repository<Booking>().QueryNoTracking().Where(b => b.CreatedOn >= since && b.Business.CityId == cityId);
        var stats = (await uow.Repository<SubCategory>().QueryNoTracking()
                .Where(s => s.IsActive && s.Category.IsActive)
                .Select(s => new
                {
                    s.Id, s.Slug, s.Name, Cat = s.Category.Name, CatSlug = s.Category.Slug, s.Category.ColorHex, s.IconUrl,
                    Businesses = s.Businesses.Count(b => b.Status == BusinessStatuses.Active && b.CityId == cityId),
                    Reviews = s.Businesses.Where(b => b.Status == BusinessStatuses.Active && b.CityId == cityId).Sum(b => (int?)b.ReviewCount) ?? 0,
                    RatingPoints = s.Businesses.Where(b => b.Status == BusinessStatuses.Active && b.CityId == cityId)
                        .Sum(b => (double?)b.AverageRating * b.ReviewCount) ?? 0,
                    Bookings = bookings.Count(b => b.Business.SubCategoryId == s.Id),
                })
                .ToListAsync(ct))
            .Select(s => new
            {
                s.Bookings,
                Dto = new TopPickCategoryDto(s.Name, s.Slug, s.Cat, s.CatSlug, s.ColorHex, s.IconUrl, s.Businesses,
                    s.Reviews > 0 ? Math.Round((decimal)(s.RatingPoints / s.Reviews), 1) : 0, s.Bookings, "database", null),
            })
            .ToList();

        // Database first: what the city actually books and lists most.
        var inCity = stats
            .Where(s => s.Dto.BusinessCount > 0)
            .OrderByDescending(s => s.Bookings).ThenByDescending(s => s.Dto.BusinessCount).ThenByDescending(s => s.Dto.Rating)
            .ToList();
        var shown = inCity.Take(DatabaseShow).Select(s => s.Dto).ToList();
        var shownSlugs = shown.Select(d => d.Slug).ToHashSet();

        // AI: extra categories for this city from the rest of the catalogue (local listings first), cached per city.
        var cacheKey = $"top-picks|{origin.CitySlug}";
        var suggestions = ai.GetCached(cacheKey);
        if (suggestions is null && ai.IsEnabled)
        {
            var candidates = stats
                .Where(s => !shownSlugs.Contains(s.Dto.Slug))
                .OrderByDescending(s => s.Dto.BusinessCount > 0).ThenByDescending(s => s.Bookings).ThenByDescending(s => s.Dto.BusinessCount)
                .Take(AiCandidates)
                .Select(s => new AiCityCategoryCandidate(s.Dto.Slug, s.Dto.Name, s.Dto.CategoryName, s.Dto.BusinessCount, s.Bookings))
                .ToList();
            ai.Enqueue(cacheKey, new AiCityCategoriesRequest(origin.CityName, origin.State, origin.CityName, IndianTime.Now,
                shown.Select(d => d.Name).ToList(), candidates, AiPicks));
        }

        var categories = shown;
        if (suggestions is not null)
        {
            var bySlug = stats.ToDictionary(s => s.Dto.Slug, s => s.Dto);
            categories = shown.Concat(suggestions.Picks
                    .Where(p => bySlug.ContainsKey(p.Key) && !shownSlugs.Contains(p.Key))
                    .Select(p => bySlug[p.Key] with { Source = "ai", Reason = p.Reason }))
                .ToList();
        }

        return new TopPicksDto(origin.CityName, origin.CitySlug, origin.PlaceName, origin.Source == "ip" ? "ip" : "city", inCity.Count,
            suggestions is not null, suggestions is null && ai.IsPending(cacheKey), suggestions?.Model ?? (ai.IsEnabled ? ai.Model : null),
            categories);
    }
}
