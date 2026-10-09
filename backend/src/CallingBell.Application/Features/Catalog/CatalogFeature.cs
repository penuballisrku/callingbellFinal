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
/// <param name="MonthlyPrice">In <paramref name="CurrencyCode"/>: the rupee price converted for the requested country (see <see cref="CountryPrice"/>).</param>
/// <param name="TaxName">Tax added at checkout (e.g. "GST"); null with <paramref name="TaxRate"/> 0 when none is charged.</param>
/// <param name="TaxRate">Fraction, e.g. 0.18.</param>
public sealed record PlanDto(Guid Id, string Code, string Name, string? Tagline, decimal MonthlyPrice, decimal AnnualPrice, int LeadCredits,
    bool IncludesFeaturedListing, bool IncludesPrioritySupport, IReadOnlyList<string> Features, string? ImageUrl, string? BadgeColor, bool IsPopular,
    int MaxServices, int MaxImages, string CurrencyCode = "INR", string Locale = "en-IN", string? TaxName = "GST", decimal TaxRate = 0.18m);
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
/// <param name="State">A state / province / region slug: only its cities (a few dozen instead of the whole country's thousands).</param>
public sealed record GetCitiesQuery(string? Country = null, string? State = null) : IRequest<IReadOnlyList<CityDto>>;

/// <summary>
/// The country's cities: curated cities first (in their curated order), then the rest largest first. Curated cities include their
/// top-level areas; for every other city the areas come from GET /api/locations/cities/{slug}/areas, which also queues their discovery.
/// </summary>
public sealed class GetCitiesHandler(IUnitOfWork uow, IGeoLocationService geo, ReferenceDataCache reference)
    : IRequestHandler<GetCitiesQuery, IReadOnlyList<CityDto>>
{
    public async Task<IReadOnlyList<CityDto>> Handle(GetCitiesQuery request, CancellationToken ct)
    {
        var country = request.Country is { Length: 2 } c2 && c2.All(char.IsAsciiLetter) ? c2.ToUpperInvariant() : geo.DefaultCountryCode;
        // Thousands of cities per country: built once and kept in memory (the city import and admin edits clear it). A state's cities are
        // their own cached list, so the location picker downloads only what it shows.
        var state = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim().ToLowerInvariant();
        return await reference.GetDerivedAsync($"cities|{country}|{state}", () => LoadAsync(country, state, ct), ct);
    }

    /// <summary>Three set-based queries (cities, business counts, curated cities' areas) instead of a sub-query per city.</summary>
    private async Task<IReadOnlyList<CityDto>> LoadAsync(string country, string? state, CancellationToken ct)
    {
        var query = uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive && c.State.CountryCode == country);
        if (state is not null) query = query.Where(c => c.State.Slug == state);
        var cities = await query
            .OrderBy(c => c.Source != null).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Slug, State = c.State.Name, c.ImageUrl, c.IsPopular, Curated = c.Source == null })
            .ToListAsync(ct);
        var cityIds = cities.Select(c => c.Id).ToList();
        var counts = await uow.Repository<Business>().QueryNoTracking()
            .Where(b => b.Status == BusinessStatuses.Active && b.CityId != null && (state == null || cityIds.Contains(b.CityId!.Value)))
            .GroupBy(b => b.CityId!.Value).Select(g => new { CityId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CityId, x => x.Count, ct);
        // Top-level areas of curated cities only; every other city's areas come with GET /api/locations/cities/{slug}/areas.
        var curatedIds = cities.Where(c => c.Curated).Select(c => c.Id).ToList();
        var areas = (await uow.Repository<Area>().QueryNoTracking()
                .Where(a => curatedIds.Contains(a.CityId) && a.IsActive && a.ParentAreaId == null)
                .OrderBy(a => a.Name)
                .Select(a => new { a.CityId, Dto = new AreaDto(a.Id, a.Name, a.Slug, a.Pincode) })
                .ToListAsync(ct))
            .ToLookup(a => a.CityId, a => a.Dto);

        return cities.Select(c => new CityDto(c.Id, c.Name, c.Slug, c.State, c.ImageUrl, c.IsPopular, counts.GetValueOrDefault(c.Id),
            c.Curated ? areas[c.Id].ToList() : [])).ToList();
    }
}

/// <param name="CityCount">Cities listed so far; 0 until the city catalogue agent has imported the country (choosing it queues that).</param>
public sealed record CountryDto(string Code, string Name, int CityCount);

/// <summary>Countries a visitor can browse: every country Calling Bell prices its plans for (CountryPricing), by name.</summary>
public sealed record GetCountriesQuery : IRequest<IReadOnlyList<CountryDto>>;

public sealed class GetCountriesHandler(IUnitOfWork uow) : IRequestHandler<GetCountriesQuery, IReadOnlyList<CountryDto>>
{
    /// <summary>CountryPricing's default row for countries without one of their own; not a country.</summary>
    private const string DefaultPricing = "ZZ";

    public async Task<IReadOnlyList<CountryDto>> Handle(GetCountriesQuery request, CancellationToken ct)
    {
        var countries = await uow.Repository<CountryPricing>().QueryNoTracking()
            .Where(p => p.IsActive && p.CountryCode != DefaultPricing)
            .Select(p => new { p.CountryCode, p.CountryName })
            .ToListAsync(ct);
        var cityCounts = await uow.Repository<City>().QueryNoTracking()
            .Where(c => c.IsActive)
            .GroupBy(c => c.State.CountryCode).Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Code, x => x.Count, ct);
        return countries
            .Select(c => new CountryDto(c.CountryCode, c.CountryName, cityCounts.GetValueOrDefault(c.CountryCode)))
            .OrderBy(c => c.Name)
            .ToList();
    }
}

/// <param name="Name">State, province or region ("Quebec", "England", "Andhra Pradesh").</param>
public sealed record StateDto(Guid Id, string Name, string Slug, int CityCount);

/// <summary>A country's states / provinces / regions that have listed cities, by name.</summary>
public sealed record GetStatesQuery(string Country) : IRequest<IReadOnlyList<StateDto>>;

public sealed class GetStatesHandler(IUnitOfWork uow) : IRequestHandler<GetStatesQuery, IReadOnlyList<StateDto>>
{
    public async Task<IReadOnlyList<StateDto>> Handle(GetStatesQuery request, CancellationToken ct)
    {
        var country = request.Country.Trim().ToUpperInvariant();
        return await uow.Repository<State>().QueryNoTracking()
            .Where(s => s.IsActive && s.CountryCode == country && s.Cities.Any(c => c.IsActive))
            .OrderBy(s => s.Name)
            .Select(s => new StateDto(s.Id, s.Name, s.Slug, s.Cities.Count(c => c.IsActive)))
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
/// <param name="Country">ISO country code: prices in that country's currency at fair local prices. Null: rupees as set (admin screens).</param>
/// <param name="BusinessId">Prices for the country this business is in (the owner portal); takes precedence over <paramref name="Country"/>.</param>
public sealed record GetPlansQuery(string? Country = null, Guid? BusinessId = null) : IRequest<IReadOnlyList<PlanDto>>;

public sealed class GetPlansHandler(IUnitOfWork uow, CountryPricingService pricing) : IRequestHandler<GetPlansQuery, IReadOnlyList<PlanDto>>
{
    public async Task<IReadOnlyList<PlanDto>> Handle(GetPlansQuery request, CancellationToken ct)
    {
        var plans = await uow.Repository<SubscriptionPlan>().QueryNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var country = request.BusinessId is { } id ? await pricing.ForBusinessAsync(id, ct) : await pricing.ForCountryAsync(request.Country, ct);
        return plans.Select(p => ToDto(p, country)).ToList();
    }

    /// <summary>A plan priced for <paramref name="country"/> (India, as set, when null).</summary>
    public static PlanDto ToDto(SubscriptionPlan p, CountryPrice? country = null)
    {
        var c = country ?? CountryPrice.India;
        return new(p.Id, p.Code, p.Name, p.Tagline, c.Convert(p.MonthlyPrice), c.Convert(p.AnnualPrice), p.LeadCredits,
            p.IncludesFeaturedListing, p.IncludesPrioritySupport,
            p.Features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), p.ImageUrl, p.BadgeColor, p.IsPopular,
            p.MaxServices, p.MaxImages, c.CurrencyCode, c.Locale, c.TaxName, c.TaxRate);
    }
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

// ---------- Service suggestions (business sign-up) ----------
/// <summary>A service businesses in a sub-category commonly list, with typical values to pre-fill a new listing's service.</summary>
/// <param name="TypicalPrice">Median price among the businesses offering it (rounded).</param>
/// <param name="BusinessCount">How many listed businesses offer it.</param>
public sealed record ServiceSuggestionDto(string Name, string? Description, decimal TypicalPrice, string? PriceUnit, int DurationMinutes, string Type,
    int BusinessCount);

public sealed record GetServiceSuggestionsQuery(string SubCategorySlug) : IRequest<IReadOnlyList<ServiceSuggestionDto>>;

/// <summary>
/// The services active listings in the sub-category offer, most common first (same name, any case, counts once per business), each with
/// the median price and the most common duration, unit and delivery type. Straight from dbo.BusinessServices: nothing is hard-coded.
/// </summary>
public sealed class GetServiceSuggestionsHandler(IUnitOfWork uow) : IRequestHandler<GetServiceSuggestionsQuery, IReadOnlyList<ServiceSuggestionDto>>
{
    private const int MaxSuggestions = 40;

    public async Task<IReadOnlyList<ServiceSuggestionDto>> Handle(GetServiceSuggestionsQuery r, CancellationToken ct)
    {
        var slug = r.SubCategorySlug.Trim();
        var rows = await uow.Repository<BusinessService>().QueryNoTracking()
            .Where(s => s.IsActive && s.Business.Status == BusinessStatuses.Active && s.Business.SubCategory != null && s.Business.SubCategory.Slug == slug)
            .Select(s => new { s.BusinessId, s.Name, s.Description, s.Price, s.PriceUnit, s.DurationMinutes, s.Type })
            .Take(2000)
            .ToListAsync(ct);

        static T Mode<T>(IEnumerable<T> values) => values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
        return rows
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var prices = g.Select(s => s.Price).Order().ToList();
                var median = prices[prices.Count / 2];
                var described = g.Select(s => s.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
                return new ServiceSuggestionDto(Mode(g.Select(s => s.Name.Trim())), described,
                    median >= 100 ? Math.Round(median / 10) * 10 : Math.Round(median), Mode(g.Select(s => s.PriceUnit)),
                    Mode(g.Select(s => s.DurationMinutes)), Mode(g.Select(s => s.Type)), g.Select(s => s.BusinessId).Distinct().Count());
            })
            .OrderByDescending(s => s.BusinessCount).ThenBy(s => s.Name)
            .Take(MaxSuggestions)
            .ToList();
    }
}
