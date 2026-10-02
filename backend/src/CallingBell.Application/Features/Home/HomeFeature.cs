using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Catalog;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Home;

public sealed record PopularServiceDto(string Name, string SubCategoryName, string SubCategorySlug, string? ImageUrl, decimal StartingPrice,
    int ProviderCount, int BookingCount);

public sealed record ReviewHighlightDto(Guid Id, byte Rating, string? Title, string Comment, string CustomerName, string BusinessName,
    string BusinessSlug, string? BusinessLogoUrl, string City, DateTimeOffset CreatedOn);

public sealed record PlatformStatsDto(int Businesses, int Cities, int Reviews, int BookingsCompleted, int OnlineNow);

public sealed record HomeDto(
    string? CitySlug,
    string? CityName,
    IReadOnlyList<BannerDto> HeroBanners,
    IReadOnlyList<BannerDto> PromoBanners,
    IReadOnlyList<SubCategoryDto> Categories,
    IReadOnlyList<PopularServiceDto> PopularServices,
    IReadOnlyList<BusinessCardDto> Nearby,
    IReadOnlyList<BusinessCardDto> OnlineNow,
    IReadOnlyList<BusinessCardDto> Featured,
    IReadOnlyList<BusinessCardDto> Sponsored,
    IReadOnlyList<ReviewHighlightDto> RecentReviews,
    IReadOnlyList<BusinessCardDto> TopRated,
    PlatformStatsDto Stats);

public sealed record GetHomeQuery(string? CitySlug) : IRequest<HomeDto>;

public sealed class GetHomeHandler(IUnitOfWork uow) : IRequestHandler<GetHomeQuery, HomeDto>
{
    private static readonly string[] OfflineStates = [AvailabilityStatuses.Offline, AvailabilityStatuses.Busy];

    public async Task<HomeDto> Handle(GetHomeQuery request, CancellationToken ct)
    {
        var now = IndianTime.Now;
        var today = now.Date;
        var card = BusinessCards.ToCard(now);

        var city = string.IsNullOrWhiteSpace(request.CitySlug)
            ? null
            : await uow.Repository<City>().QueryNoTracking().Where(c => c.Slug == request.CitySlug && c.IsActive)
                .Select(c => new { c.Id, c.Name, c.Slug }).FirstOrDefaultAsync(ct);

        var cityId = city?.Id;
        var listed = uow.Repository<Business>().QueryNoTracking().Listed();
        var inCity = cityId is null ? listed : listed.Where(b => b.CityId == cityId);

        var hero = await GetBannersHandler.ActiveBanners(uow, "HomeHero").ToListAsync(ct);
        var promo = await GetBannersHandler.ActiveBanners(uow, "HomeMid").ToListAsync(ct);
        var categories = await CatalogQueries.FeaturedSubCategories(uow).ToListAsync(ct);

        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var popularServices = await uow.Repository<Booking>().QueryNoTracking()
            .Where(b => b.CreatedOn >= since && b.Business.SubCategory != null && b.Service.Price > 0)
            .GroupBy(b => new { b.Service.Name, Sub = b.Business.SubCategory!.Name, b.Business.SubCategory.Slug, b.Business.SubCategory.ImageUrl })
            .Select(g => new
            {
                g.Key.Name, g.Key.Sub, g.Key.Slug, g.Key.ImageUrl,
                MinPrice = g.Min(x => x.Service.Price), Providers = g.Select(x => x.BusinessId).Distinct().Count(), Bookings = g.Count()
            })
            .OrderByDescending(s => s.Bookings)
            .Take(8)
            .Select(s => new PopularServiceDto(s.Name, s.Sub, s.Slug, s.ImageUrl, s.MinPrice, s.Providers, s.Bookings))
            .ToListAsync(ct);

        var nearby = await inCity
            .OrderByDescending(b => b.IsFeatured).ThenByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount)
            .Take(8).Select(card).ToListAsync(ct);

        var online = await inCity
            .Where(b => !OfflineStates.Contains(b.AvailabilityStatus))
            .OrderByDescending(b => b.LastSeenOn)
            .Take(8).Select(card).ToListAsync(ct);

        var featured = await listed
            .Where(b => b.IsFeatured || b.Advertisements.Any(a => a.AdType == AdTypes.FeaturedListing && a.Status == AdStatuses.Active
                                                                  && a.StartDate <= today && a.EndDate >= today))
            .OrderByDescending(b => cityId != null && b.CityId == cityId).ThenByDescending(b => b.AverageRating)
            .Take(8).Select(card).ToListAsync(ct);

        var sponsored = await listed
            .Where(b => b.Advertisements.Any(a => a.AdType == AdTypes.SponsoredListing && a.Status == AdStatuses.Active
                                                  && a.StartDate <= today && a.EndDate >= today))
            .OrderByDescending(b => cityId != null && b.CityId == cityId).ThenBy(b => b.Name)
            .Take(6).Select(card).ToListAsync(ct);

        var recentReviews = await uow.Repository<Review>().QueryNoTracking()
            .Where(r => r.Status == ReviewStatuses.Published && r.Rating >= 4 && r.Comment.Length > 60 && r.Business.Status == BusinessStatuses.Active)
            .OrderByDescending(r => r.CreatedOn)
            .Take(6)
            .Select(r => new ReviewHighlightDto(r.Id, r.Rating, r.Title, r.Comment, r.Customer.DisplayName, r.Business.Name, r.Business.Slug,
                r.Business.LogoUrl, r.Business.City, r.CreatedOn))
            .ToListAsync(ct);

        var topRated = await inCity
            .Where(b => b.ReviewCount >= 8)
            .OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount)
            .Take(8).Select(card).ToListAsync(ct);

        var stats = new PlatformStatsDto(
            await listed.CountAsync(ct),
            await uow.Repository<City>().QueryNoTracking().CountAsync(c => c.IsActive, ct),
            await uow.Repository<Review>().QueryNoTracking().CountAsync(r => r.Status == ReviewStatuses.Published, ct),
            await uow.Repository<Booking>().QueryNoTracking().CountAsync(b => b.Status == BookingStatuses.Completed, ct),
            await listed.CountAsync(b => !OfflineStates.Contains(b.AvailabilityStatus), ct));

        return new HomeDto(city?.Slug, city?.Name, hero, promo, categories, popularServices, nearby, online, featured, sponsored,
            recentReviews, topRated, stats);
    }
}
