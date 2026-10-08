using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CallingBell.Application.Features.Search;

/// <param name="SearchText">What to search for on Google Maps.</param>
/// <param name="CategorySlug">The category the entry belongs to (for its icon and colour), if any.</param>
/// <param name="SubCategorySlug">Set when the search is exactly a sub-category, so picking it opens that sub-category.</param>
/// <param name="Group">The category's name, shown under the label.</param>
/// <param name="SearchCount">Searches made on Explore nearby.</param>
public sealed record PopularSearchDto(string Code, string Label, string SearchText, string? CategorySlug, string? SubCategorySlug, string? Group,
    string? IconUrl, string? ColorHex, int SearchCount);

public sealed record GetPopularSearchesQuery(string? Country, int Limit) : IRequest<IReadOnlyList<PopularSearchDto>>;

public sealed class GetPopularSearchesValidator : AbstractValidator<GetPopularSearchesQuery>
{
    public GetPopularSearchesValidator()
    {
        RuleFor(x => x.Country).Matches("^[A-Za-z]{2}$").When(x => !string.IsNullOrEmpty(x.Country)).WithMessage("country must be a 2-letter ISO code.");
        RuleFor(x => x.Limit).InclusiveBetween(1, 60);
    }
}

/// <summary>
/// The active entries for every country plus the browsed country's own, most searched first (then the curated order). When an entry
/// exists for every country and for this one with the same search, the country's wins. Kept in memory for two minutes per country, so
/// the counts move without a query on every page view.
/// </summary>
public sealed class GetPopularSearchesHandler(IUnitOfWork uow, IMemoryCache cache) : IRequestHandler<GetPopularSearchesQuery, IReadOnlyList<PopularSearchDto>>
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private const int MaxRows = 60;

    public async Task<IReadOnlyList<PopularSearchDto>> Handle(GetPopularSearchesQuery r, CancellationToken ct)
    {
        var country = string.IsNullOrWhiteSpace(r.Country) ? null : r.Country.Trim().ToUpperInvariant();
        var all = await cache.GetOrCreateAsync($"popular-searches|{country}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Lifetime;
            var rows = await uow.Repository<PopularSearch>().QueryNoTracking()
                .Where(p => p.IsActive && (p.CountryCode == null || p.CountryCode == country)
                            && (p.SubCategoryId == null || p.SubCategory!.IsActive)
                            && (p.CategoryId == null || p.Category!.IsActive))
                .Select(p => new
                {
                    p.Code, p.Label, p.SearchText, p.CountryCode, p.SearchCount, p.SortOrder,
                    SubSlug = p.SubCategory == null ? null : p.SubCategory.Slug,
                    SubName = p.SubCategory == null ? null : p.SubCategory.Name,
                    SubIcon = p.SubCategory == null ? null : p.SubCategory.IconUrl,
                    // The category is the entry's own, else its sub-category's.
                    CatSlug = p.Category != null ? p.Category.Slug : p.SubCategory != null ? p.SubCategory.Category.Slug : null,
                    CatName = p.Category != null ? p.Category.Name : p.SubCategory != null ? p.SubCategory.Category.Name : null,
                    CatIcon = p.Category != null ? p.Category.IconUrl : p.SubCategory != null ? p.SubCategory.Category.IconUrl : null,
                    CatColor = p.Category != null ? p.Category.ColorHex : p.SubCategory != null ? p.SubCategory.Category.ColorHex : null,
                })
                .ToListAsync(ct);
            return (IReadOnlyList<PopularSearchDto>)rows
                .GroupBy(p => p.SearchText, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(p => p.CountryCode == null).First())
                .OrderByDescending(p => p.SearchCount).ThenBy(p => p.SortOrder).ThenBy(p => p.Label)
                .Take(MaxRows)
                .Select(p => new PopularSearchDto(p.Code, p.Label, p.SearchText, p.CatSlug,
                    string.Equals(p.SubName, p.SearchText, StringComparison.OrdinalIgnoreCase) ? p.SubSlug : null,
                    p.CatName, p.SubIcon ?? p.CatIcon, p.CatColor, p.SearchCount))
                .ToList();
        });
        return all!.Take(r.Limit).ToList();
    }
}
