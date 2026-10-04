using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Content;

/// <summary>Live figures for the business referenced by a block (testimonials).</summary>
public sealed record ContentBusinessDto(string Name, string Slug, string City, string? LogoUrl, string? CategoryName, decimal AverageRating,
    int ReviewCount, bool IsVerified, int Leads, int Bookings, string? PlanName);

public sealed record ContentBlockDto(string Code, string Section, string? Eyebrow, string Title, string? Subtitle, string? Body, string? IconKey,
    string? ImageUrl, string? ThumbnailUrl, string? MobileImageUrl, string? DesktopImageUrl, string? AltText, string? VideoUrl,
    string? MediaCredit, string? MediaCreditUrl, string? CtaText, string? LinkUrl, ContentBusinessDto? Business);

/// <summary>Platform-wide proof points shown to prospective business owners.</summary>
public sealed record BusinessGrowthStatsDto(int ActiveBusinesses, int VerifiedBusinesses, int Cities, int Categories, int LeadsLast30Days,
    int BookingsCompleted, int Reviews, decimal AverageRating);

public sealed record MarketingPageDto(string PageKey, IReadOnlyList<ContentBlockDto> Blocks, BusinessGrowthStatsDto Stats);

public sealed record GetMarketingPageQuery(string PageKey) : IRequest<MarketingPageDto>;

public sealed class GetMarketingPageHandler(IUnitOfWork uow) : IRequestHandler<GetMarketingPageQuery, MarketingPageDto>
{
    public async Task<MarketingPageDto> Handle(GetMarketingPageQuery request, CancellationToken ct)
    {
        var blocks = await uow.Repository<MarketingContent>().QueryNoTracking()
            .Where(c => c.PageKey == request.PageKey && c.IsActive)
            .OrderBy(c => c.SectionKey).ThenBy(c => c.SortOrder)
            .Select(c => new ContentBlockDto(
                c.Code, c.SectionKey, c.Eyebrow, c.Title, c.Subtitle, c.Body, c.IconKey,
                c.ImageUrl, c.ThumbnailUrl, c.MobileImageUrl, c.DesktopImageUrl, c.AltText, c.VideoUrl,
                c.MediaCredit, c.MediaCreditUrl, c.CtaText, c.LinkUrl,
                c.Business == null || c.Business.Status != BusinessStatuses.Active ? null : new ContentBusinessDto(
                    c.Business.Name, c.Business.Slug, c.Business.City, c.Business.LogoUrl,
                    c.Business.SubCategory != null ? c.Business.SubCategory.Name : c.Business.Category.Name,
                    c.Business.AverageRating, c.Business.ReviewCount,
                    c.Business.VerificationStatus == VerificationStatuses.Verified,
                    c.Business.Enquiries.Count(), c.Business.Bookings.Count(),
                    c.Business.Subscriptions.Where(s => s.Status == SubscriptionStatuses.Active)
                        .OrderByDescending(s => s.StartDate).Select(s => s.Plan.Name).FirstOrDefault())))
            .ToListAsync(ct);

        if (blocks.Count == 0) throw new NotFoundException("Page", request.PageKey);

        var listed = uow.Repository<Business>().QueryNoTracking().Listed();
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var reviews = uow.Repository<Review>().QueryNoTracking().Where(r => r.Status == ReviewStatuses.Published);
        var averageRating = await reviews.AverageAsync(r => (double?)r.Rating, ct) ?? 0;

        var stats = new BusinessGrowthStatsDto(
            await listed.CountAsync(ct),
            await listed.CountAsync(b => b.VerificationStatus == VerificationStatuses.Verified, ct),
            // Cities Calling Bell operates in (curated), not every city in the imported country catalogue.
            await uow.Repository<City>().QueryNoTracking().CountAsync(c => c.IsActive && c.Source == null, ct),
            await uow.Repository<SubCategory>().QueryNoTracking().CountAsync(s => s.IsActive, ct),
            await uow.Repository<Enquiry>().QueryNoTracking().CountAsync(e => e.CreatedOn >= since, ct),
            await uow.Repository<Booking>().QueryNoTracking().CountAsync(b => b.Status == BookingStatuses.Completed, ct),
            await reviews.CountAsync(ct),
            Math.Round((decimal)averageRating, 1));

        return new MarketingPageDto(request.PageKey, blocks, stats);
    }
}
