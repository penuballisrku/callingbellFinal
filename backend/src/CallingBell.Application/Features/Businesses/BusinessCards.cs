using System.Linq.Expressions;
using CallingBell.Application.Common;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;

namespace CallingBell.Application.Features.Businesses;

public sealed record BusinessCardDto
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Tagline { get; init; }
    public string? LogoUrl { get; init; }
    public string? CoverImageUrl { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string CategorySlug { get; init; } = string.Empty;
    public string? SubCategoryName { get; init; }
    public string? SubCategorySlug { get; init; }
    public string City { get; init; } = string.Empty;
    public string? Area { get; init; }
    public decimal AverageRating { get; init; }
    public int ReviewCount { get; init; }
    public string AvailabilityStatus { get; init; } = string.Empty;
    public DateTimeOffset? LastSeenOn { get; init; }
    public bool IsVerified { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsSponsored { get; init; }
    public decimal? StartingPrice { get; init; }
    public bool AcceptsOnlineBooking { get; init; }
    public bool OffersVideoConsultation { get; init; }
    public bool OffersHomeService { get; init; }
    public bool IsOpenNow { get; init; }
    public int? ResponseTimeMinutes { get; init; }
    public string? PlanCode { get; init; }
    public double? DistanceKm { get; init; }
}

/// <summary>Reusable, SQL-translatable projections and predicates for business listings.</summary>
public static class BusinessCards
{
    public sealed record Origin(double Latitude, double Longitude);

    public static IQueryable<Business> Listed(this IQueryable<Business> query) =>
        query.Where(b => b.Status == BusinessStatuses.Active);

    public static Expression<Func<Business, bool>> IsOpenAt(DateTimeOffset istNow)
    {
        var day = (byte)istNow.DayOfWeek;
        var time = istNow.TimeOfDay;
        return b => b.Hours.Any(h => h.DayOfWeek == day && !h.IsClosed && h.OpenTime <= time && h.CloseTime > time);
    }

    public static Expression<Func<Business, bool>> IsSponsoredOn(DateTime today) =>
        b => b.Advertisements.Any(a => a.Status == AdStatuses.Active
                                       && (a.AdType == AdTypes.SponsoredListing || a.AdType == AdTypes.SearchPromotion)
                                       && a.StartDate <= today && a.EndDate >= today);

    public static Expression<Func<Business, BusinessCardDto>> ToCard(DateTimeOffset istNow, Origin? origin = null)
    {
        var today = istNow.Date;
        var day = (byte)istNow.DayOfWeek;
        var time = istNow.TimeOfDay;
        var hasOrigin = origin is not null;
        var lat = origin?.Latitude ?? 0;
        var lng = origin?.Longitude ?? 0;
        var cosLat = Math.Cos(lat * Math.PI / 180);

        return b => new BusinessCardDto
        {
            Id = b.Id,
            Slug = b.Slug,
            Name = b.Name,
            Tagline = b.Tagline,
            LogoUrl = b.LogoUrl,
            CoverImageUrl = b.CoverImageUrl,
            CategoryName = b.Category.Name,
            CategorySlug = b.Category.Slug,
            SubCategoryName = b.SubCategory != null ? b.SubCategory.Name : null,
            SubCategorySlug = b.SubCategory != null ? b.SubCategory.Slug : null,
            City = b.City,
            Area = b.Area,
            AverageRating = b.AverageRating,
            ReviewCount = b.ReviewCount,
            AvailabilityStatus = b.AvailabilityStatus,
            LastSeenOn = b.LastSeenOn,
            IsVerified = b.VerificationStatus == VerificationStatuses.Verified,
            IsFeatured = b.IsFeatured,
            IsSponsored = b.Advertisements.Any(a => a.Status == AdStatuses.Active
                                                    && (a.AdType == AdTypes.SponsoredListing || a.AdType == AdTypes.SearchPromotion)
                                                    && a.StartDate <= today && a.EndDate >= today),
            StartingPrice = b.Services.Where(s => s.IsActive && s.Price > 0).Min(s => (decimal?)s.Price),
            AcceptsOnlineBooking = b.AcceptsOnlineBooking,
            OffersVideoConsultation = b.OffersVideoConsultation,
            OffersHomeService = b.OffersHomeService,
            IsOpenNow = b.Hours.Any(h => h.DayOfWeek == day && !h.IsClosed && h.OpenTime <= time && h.CloseTime > time),
            ResponseTimeMinutes = b.ResponseTimeMinutes,
            PlanCode = b.Subscriptions
                .Where(s => (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today)
                .OrderByDescending(s => s.StartDate)
                .Select(s => s.Plan.Code)
                .FirstOrDefault(),
            DistanceKm = hasOrigin && b.Latitude != null && b.Longitude != null
                ? Math.Sqrt(Math.Pow((double)b.Latitude - lat, 2) + Math.Pow(((double)b.Longitude - lng) * cosLat, 2)) * 111.32
                : null
        };
    }

    public static DateTimeOffset Now => IndianTime.Now;
}
