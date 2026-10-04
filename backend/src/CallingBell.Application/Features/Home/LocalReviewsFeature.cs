using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Geo;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Home;

/// <param name="CustomerName">Reviewer's first name and last initial (e.g. "Aarav S.") - full names are not shown publicly.</param>
public sealed record LocalReviewDto(Guid Id, string CustomerName, byte Rating, string? Title, string Comment, DateTimeOffset CreatedOn,
    bool IsVerifiedVisit, string BusinessName, string BusinessSlug, string? BusinessLogoUrl, string? SubCategoryName, string? Area, string City);

/// <param name="Scope">"area" (businesses near the visitor), "city" or "all" - widened when a scope has too few reviews.</param>
/// <param name="AverageRating">Average rating of the recent reviews in scope (all ratings, not just the ones shown).</param>
/// <param name="AiSummary">Two-sentence AI summary of the real reviews in scope, once generated (null otherwise).</param>
/// <param name="AiPending">True while that summary is being generated.</param>
public sealed record LocalReviewsDto(string? PlaceName, string? CityName, string Scope, int ReviewCount, decimal AverageRating,
    string? AiSummary, bool AiPending, IReadOnlyList<LocalReviewDto> Reviews);

/// <param name="Exclude">Review ids the visitor saw recently; avoided when enough other reviews exist.</param>
public sealed record GetLocalReviewsQuery(string? ClientIp, string? CitySlug, string? AreaSlug, double RadiusKm, IReadOnlyCollection<Guid> Exclude)
    : IRequest<LocalReviewsDto>;

/// <summary>
/// Real, published customer reviews of businesses near the visitor (IP area or chosen area, then the whole city, then everywhere),
/// a different random selection on every request. Nothing is generated: the local AI only summarises these real reviews.
/// </summary>
public sealed class GetLocalReviewsHandler(IUnitOfWork uow, VisitorOriginResolver origins, IReviewSummarizer summarizer, IMemoryCache cache)
    : IRequestHandler<GetLocalReviewsQuery, LocalReviewsDto>
{
    private const int Show = 9;
    private const int MinForScope = 12;
    /// <summary>The area is used only when this many different businesses there have reviews; otherwise the whole city.</summary>
    private const int MinBusinessesForArea = 5;
    /// <summary>At most this many reviews of one business in a set, so the carousel shows a range of businesses.</summary>
    private const int MaxPerBusiness = 2;
    private const double AreaKm = 5;
    private const int Pool = 400;

    public async Task<LocalReviewsDto> Handle(GetLocalReviewsQuery r, CancellationToken ct)
    {
        var origin = await origins.ResolveAsync(r.ClientIp, r.CitySlug, r.AreaSlug, r.RadiusKm, ct);
        var pool = await PoolAsync(origin?.CityId, ct);

        // Narrow to the visitor's area when it has enough reviews; otherwise the city; with no location at all, everywhere.
        var scope = origin is null ? "all" : "city";
        var inScope = pool;
        if (origin is { Lat: { } lat, Lng: { } lng })
        {
            var near = pool.Where(x => x.Latitude is { } bl && x.Longitude is { } bg && GeoMath.HaversineKm(lat, lng, (double)bl, (double)bg) <= AreaKm).ToList();
            if (near.Count >= MinForScope && near.Select(x => x.Slug).Distinct().Count() >= MinBusinessesForArea) { inScope = near; scope = "area"; }
        }
        if (inScope.Count == 0 && origin is not null)
        {
            inScope = await PoolAsync(null, ct);
            scope = "all";
        }

        // Showcase: well-written positive reviews, a fresh random selection each time, avoiding what the visitor saw recently.
        var showcase = inScope.Where(x => x.Rating >= 4 && x.Comment.Length >= 50).ToList();
        var exclude = r.Exclude.ToHashSet();
        var ordered = showcase.Where(x => !exclude.Contains(x.Id)).OrderBy(_ => Random.Shared.Next())
            .Concat(showcase.Where(x => exclude.Contains(x.Id)).OrderBy(_ => Random.Shared.Next()));  // seen ones only if needed
        var perBusiness = new Dictionary<string, int>();
        var picked = ordered.Where(x => (perBusiness[x.Slug] = perBusiness.GetValueOrDefault(x.Slug) + 1) <= MaxPerBusiness).Take(Show).ToList();

        // AI summary of the real reviews in scope (cached per place); computed numbers are always available as a fallback.
        var place = scope switch { "area" => origin!.PlaceName, "city" => origin!.CityName, _ => "India" };
        var key = $"reviews|{scope}|{origin?.CitySlug}|{(scope == "area" ? origin!.PlaceKey : "")}";
        var summary = summarizer.GetCached(key);
        if (summary is null && summarizer.IsEnabled && showcase.Count >= 5)
        {
            summarizer.Enqueue(key, new AiReviewSummaryRequest(place, origin?.CityName ?? "India",
                showcase.Take(25).Select(x => (x.Rating, x.Comment)).ToList()));
        }

        return new LocalReviewsDto(origin?.PlaceName, origin?.CityName, scope, inScope.Count,
            inScope.Count > 0 ? Math.Round((decimal)inScope.Average(x => (double)x.Rating), 1) : 0,
            summary?.Text, summary is null && summarizer.IsPending(key),
            picked.Select(x => new LocalReviewDto(x.Id, ShortName(x.Customer), x.Rating, x.Title, x.Comment, x.CreatedOn, x.IsVerifiedVisit,
                x.Name, x.Slug, x.LogoUrl, x.Sub, x.Area, x.City)).ToList());
    }

    /// <summary>
    /// The most recent published reviews of a city (or everywhere), kept in memory for <see cref="PoolLifetime"/>: every home page view
    /// asks for a fresh random selection, which is drawn from this pool instead of re-reading SQL Server each time.
    /// </summary>
    private async Task<List<PoolReview>> PoolAsync(Guid? cityId, CancellationToken ct)
    {
        var key = $"reviews-pool|{cityId?.ToString() ?? "all"}";
        if (cache.TryGetValue(key, out List<PoolReview>? hit) && hit is not null) return hit;
        var reviews = uow.Repository<Review>().QueryNoTracking()
            .Where(x => x.Status == ReviewStatuses.Published && x.Business.Status == BusinessStatuses.Active);
        if (cityId is { } id) reviews = reviews.Where(x => x.Business.CityId == id);
        var pool = await reviews
            .OrderByDescending(x => x.CreatedOn)
            .Take(Pool)
            .Select(x => new PoolReview(x.Id, x.Rating, x.Title, x.Comment, x.CreatedOn, x.IsVerifiedVisit, x.Customer.DisplayName,
                x.Business.Name, x.Business.Slug, x.Business.LogoUrl, x.Business.SubCategory != null ? x.Business.SubCategory.Name : null,
                x.Business.Area, x.Business.City, x.Business.Latitude, x.Business.Longitude))
            .ToListAsync(ct);
        cache.Set(key, pool, PoolLifetime);
        return pool;
    }

    private static readonly TimeSpan PoolLifetime = TimeSpan.FromMinutes(2);

    private sealed record PoolReview(Guid Id, byte Rating, string? Title, string Comment, DateTimeOffset CreatedOn, bool IsVerifiedVisit, string? Customer,
        string Name, string Slug, string? LogoUrl, string? Sub, string? Area, string City, decimal? Latitude, decimal? Longitude);

    /// <summary>"Aarav Sharma" -> "Aarav S."; single names are shown as they are.</summary>
    private static string ShortName(string? displayName)
    {
        var parts = (displayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Verified customer",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}
