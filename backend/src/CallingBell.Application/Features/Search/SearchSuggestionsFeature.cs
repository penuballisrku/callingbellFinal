using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Search;

/// <summary>
/// One autocomplete suggestion. <see cref="Kind"/> is Category, SubCategory, Service or Business; the client builds the link from
/// <see cref="Slug"/> (category / sub-category / business slug) and, for services, <see cref="SubCategorySlug"/>.
/// </summary>
public sealed record SearchSuggestionDto(string Kind, string Label, string? Detail, string? ImageUrl, string Slug, string? SubCategorySlug, decimal? Rating);

public sealed record SearchSuggestionsDto(string Query, IReadOnlyList<SearchSuggestionDto> Categories,
    IReadOnlyList<SearchSuggestionDto> Services, IReadOnlyList<SearchSuggestionDto> Businesses);

public sealed record GetSearchSuggestionsQuery(string? Q, string? City) : IRequest<SearchSuggestionsDto>;

public sealed class GetSearchSuggestionsHandler(IUnitOfWork uow) : IRequestHandler<GetSearchSuggestionsQuery, SearchSuggestionsDto>
{
    public const int MinLength = 3;
    private const int MaxLength = 60;

    public async Task<SearchSuggestionsDto> Handle(GetSearchSuggestionsQuery request, CancellationToken ct)
    {
        var q = (request.Q ?? string.Empty).Trim();
        if (q.Length > MaxLength) q = q[..MaxLength];
        if (q.Length < MinLength) return new SearchSuggestionsDto(q, [], [], []);
        // Accept simple plurals, as the main search does: "electricians" matches "Electrician".
        var t = q.Length > 4 && q.EndsWith('s') ? q[..^1] : q;

        var categories = await uow.Repository<Category>().QueryNoTracking()
            .Where(c => c.IsActive && c.Name.Contains(t))
            .OrderByDescending(c => c.Name.StartsWith(t)).ThenBy(c => c.SortOrder)
            .Take(2)
            .Select(c => new SearchSuggestionDto("Category", c.Name, "Category", c.IconUrl, c.Slug, null, null))
            .ToListAsync(ct);

        var subCategories = await uow.Repository<SubCategory>().QueryNoTracking()
            .Where(s => s.IsActive && s.Category.IsActive && (s.Name.Contains(t) || (s.Description != null && s.Description.Contains(t))))
            .Select(s => new { s.Name, s.Slug, s.IconUrl, Cat = s.Category.Name, NameHit = s.Name.Contains(t), Starts = s.Name.StartsWith(t),
                               Count = s.Businesses.Count(b => b.Status == Domain.Constants.BusinessStatuses.Active) })
            .OrderByDescending(s => s.Starts).ThenByDescending(s => s.NameHit).ThenByDescending(s => s.Count)
            .Take(5 - categories.Count)
            .ToListAsync(ct);

        var services = await uow.Repository<BusinessService>().QueryNoTracking()
            .Where(s => s.IsActive && s.Business.Status == Domain.Constants.BusinessStatuses.Active && s.Business.SubCategory != null
                        && (s.Name.StartsWith(t) || s.Name.Contains(" " + t)))
            .GroupBy(s => new { s.Name, Sub = s.Business.SubCategory!.Name, SubSlug = s.Business.SubCategory.Slug, s.Business.SubCategory.IconUrl })
            .Select(g => new { g.Key, Starts = g.Key.Name.StartsWith(t), Count = g.Count() })
            .OrderByDescending(s => s.Starts).ThenByDescending(s => s.Count).ThenBy(s => s.Key.Name)
            .Take(5)
            .ToListAsync(ct);

        var businesses = await uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => b.Name.StartsWith(t) || b.Name.Contains(" " + t))
            .OrderByDescending(b => request.City != null && b.CityRef != null && b.CityRef.Slug == request.City)
            .ThenByDescending(b => b.Name.StartsWith(t)).ThenByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount)
            .Take(5)
            .Select(b => new SearchSuggestionDto("Business", b.Name,
                b.Area != null ? b.Area + ", " + b.City : b.City, b.LogoUrl, b.Slug, null, b.ReviewCount > 0 ? b.AverageRating : null))
            .ToListAsync(ct);

        return new SearchSuggestionsDto(q,
            categories.Concat(subCategories.Select(s => new SearchSuggestionDto("SubCategory", s.Name, s.Cat, s.IconUrl, s.Slug, s.Slug, null))).ToList(),
            services.Select(s => new SearchSuggestionDto("Service", s.Key.Name, s.Key.Sub, s.Key.IconUrl, s.Key.SubSlug, s.Key.SubSlug, null)).ToList(),
            businesses);
    }
}

// ===================== Suggestions by meaning (local AI) =====================

/// <summary>Sub-categories the local AI matches by meaning ("water dripping from the ceiling" gives Plumbers).</summary>
public sealed record GetMeaningSuggestionsQuery(string? Q) : IRequest<IReadOnlyList<SearchSuggestionDto>>;

/// <summary>
/// The search box's "Suggested by AI" group. Separate from (and slower than) the name-based suggestions, so typing never waits for
/// the model: the client asks once the visitor pauses on a phrase. Only confident matches, plus close runners-up.
/// </summary>
public sealed class GetMeaningSuggestionsHandler(IUnitOfWork uow, ISemanticCatalog semantic)
    : IRequestHandler<GetMeaningSuggestionsQuery, IReadOnlyList<SearchSuggestionDto>>
{
    public const int MinLength = 8;
    private const int MaxLength = 120;

    public async Task<IReadOnlyList<SearchSuggestionDto>> Handle(GetMeaningSuggestionsQuery request, CancellationToken ct)
    {
        var q = (request.Q ?? string.Empty).Trim();
        if (q.Length > MaxLength) q = q[..MaxLength];
        // Phrases ("tap leaking", "water dripping from ceiling"), not single words, which the name-based suggestions cover.
        if (q.Length < MinLength || !q.Contains(' ')) return [];

        var result = await semantic.MatchAsync(q, 3, ct);
        if (result is not { Confident: true }) return [];
        var top = result.Top[0].Score;
        var slugs = result.Top.Where(m => top - m.Score <= 0.03).Select(m => m.SubCategorySlug).ToList();
        var subs = await uow.Repository<SubCategory>().QueryNoTracking().Where(s => slugs.Contains(s.Slug))
            .Select(s => new SearchSuggestionDto("SubCategory", s.Name, s.Category.Name, s.IconUrl, s.Slug, s.Slug, null))
            .ToDictionaryAsync(s => s.Slug, ct);
        return slugs.Where(subs.ContainsKey).Select(slug => subs[slug]).ToList(); // best match first
    }
}
