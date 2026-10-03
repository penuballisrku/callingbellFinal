using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Catalog;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Home;

/// <summary>
/// A home-page service card. <see cref="SearchTerm"/> is the underlying BusinessServices.Name used for search links;
/// <see cref="Rating"/> is the review-weighted average of the active businesses offering it (0 with <see cref="ReviewCount"/> 0 when unrated).
/// </summary>
public sealed record PopularServiceDto(string Name, string SearchTerm,
    string SubCategoryName, string SubCategorySlug, string CategoryName, string CategorySlug, string? ColorHex,
    string? ImageUrl, string? IconUrl, string? AltText, decimal StartingPrice, string? PriceUnit,
    decimal Rating, int ReviewCount, int BookingCount);

public sealed record PlatformStatsDto(int Businesses, int Cities, int Reviews, int BookingsCompleted);

public sealed record HomeDto(
    string? CitySlug,
    string? CityName,
    IReadOnlyList<BannerDto> HeroBanners,
    IReadOnlyList<BannerDto> PromoBanners,
    IReadOnlyList<SubCategoryDto> Categories,
    IReadOnlyList<PopularServiceDto> PopularServices,
    PlatformStatsDto Stats);

public sealed record GetHomeQuery(string? CitySlug) : IRequest<HomeDto>;

public sealed class GetHomeHandler(IUnitOfWork uow) : IRequestHandler<GetHomeQuery, HomeDto>
{
    public async Task<HomeDto> Handle(GetHomeQuery request, CancellationToken ct)
    {
        var city = string.IsNullOrWhiteSpace(request.CitySlug)
            ? null
            : await uow.Repository<City>().QueryNoTracking().Where(c => c.Slug == request.CitySlug && c.IsActive)
                .Select(c => new { c.Id, c.Name, c.Slug }).FirstOrDefaultAsync(ct);

        var listed = uow.Repository<Business>().QueryNoTracking().Listed();

        var hero = await GetBannersHandler.ActiveBanners(uow, "HomeHero").ToListAsync(ct);
        var promo = await GetBannersHandler.ActiveBanners(uow, "HomeMid").ToListAsync(ct);
        var categories = await CatalogQueries.FeaturedSubCategories(uow).ToListAsync(ct);

        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var popularServices = await PopularServices(since, ct);


        var stats = new PlatformStatsDto(
            await listed.CountAsync(ct),
            await uow.Repository<City>().QueryNoTracking().CountAsync(c => c.IsActive, ct),
            await uow.Repository<Review>().QueryNoTracking().CountAsync(r => r.Status == ReviewStatuses.Published, ct),
            await uow.Repository<Booking>().QueryNoTracking().CountAsync(b => b.Status == BookingStatuses.Completed, ct));

        return new HomeDto(city?.Slug, city?.Name, hero, promo, categories, popularServices, stats);
    }

    /// <summary>
    /// Curated list from dbo.PopularServices with live price / rating / booking figures; entries no active business offers are hidden.
    /// Falls back to the most-booked services (unrated) when nothing has been curated.
    /// </summary>
    private async Task<IReadOnlyList<PopularServiceDto>> PopularServices(DateTimeOffset since, CancellationToken ct)
    {
        var offered = uow.Repository<BusinessService>().QueryNoTracking().Where(s => s.IsActive && s.Business.Status == BusinessStatuses.Active);
        var recentBookings = uow.Repository<Booking>().QueryNoTracking().Where(b => b.CreatedOn >= since);

        var curated = await uow.Repository<PopularService>().QueryNoTracking()
            .Where(p => p.IsActive && p.SubCategory.IsActive && p.SubCategory.Category.IsActive
                        && offered.Any(s => s.Name == p.ServiceName && s.Business.SubCategoryId == p.SubCategoryId))
            .OrderBy(p => p.SortOrder)
            .Take(24)
            .Select(p => new
            {
                p.Title, p.ServiceName, Sub = p.SubCategory.Name, SubSlug = p.SubCategory.Slug, p.SubCategory.IconUrl,
                Cat = p.SubCategory.Category.Name, CatSlug = p.SubCategory.Category.Slug, p.SubCategory.Category.ColorHex,
                ImageUrl = p.ImageUrl ?? p.SubCategory.ImageUrl, AltText = p.AltText ?? p.Title,
                MinPrice = offered.Where(s => s.Name == p.ServiceName && s.Business.SubCategoryId == p.SubCategoryId && s.Price > 0).Min(s => (decimal?)s.Price),
                PriceUnit = offered.Where(s => s.Name == p.ServiceName && s.Business.SubCategoryId == p.SubCategoryId && s.Price > 0)
                    .OrderBy(s => s.Price).Select(s => s.PriceUnit).FirstOrDefault(),
                RatingPoints = offered.Where(s => s.Name == p.ServiceName && s.Business.SubCategoryId == p.SubCategoryId)
                    .Sum(s => (double?)s.Business.AverageRating * s.Business.ReviewCount),
                Reviews = offered.Where(s => s.Name == p.ServiceName && s.Business.SubCategoryId == p.SubCategoryId).Sum(s => (int?)s.Business.ReviewCount),
                Bookings = recentBookings.Count(b => b.Service.Name == p.ServiceName && b.Business.SubCategoryId == p.SubCategoryId),
            })
            .ToListAsync(ct);
        if (curated.Count > 0)
        {
            return curated.Select(p =>
            {
                var reviews = p.Reviews ?? 0;
                var rating = reviews > 0 ? Math.Round((decimal)((p.RatingPoints ?? 0) / reviews), 1) : 0;
                return new PopularServiceDto(p.Title, p.ServiceName, p.Sub, p.SubSlug, p.Cat, p.CatSlug, p.ColorHex, p.ImageUrl, p.IconUrl, p.AltText,
                    p.MinPrice ?? 0, p.PriceUnit, rating, reviews, p.Bookings);
            }).ToList();
        }

        return await recentBookings
            .Where(b => b.Business.SubCategory != null && b.Service.Price > 0)
            .GroupBy(b => new
            {
                b.Service.Name, Sub = b.Business.SubCategory!.Name, SubSlug = b.Business.SubCategory.Slug, b.Business.SubCategory.ImageUrl,
                b.Business.SubCategory.IconUrl, Cat = b.Business.Category.Name, CatSlug = b.Business.Category.Slug, b.Business.Category.ColorHex
            })
            .Select(g => new { g.Key, MinPrice = g.Min(x => x.Service.Price), Bookings = g.Count() })
            .OrderByDescending(s => s.Bookings)
            .Take(8)
            .Select(s => new PopularServiceDto(s.Key.Name, s.Key.Name, s.Key.Sub, s.Key.SubSlug, s.Key.Cat, s.Key.CatSlug, s.Key.ColorHex,
                s.Key.ImageUrl, s.Key.IconUrl, s.Key.Name, s.MinPrice, null, 0, 0, s.Bookings))
            .ToListAsync(ct);
    }
}
