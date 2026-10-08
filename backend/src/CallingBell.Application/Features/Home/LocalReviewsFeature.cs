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

/// <param name="Scope">
/// "area" (businesses near the chosen or detected area), "city", "state", "country" or "all": the narrowest place with enough reviews.
/// </param>
/// <param name="AverageRating">Average rating of the recent reviews in scope (all ratings, not just the ones shown).</param>
/// <param name="AiSummary">Two-sentence AI summary of the real reviews in scope, once generated (null otherwise).</param>
/// <param name="AiPending">True while that summary is being generated.</param>
/// <param name="ScopeName">The place the reviews are from: the area, city, state or country name; null for "all".</param>
public sealed record LocalReviewsDto(string? PlaceName, string? CityName, string Scope, int ReviewCount, decimal AverageRating,
    string? AiSummary, bool AiPending, IReadOnlyList<LocalReviewDto> Reviews, string? ScopeName = null);

/// <param name="Exclude">Review ids the visitor saw recently; avoided when enough other reviews exist.</param>
/// <param name="Country">The country being browsed (ISO code) when no city is chosen; the IP's location is ignored if it is elsewhere.</param>
public sealed record GetLocalReviewsQuery(string? ClientIp, string? CitySlug, string? AreaSlug, double RadiusKm, IReadOnlyCollection<Guid> Exclude,
    string? Country = null) : IRequest<LocalReviewsDto>;

/// <summary>
/// Real, published customer reviews from the narrowest place that has enough of them: the chosen (or IP) area, then its city, state
/// and country, then everywhere. A different random selection on every request. Nothing is generated: the local AI only summarises
/// these real reviews.
/// </summary>
public sealed class GetLocalReviewsHandler(IUnitOfWork uow, VisitorOriginResolver origins, IReviewSummarizer summarizer, IMemoryCache cache)
    : IRequestHandler<GetLocalReviewsQuery, LocalReviewsDto>
{
    private const int Show = 9;
    private const int MinForScope = 12;
    /// <summary>The area is used only when this many different businesses there have reviews; otherwise the whole city.</summary>
    private const int MinBusinessesForArea = 5;
    /// <summary>A city, state or country is used when it has at least this many showcase-quality reviews; otherwise the next level up.</summary>
    private const int MinShowcase = 3;
    /// <summary>At most this many reviews of one business in a set, so the carousel shows a range of businesses.</summary>
    private const int MaxPerBusiness = 2;
    private const double AreaKm = 5;
    private const int Pool = 400;

    public async Task<LocalReviewsDto> Handle(GetLocalReviewsQuery r, CancellationToken ct)
    {
        var origin = await origins.ResolveAsync(r.ClientIp, r.CitySlug, r.AreaSlug, r.RadiusKm, ct);
        var country = r.Country is { Length: 2 } c2 ? c2.ToUpperInvariant() : origin?.CountryCode;
        // Browsing another country than the IP's, with no city chosen: the IP's city says nothing about the place being browsed.
        if (string.IsNullOrWhiteSpace(r.CitySlug) && origin is not null && country is not null && origin.CountryCode != country) origin = null;

        // Narrowest place with enough reviews: area, city, state, country, then everywhere.
        List<PoolReview> inScope = [];
        string scope = "all";
        string? scopeName = null;
        if (origin is not null)
        {
            var cityPool = await PoolAsync(Level.City, origin.CityId.ToString(), ct);
            if (origin is { Source: "area" or "ip", Lat: { } lat, Lng: { } lng })
            {
                var near = cityPool.Where(x => x.Latitude is { } bl && x.Longitude is { } bg && GeoMath.HaversineKm(lat, lng, (double)bl, (double)bg) <= AreaKm).ToList();
                if (near.Count >= MinForScope && near.Select(x => x.Slug).Distinct().Count() >= MinBusinessesForArea) (inScope, scope, scopeName) = (near, "area", origin.PlaceName);
            }
            if (scope == "all" && Showcase(cityPool) >= MinShowcase) (inScope, scope, scopeName) = (cityPool, "city", origin.CityName);
            if (scope == "all" && origin.CountryCode is { } cc && await PoolAsync(Level.State, $"{cc}|{origin.State}", ct) is var statePool
                && Showcase(statePool) >= MinShowcase)
                (inScope, scope, scopeName) = (statePool, "state", origin.State);
        }
        if (scope == "all" && country is not null && await PoolAsync(Level.Country, country, ct) is var countryPool && Showcase(countryPool) >= MinShowcase)
            (inScope, scope, scopeName) = (countryPool, "country", CountryName(country));
        if (scope == "all") inScope = await PoolAsync(Level.All, "", ct);

        // Showcase: well-written positive reviews, a fresh random selection each time, avoiding what the visitor saw recently.
        var showcase = inScope.Where(IsShowcase).ToList();
        var exclude = r.Exclude.ToHashSet();
        var ordered = showcase.Where(x => !exclude.Contains(x.Id)).OrderBy(_ => Random.Shared.Next())
            .Concat(showcase.Where(x => exclude.Contains(x.Id)).OrderBy(_ => Random.Shared.Next()));  // seen ones only if needed
        var perBusiness = new Dictionary<string, int>();
        var picked = ordered.Where(x => (perBusiness[x.Slug] = perBusiness.GetValueOrDefault(x.Slug) + 1) <= MaxPerBusiness).Take(Show).ToList();

        // AI summary of the real reviews in scope (cached per place); computed numbers are always available as a fallback.
        var place = scopeName ?? "Calling Bell";
        var key = $"reviews|{scope}|{(scope == "country" ? country : origin?.CitySlug)}|{(scope == "area" ? origin!.PlaceKey : scope == "state" ? origin!.State : "")}";
        var summary = summarizer.GetCached(key);
        if (summary is null && summarizer.IsEnabled && showcase.Count >= 5)
        {
            summarizer.Enqueue(key, new AiReviewSummaryRequest(place, origin?.CityName ?? place,
                showcase.Take(25).Select(x => (x.Rating, x.Comment)).ToList()));
        }

        return new LocalReviewsDto(origin?.PlaceName, origin?.CityName, scope, inScope.Count,
            inScope.Count > 0 ? Math.Round((decimal)inScope.Average(x => (double)x.Rating), 1) : 0,
            summary?.Text, summary is null && summarizer.IsPending(key),
            picked.Select(x => new LocalReviewDto(x.Id, ShortName(x.Customer), x.Rating, x.Title, x.Comment, x.CreatedOn, x.IsVerifiedVisit,
                x.Name, x.Slug, x.LogoUrl, x.Sub, x.Area, x.City)).ToList(), scopeName);
    }

    private enum Level { City, State, Country, All }

    private static bool IsShowcase(PoolReview x) => x.Rating >= 4 && x.Comment.Length >= 50;

    private static int Showcase(List<PoolReview> pool) => pool.Count(IsShowcase);

    private static string CountryName(string code)
    {
        try { return new System.Globalization.RegionInfo(code).EnglishName; }
        catch (ArgumentException) { return code; }
    }

    /// <summary>
    /// The most recent published reviews of a city, state ("IN|Andhra Pradesh"), country or everywhere, kept in memory for
    /// <see cref="PoolLifetime"/>: every home page view asks for a fresh random selection, which is drawn from this pool instead of
    /// re-reading SQL Server each time.
    /// </summary>
    private async Task<List<PoolReview>> PoolAsync(Level level, string id, CancellationToken ct)
    {
        var key = $"reviews-pool|{level}|{id}";
        if (cache.TryGetValue(key, out List<PoolReview>? hit) && hit is not null) return hit;
        var reviews = uow.Repository<Review>().QueryNoTracking()
            .Where(x => x.Status == ReviewStatuses.Published && x.Business.Status == BusinessStatuses.Active);
        switch (level)
        {
            case Level.City:
                var cityId = Guid.Parse(id);
                reviews = reviews.Where(x => x.Business.CityId == cityId);
                break;
            case Level.State:
                var (cc, state) = (id[..2], id[3..]);
                reviews = reviews.Where(x => x.Business.CityRef != null && x.Business.CityRef.State.CountryCode == cc && x.Business.CityRef.State.Name == state);
                break;
            case Level.Country:
                reviews = reviews.Where(x => x.Business.CityRef != null && x.Business.CityRef.State.CountryCode == id);
                break;
        }
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
