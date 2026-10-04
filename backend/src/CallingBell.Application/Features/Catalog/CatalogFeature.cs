using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Catalog;

public sealed record SubCategoryDto(Guid Id, string Name, string Slug, string? Description, string? ImageUrl, string? IconUrl, string? AltText,
    bool IsFeatured, int BusinessCount, string CategorySlug, string CategoryName, string? ColorHex);

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Description, string? ImageUrl, string? IconUrl, string? BannerUrl,
    string? AltText, string? ColorHex, bool IsFeatured, int BusinessCount, IReadOnlyList<SubCategoryDto> SubCategories);

public sealed record AreaDto(Guid Id, string Name, string Slug, string Pincode);
public sealed record CityDto(Guid Id, string Name, string Slug, string State, string? ImageUrl, bool IsPopular, int BusinessCount, IReadOnlyList<AreaDto> Areas);
public sealed record LookupDto(string Code, string Name, string? Description, string? ColorHex, int SortOrder);
public sealed record PlanDto(Guid Id, string Code, string Name, string? Tagline, decimal MonthlyPrice, decimal AnnualPrice, int LeadCredits,
    bool IncludesFeaturedListing, bool IncludesPrioritySupport, IReadOnlyList<string> Features, string? ImageUrl, string? BadgeColor, bool IsPopular,
    int MaxServices, int MaxImages);
public sealed record BannerDto(Guid Id, string Title, string? Subtitle, string? CtaText, string? LinkUrl, string ImageUrl,
    string? MobileImageUrl, string? DesktopImageUrl, string? AltText, string Placement);

// ---------- Categories ----------
public sealed record GetCategoriesQuery : IRequest<IReadOnlyList<CategoryDto>>;

public sealed class GetCategoriesHandler(IUnitOfWork uow) : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery request, CancellationToken ct) =>
        await CatalogQueries.CategoryTree(uow).ToListAsync(ct);
}

public sealed record GetCategoryBySlugQuery(string Slug) : IRequest<CategoryDto>;

public sealed class GetCategoryBySlugHandler(IUnitOfWork uow) : IRequestHandler<GetCategoryBySlugQuery, CategoryDto>
{
    public async Task<CategoryDto> Handle(GetCategoryBySlugQuery request, CancellationToken ct) =>
        await CatalogQueries.CategoryTree(uow).FirstOrDefaultAsync(c => c.Slug == request.Slug, ct)
        ?? throw new NotFoundException("Category", request.Slug);
}

public sealed record GetFeaturedSubCategoriesQuery : IRequest<IReadOnlyList<SubCategoryDto>>;

public sealed class GetFeaturedSubCategoriesHandler(IUnitOfWork uow) : IRequestHandler<GetFeaturedSubCategoriesQuery, IReadOnlyList<SubCategoryDto>>
{
    public async Task<IReadOnlyList<SubCategoryDto>> Handle(GetFeaturedSubCategoriesQuery request, CancellationToken ct) =>
        await CatalogQueries.FeaturedSubCategories(uow).ToListAsync(ct);
}

internal static class CatalogQueries
{
    public static IQueryable<CategoryDto> CategoryTree(IUnitOfWork uow) =>
        uow.Repository<Category>().QueryNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.IconUrl, c.BannerUrl, c.AltText, c.ColorHex, c.IsFeatured,
                c.Businesses.Count(b => b.Status == BusinessStatuses.Active),
                c.SubCategories.Where(s => s.IsActive).OrderBy(s => s.SortOrder)
                    .Select(s => new SubCategoryDto(s.Id, s.Name, s.Slug, s.Description, s.ImageUrl, s.IconUrl, s.AltText, s.IsFeatured,
                        s.Businesses.Count(b => b.Status == BusinessStatuses.Active), c.Slug, c.Name, c.ColorHex))
                    .ToList()));

    public static IQueryable<SubCategoryDto> FeaturedSubCategories(IUnitOfWork uow) =>
        uow.Repository<SubCategory>().QueryNoTracking()
            .Where(s => s.IsActive && s.IsFeatured && s.Category.IsActive)
            .OrderBy(s => s.Category.SortOrder).ThenBy(s => s.SortOrder)
            .Select(s => new SubCategoryDto(s.Id, s.Name, s.Slug, s.Description, s.ImageUrl, s.IconUrl, s.AltText, s.IsFeatured,
                s.Businesses.Count(b => b.Status == BusinessStatuses.Active), s.Category.Slug, s.Category.Name, s.Category.ColorHex));
}

// ---------- Locations ----------
/// <param name="Country">ISO 3166-1 alpha-2 code; the default country when omitted. The city catalogue agent fills each country's cities.</param>
public sealed record GetCitiesQuery(string? Country = null) : IRequest<IReadOnlyList<CityDto>>;

/// <summary>
/// The country's cities: curated cities first (in their curated order), then the rest largest first. Curated cities include their
/// top-level areas; for every other city the areas come from GET /api/locations/cities/{slug}/areas, which also queues their discovery.
/// </summary>
public sealed class GetCitiesHandler(IUnitOfWork uow, IGeoLocationService geo) : IRequestHandler<GetCitiesQuery, IReadOnlyList<CityDto>>
{
    public async Task<IReadOnlyList<CityDto>> Handle(GetCitiesQuery request, CancellationToken ct)
    {
        var country = request.Country is { Length: 2 } c2 && c2.All(char.IsAsciiLetter) ? c2.ToUpperInvariant() : geo.DefaultCountryCode;
        var businesses = uow.Repository<Business>().QueryNoTracking();
        return await uow.Repository<City>().QueryNoTracking()
            .Where(c => c.IsActive && c.State.CountryCode == country)
            .OrderBy(c => c.Source != null).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CityDto(c.Id, c.Name, c.Slug, c.State.Name, c.ImageUrl, c.IsPopular,
                businesses.Count(b => b.CityId == c.Id && b.Status == BusinessStatuses.Active),
                // Top-level areas only; sub-localities come with GET /api/locations/cities/{slug}/areas.
                c.Source == null
                    ? c.Areas.Where(a => a.IsActive && a.ParentAreaId == null).OrderBy(a => a.Name).Select(a => new AreaDto(a.Id, a.Name, a.Slug, a.Pincode)).ToList()
                    : new List<AreaDto>()))
            .ToListAsync(ct);
    }
}

// ---------- Lookups ----------
public sealed record GetLookupsQuery : IRequest<IReadOnlyDictionary<string, IReadOnlyList<LookupDto>>>;

public sealed class GetLookupsHandler(IUnitOfWork uow) : IRequestHandler<GetLookupsQuery, IReadOnlyDictionary<string, IReadOnlyList<LookupDto>>>
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<LookupDto>>> Handle(GetLookupsQuery request, CancellationToken ct)
    {
        var rows = await uow.Repository<LookupValue>().QueryNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.LookupType).ThenBy(l => l.SortOrder)
            .Select(l => new { l.LookupType, Dto = new LookupDto(l.Code, l.Name, l.Description, l.ColorHex, l.SortOrder) })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.LookupType)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LookupDto>)g.Select(r => r.Dto).ToList());
    }
}

// ---------- Plans ----------
public sealed record GetPlansQuery : IRequest<IReadOnlyList<PlanDto>>;

public sealed class GetPlansHandler(IUnitOfWork uow) : IRequestHandler<GetPlansQuery, IReadOnlyList<PlanDto>>
{
    public async Task<IReadOnlyList<PlanDto>> Handle(GetPlansQuery request, CancellationToken ct)
    {
        var plans = await uow.Repository<SubscriptionPlan>().QueryNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct);
        return plans.Select(ToDto).ToList();
    }

    public static PlanDto ToDto(SubscriptionPlan p) => new(p.Id, p.Code, p.Name, p.Tagline, p.MonthlyPrice, p.AnnualPrice, p.LeadCredits,
        p.IncludesFeaturedListing, p.IncludesPrioritySupport,
        p.Features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), p.ImageUrl, p.BadgeColor, p.IsPopular,
        p.MaxServices, p.MaxImages);
}

// ---------- Banners ----------
public sealed record GetBannersQuery(string Placement) : IRequest<IReadOnlyList<BannerDto>>;

public sealed class GetBannersHandler(IUnitOfWork uow) : IRequestHandler<GetBannersQuery, IReadOnlyList<BannerDto>>
{
    public async Task<IReadOnlyList<BannerDto>> Handle(GetBannersQuery request, CancellationToken ct) =>
        await ActiveBanners(uow, request.Placement).ToListAsync(ct);

    public static IQueryable<BannerDto> ActiveBanners(IUnitOfWork uow, string placement)
    {
        var today = IndianTime.Today;
        return uow.Repository<Banner>().QueryNoTracking()
            .Where(b => b.IsActive && b.Placement == placement
                        && (b.StartsOn == null || b.StartsOn <= today) && (b.EndsOn == null || b.EndsOn >= today))
            .OrderBy(b => b.SortOrder)
            .Select(b => new BannerDto(b.Id, b.Title, b.Subtitle, b.CtaText, b.LinkUrl, b.ImageUrl, b.MobileImageUrl, b.DesktopImageUrl, b.AltText, b.Placement));
    }
}
