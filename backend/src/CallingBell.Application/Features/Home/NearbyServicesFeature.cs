using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Geo;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Home;

/// <param name="BookingCount">Bookings of this service in the visitor's city over the last 90 days.</param>
/// <param name="NearestKm">Distance from the visitor's area to the closest business offering it.</param>
/// <param name="Reason">One-line AI explanation of why it is popular here and now (null until the AI ranking is ready).</param>
public sealed record NearbyServiceDto(string Name, string SearchTerm, string SubCategoryName, string SubCategorySlug, string CategoryName,
    string CategorySlug, string? ColorHex, string? ImageUrl, string? IconUrl, decimal StartingPrice, string? PriceUnit,
    decimal Rating, int ReviewCount, int BookingCount, double? NearestKm, string? Reason);

/// <summary>A sub-category related to the popular services near the visitor, with how much of it is available nearby.</summary>
/// <param name="Reason">One-line AI explanation of how it complements the popular services (null until the AI ranking is ready).</param>
public sealed record RelatedCategoryDto(string Name, string Slug, string CategoryName, string CategorySlug, string? ColorHex, string? IconUrl,
    string? ImageUrl, int BusinessCount, double? NearestKm, string? Reason);

/// <param name="Source">How the origin was found: "area" (selected area), "ip" (visitor IP coordinates), "city" (city centre) or "none".</param>
/// <param name="AiRanked">True when <see cref="Items"/> is ordered by the AI model; false while only the database ranking is available.</param>
/// <param name="AiPending">True while an AI ranking is being generated; the client can poll until it flips to <see cref="AiRanked"/>.</param>
/// <param name="RelatedCategories">Sub-categories near the visitor. For a selected area only those with a business within
/// <see cref="GetNearbyServicesHandler.NearKm"/> of it; the rest of the city's are in <paramref name="CityCategories"/>.</param>
/// <param name="CityCategories">Area searches only: the city's other sub-categories, most businesses first.</param>
/// <param name="Catalog">True when nothing is listed in the city yet: items and categories come from the platform-wide catalogue
/// (national figures, no distances) and the AI picks what suits the place.</param>
public sealed record NearbyServicesDto(string? PlaceName, string? CityName, string? CitySlug, string Source,
    bool AiRanked, bool AiPending, string? AiModel, IReadOnlyList<NearbyServiceDto> Items, IReadOnlyList<RelatedCategoryDto> RelatedCategories,
    IReadOnlyList<RelatedCategoryDto> CityCategories, bool Catalog = false);

/// <param name="ClientIp">Visitor IP, located server-side for precise coordinates.</param>
/// <param name="CitySlug">The city the visitor is browsing; when it isn't the IP's city, the city centre is used instead.</param>
/// <param name="AreaSlug">A specific area within <paramref name="CitySlug"/>; takes precedence over the IP.</param>
public sealed record GetNearbyServicesQuery(string? ClientIp, string? CitySlug, string? AreaSlug, double RadiusKm) : IRequest<NearbyServicesDto>;

/// <summary>
/// Popular services near the visitor. The shortlist is computed from the database (recent bookings weighted by proximity, then rating);
/// a local AI model then re-ranks it for the place and season in the background (see <see cref="IServiceRecommender"/>), and suggests
/// related sub-categories available nearby that complement those services. A city with listings but no recent bookings is ranked by
/// rating; a city with no listings at all falls back to the platform-wide catalogue, from which the AI picks what suits the place.
/// </summary>
public sealed class GetNearbyServicesHandler(IUnitOfWork uow, VisitorOriginResolver origins, IServiceRecommender ai, ReferenceDataCache reference)
    : IRequestHandler<GetNearbyServicesQuery, NearbyServicesDto>
{
    private const int Show = 8;
    private const int Shortlist = 14;
    private const int RelatedAiPicks = 6;
    private const int RelatedShortlist = 10;
    /// <summary>Without local listings the AI chooses from more of the catalogue, since local demand can't narrow it.</summary>
    private const int CatalogRelatedShortlist = 24;
    /// <summary>For a selected area, a category counts as near it when one of its businesses is within this distance.</summary>
    public const double NearKm = 7;

    public async Task<NearbyServicesDto> Handle(GetNearbyServicesQuery r, CancellationToken ct)
    {
        var origin = await origins.ResolveAsync(r.ClientIp, r.CitySlug, r.AreaSlug, r.RadiusKm, ct);
        if (origin is null) return new NearbyServicesDto(null, null, null, "none", false, false, null, [], [], []);

        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var bookings = uow.Repository<Booking>().QueryNoTracking().Where(b => b.CreatedOn >= since);
        var rows = await uow.Repository<BusinessService>().QueryNoTracking()
            .Where(s => s.IsActive && s.Business.Status == BusinessStatuses.Active && s.Business.CityId == origin.CityId && s.Business.SubCategory != null)
            .Select(s => new
            {
                s.Name, s.Price, s.PriceUnit, s.BusinessId, s.Business.Latitude, s.Business.Longitude, s.Business.AverageRating, s.Business.ReviewCount,
                Sub = s.Business.SubCategory!.Name, SubSlug = s.Business.SubCategory.Slug, SubImage = s.Business.SubCategory.ImageUrl,
                SubIcon = s.Business.SubCategory.IconUrl, Cat = s.Business.Category.Name, CatSlug = s.Business.Category.Slug, s.Business.Category.ColorHex,
                Bookings = bookings.Count(b => b.ServiceId == s.Id),
            })
            .ToListAsync(ct);

        // Prefer the curated card artwork when the service is also a curated popular service.
        var artwork = await uow.Repository<PopularService>().QueryNoTracking()
            .Where(p => p.IsActive && p.ImageUrl != null)
            .Select(p => new { p.SubCategory.Slug, p.ServiceName, p.ImageUrl })
            .ToListAsync(ct);
        var artworkByKey = artwork.GroupBy(a => Key(a.Slug, a.ServiceName)).ToDictionary(g => g.Key, g => g.First().ImageUrl);

        var located = rows
            .Select(x => new
            {
                x,
                Km = origin.Lat is { } lat && origin.Lng is { } lng && x.Latitude is { } bl && x.Longitude is { } bg
                    ? GeoMath.HaversineKm(lat, lng, (double)bl, (double)bg) : (double?)null,
            })
            .ToList();

        var scored = located
            .GroupBy(v => Key(v.x.SubSlug, v.x.Name))
            .Select(g =>
            {
                var first = g.First().x;
                var reviews = g.Sum(v => v.x.ReviewCount);
                var rating = reviews > 0 ? Math.Round(g.Sum(v => v.x.AverageRating * v.x.ReviewCount) / reviews, 1) : 0;
                var priced = g.Where(v => v.x.Price > 0).OrderBy(v => v.x.Price).FirstOrDefault()?.x;
                var nearest = g.Min(v => v.Km);
                // Demand near the visitor: each booking counts more the closer its business is (halves at ~4 km).
                var demand = g.Sum(v => v.x.Bookings / (1 + (v.Km ?? 8) / 4));
                return new
                {
                    Key = g.Key,
                    Score = demand * (0.5 + (double)rating / 10),
                    Dto = new NearbyServiceDto(first.Name, first.Name, first.Sub, first.SubSlug, first.Cat, first.CatSlug, first.ColorHex,
                        artworkByKey.GetValueOrDefault(g.Key) ?? first.SubImage, first.SubIcon, priced?.Price ?? 0, priced?.PriceUnit,
                        rating, reviews, g.Sum(v => v.x.Bookings), nearest is { } km ? Math.Round(km, 1) : null, null),
                };
            })
            .ToList();
        // Listings but no recent bookings in the city: rank what is listed by rating instead of showing nothing.
        if (scored.Any(c => c.Dto.BookingCount > 0)) scored = scored.Where(c => c.Dto.BookingCount > 0).ToList();

        // Nothing listed in the city yet: the platform-wide catalogue (national figures, no distances); the AI picks for the place.
        var catalogOnly = located.Count == 0;
        if (catalogOnly)
        {
            // National figures, the same for every empty city: kept in memory with the other reference data (10 minutes).
            scored = (await reference.GetDerivedAsync("nearby|catalog-services", () => GetHomeHandler.PopularServices(uow, since, ct), ct))
                .Select(p => new
                {
                    Key = Key(p.SubCategorySlug, p.SearchTerm),
                    Score = (double)p.BookingCount,
                    Dto = new NearbyServiceDto(p.Name, p.SearchTerm, p.SubCategoryName, p.SubCategorySlug, p.CategoryName, p.CategorySlug,
                        p.ColorHex, p.ImageUrl, p.IconUrl, p.StartingPrice, p.PriceUnit, p.Rating, p.ReviewCount, p.BookingCount, null, null),
                })
                .ToList();
        }

        var ranked = scored
            // One service per sub-category (its strongest) so a single busy business can't fill the whole list.
            .GroupBy(c => c.Dto.SubCategorySlug)
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .OrderByDescending(c => c.Score).ThenByDescending(c => c.Dto.Rating)
            .Take(Shortlist)
            .ToList();

        // Every sub-category with listings in the city: how many businesses, how close, and proximity-weighted demand.
        var subStats = located
            .GroupBy(v => v.x.SubSlug)
            .Select(g =>
            {
                var first = g.First().x;
                var nearest = g.Min(v => v.Km);
                return new
                {
                    Demand = g.Sum(v => v.x.Bookings / (1 + (v.Km ?? 8) / 4)),
                    Bookings = g.Sum(v => v.x.Bookings),
                    Dto = new RelatedCategoryDto(first.Sub, first.SubSlug, first.Cat, first.CatSlug, first.ColorHex, first.SubIcon, first.SubImage,
                        g.Select(v => v.x.BusinessId).Distinct().Count(), nearest is { } km ? Math.Round(km, 1) : null, null),
                };
            })
            .ToList();
        if (catalogOnly)
        {
            // The whole active catalogue, most booked nationally first; no local business counts or distances.
            subStats = (await reference.GetDerivedAsync("nearby|catalog-subs", () => uow.Repository<SubCategory>().QueryNoTracking()
                    .Where(s => s.IsActive && s.Category.IsActive)
                    .Select(s => new
                    {
                        s.Name, s.Slug, Cat = s.Category.Name, CatSlug = s.Category.Slug, s.Category.ColorHex, s.IconUrl, s.ImageUrl,
                        Bookings = bookings.Count(b => b.Business.SubCategoryId == s.Id),
                    })
                    .ToListAsync(ct), ct))
                .Select(s => new
                {
                    Demand = (double)s.Bookings,
                    s.Bookings,
                    Dto = new RelatedCategoryDto(s.Name, s.Slug, s.Cat, s.CatSlug, s.ColorHex, s.IconUrl, s.ImageUrl, 0, null, null),
                })
                .ToList();
        }
        // Every nearby sub-category, those sharing a category with the popular services first. The AI chooses from the top of this
        // list (minus what the database already shows above); the client gets all of it so it can offer "show all".
        var shownSubs = ranked.Take(Show).Select(c => c.Dto.SubCategorySlug).ToHashSet();
        var shownCategories = ranked.Take(Show).Select(c => c.Dto.CategorySlug).ToHashSet();
        // For a selected area, categories with a business close to it come first, so the AI suggests from those.
        var isArea = origin.Source == "area";
        bool Near(RelatedCategoryDto d) => d.NearestKm is { } km && km <= NearKm;
        var relatedAll = subStats
            .OrderByDescending(s => isArea && Near(s.Dto))
            .ThenByDescending(s => shownCategories.Contains(s.Dto.CategorySlug))
            .ThenByDescending(s => s.Demand).ThenByDescending(s => s.Dto.BusinessCount).ThenBy(s => s.Dto.NearestKm ?? double.MaxValue)
            .ToList();
        var relatedRanked = relatedAll.Where(s => !shownSubs.Contains(s.Dto.Slug))
            .Take(catalogOnly ? CatalogRelatedShortlist : RelatedShortlist).ToList();

        var cacheKey = $"{origin.CitySlug}|{origin.Source}|{origin.PlaceKey}{(catalogOnly ? "|catalog" : "")}";
        var ranking = ai.GetCached(cacheKey);
        if (ranking is null && ai.IsEnabled && ranked.Count > 0)
        {
            ai.Enqueue(cacheKey, new AiServiceRankingRequest(origin.PlaceName, origin.CityName, origin.State, IndianTime.Now, Show,
                ranked.Select(c => new AiServiceCandidate(c.Key, c.Dto.Name, c.Dto.SubCategoryName, c.Dto.CategoryName, c.Dto.BookingCount,
                    c.Dto.Rating, c.Dto.NearestKm, c.Dto.StartingPrice)).ToList(),
                RelatedAiPicks,
                relatedRanked.Select(s => new AiCategoryCandidate(s.Dto.Slug, s.Dto.Name, s.Dto.CategoryName, s.Dto.BusinessCount, s.Bookings,
                    s.Dto.NearestKm)).ToList(), catalogOnly));
        }

        IEnumerable<NearbyServiceDto> items = ranked.Select(c => c.Dto);
        if (ranking is not null)
        {
            // AI picks first (with their reasons), then the database order for anything the model left out.
            var byKey = ranked.ToDictionary(c => c.Key, c => c.Dto);
            var picked = ranking.Picks.Where(p => byKey.ContainsKey(p.Key)).Select(p => byKey[p.Key] with { Reason = p.Reason }).ToList();
            var pickedKeys = ranking.Picks.Select(p => p.Key).ToHashSet();
            items = picked.Concat(ranked.Where(c => !pickedKeys.Contains(c.Key)).Select(c => c.Dto));
        }

        var shown = items.Take(Show).ToList();

        // Related categories: the AI's suggestions (with reasons) first, then the database order; never a sub-category already shown above.
        var finalSubs = shown.Select(i => i.SubCategorySlug).ToHashSet();
        IEnumerable<RelatedCategoryDto> related = relatedAll.Select(s => s.Dto);
        if (ranking is not null)
        {
            var bySlug = subStats.ToDictionary(s => s.Dto.Slug, s => s.Dto);
            var suggested = ranking.Related.Where(p => bySlug.ContainsKey(p.Key)).Select(p => bySlug[p.Key] with { Reason = p.Reason });
            var suggestedSlugs = ranking.Related.Select(p => p.Key).ToHashSet();
            related = suggested.Concat(related.Where(d => !suggestedSlugs.Contains(d.Slug)));
        }

        var available = related.Where(d => !finalSubs.Contains(d.Slug)).ToList();
        // A selected area: what is near it (AI suggestions first, then the closest), and separately the rest of the city.
        List<RelatedCategoryDto> near = available, city = [];
        if (isArea && catalogOnly)
        {
            // No distances without listings: the AI's picks for the area are "near", the rest of the catalogue is the city's.
            near = available.Where(d => d.Reason is not null).ToList();
            city = available.Where(d => d.Reason is null).ToList();
        }
        else if (isArea)
        {
            near = available.Where(Near).OrderByDescending(d => d.Reason is not null).ThenBy(d => d.NearestKm).ToList();
            city = available.Where(d => !Near(d)).OrderByDescending(d => d.BusinessCount).ThenBy(d => d.Name).ToList();
        }
        return new NearbyServicesDto(origin.PlaceName, origin.CityName, origin.CitySlug, origin.Source,
            ranking is not null, ranking is null && ai.IsPending(cacheKey), ranking?.Model ?? (ai.IsEnabled ? ai.Model : null),
            shown, near, city, catalogOnly);
    }

    private static string Key(string subSlug, string serviceName) => $"{subSlug}|{serviceName}";
}
