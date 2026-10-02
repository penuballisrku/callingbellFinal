using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Common.Models;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Businesses;

// ===================== Search =====================

public sealed record SearchBusinessesQuery : PagedRequest, IRequest<SearchResultDto>
{
    public string? Q { get; init; }
    public string? Category { get; init; }
    public string? Sub { get; init; }
    public string? City { get; init; }
    public Guid? AreaId { get; init; }
    public decimal? MinRating { get; init; }
    /// <summary>Availability code, or "now" for any available state.</summary>
    public string? Availability { get; init; }
    public bool OpenNow { get; init; }
    public bool VerifiedOnly { get; init; }
    public bool HomeService { get; init; }
    public bool VideoConsultation { get; init; }
    public bool OnlineBooking { get; init; }
    public string? Plan { get; init; }
    /// <summary>relevance | rating | reviews | newest | distance | price</summary>
    public string? Sort { get; init; }
    public double? Lat { get; init; }
    public double? Lng { get; init; }
    public double? MaxDistanceKm { get; init; }
}

public sealed record AppliedFiltersDto(string? Q, string? CategorySlug, string? CategoryName, string? SubSlug, string? SubName,
    string? CitySlug, string? CityName, bool OpenNow, string? Availability);

public sealed record SearchResultDto(IReadOnlyList<BusinessCardDto> Items, PaginationMeta Pagination, AppliedFiltersDto Applied);

public sealed class SearchBusinessesHandler(IUnitOfWork uow) : IRequestHandler<SearchBusinessesQuery, SearchResultDto>
{
    private static readonly string[] Unavailable = [AvailabilityStatuses.Offline, AvailabilityStatuses.Busy];
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "near", "me", "nearby", "best", "top", "in", "at", "around", "for", "the", "a", "an", "good", "services", "service" };

    public async Task<SearchResultDto> Handle(SearchBusinessesQuery r, CancellationToken ct)
    {
        var now = IndianTime.Now;
        var today = now.Date;

        // Understand natural phrases like "Electricians near me", "Available doctors", "Salons open now", "Lawyers in Pune".
        var text = (r.Q ?? string.Empty).Trim();
        var openNow = r.OpenNow;
        var availability = r.Availability;
        var citySlug = r.City;
        if (text.Contains("open now", StringComparison.OrdinalIgnoreCase)) { openNow = true; text = text.Replace("open now", " ", StringComparison.OrdinalIgnoreCase); }

        var cities = await uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Name, c.Slug, c.Latitude, c.Longitude }).ToListAsync(ct);

        var terms = new List<string>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Equals("available", StringComparison.OrdinalIgnoreCase) || token.Equals("online", StringComparison.OrdinalIgnoreCase))
            {
                availability ??= "now";
                continue;
            }
            var cityMatch = cities.FirstOrDefault(c => c.Slug.Equals(token, StringComparison.OrdinalIgnoreCase) || c.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (cityMatch is not null && citySlug is null) { citySlug = cityMatch.Slug; continue; }
            if (!StopWords.Contains(token)) terms.Add(token);
        }

        var query = uow.Repository<Business>().QueryNoTracking().Listed();

        string? categoryName = null, subName = null, cityName = null;
        if (!string.IsNullOrWhiteSpace(r.Category))
        {
            categoryName = await uow.Repository<Category>().QueryNoTracking().Where(c => c.Slug == r.Category).Select(c => c.Name).FirstOrDefaultAsync(ct);
            query = query.Where(b => b.Category.Slug == r.Category);
        }
        if (!string.IsNullOrWhiteSpace(r.Sub))
        {
            subName = await uow.Repository<SubCategory>().QueryNoTracking().Where(s => s.Slug == r.Sub).Select(s => s.Name).FirstOrDefaultAsync(ct);
            query = query.Where(b => b.SubCategory != null && b.SubCategory.Slug == r.Sub);
        }
        var city = citySlug is null ? null : cities.FirstOrDefault(c => c.Slug == citySlug);
        if (city is not null)
        {
            cityName = city.Name;
            query = query.Where(b => b.CityId == city.Id);
        }
        if (r.AreaId is { } areaId) query = query.Where(b => b.AreaId == areaId);

        foreach (var term in terms)
        {
            // Accept simple plurals: "electricians" matches "Electrician".
            var t = term.Length > 4 && term.EndsWith('s') ? term[..^1] : term;
            query = query.Where(b => b.Name.Contains(t) || (b.Tagline != null && b.Tagline.Contains(t)) || b.Description.Contains(t)
                                     || b.Category.Name.Contains(t) || (b.SubCategory != null && b.SubCategory.Name.Contains(t))
                                     || (b.Area != null && b.Area.Contains(t))
                                     || b.Services.Any(s => s.IsActive && s.Name.Contains(t)));
        }

        if (r.MinRating is { } minRating) query = query.Where(b => b.AverageRating >= minRating);
        if (availability == "now") query = query.Where(b => !Unavailable.Contains(b.AvailabilityStatus));
        else if (!string.IsNullOrWhiteSpace(availability)) query = query.Where(b => b.AvailabilityStatus == availability || b.AvailabilityStatus == AvailabilityStatuses.Online);
        if (openNow) query = query.Where(BusinessCards.IsOpenAt(now));
        if (r.VerifiedOnly) query = query.Where(b => b.VerificationStatus == VerificationStatuses.Verified);
        if (r.HomeService) query = query.Where(b => b.OffersHomeService);
        if (r.VideoConsultation) query = query.Where(b => b.OffersVideoConsultation);
        if (r.OnlineBooking) query = query.Where(b => b.AcceptsOnlineBooking);
        if (!string.IsNullOrWhiteSpace(r.Plan))
        {
            query = query.Where(b => b.Subscriptions.Any(s => s.Plan.Code == r.Plan && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial)
                                                             && s.StartDate <= today && s.EndDate >= today));
        }

        BusinessCards.Origin? origin = r is { Lat: not null, Lng: not null } ? new(r.Lat.Value, r.Lng.Value) : null;
        if (origin is null && city is { Latitude: not null, Longitude: not null } && r.Sort == "distance")
        {
            origin = new((double)city.Latitude, (double)city.Longitude);
        }
        if (origin is not null && r.MaxDistanceKm is { } maxKm)
        {
            var (lat, lng) = (origin.Latitude, origin.Longitude);
            var cos = Math.Cos(lat * Math.PI / 180);
            var maxDeg = maxKm / 111.32;
            query = query.Where(b => b.Latitude != null && b.Longitude != null
                                     && Math.Pow((double)b.Latitude - lat, 2) + Math.Pow(((double)b.Longitude - lng) * cos, 2) <= maxDeg * maxDeg);
        }

        var total = await query.CountAsync(ct);

        var sponsored = BusinessCards.IsSponsoredOn(today);
        IOrderedQueryable<Business> ordered = r.Sort switch
        {
            "rating" => query.OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount),
            "reviews" => query.OrderByDescending(b => b.ReviewCount),
            "newest" => query.OrderByDescending(b => b.CreatedOn),
            "price" => query.OrderBy(b => b.Services.Where(s => s.IsActive && s.Price > 0).Min(s => (decimal?)s.Price) ?? decimal.MaxValue),
            "distance" when origin is not null => query.OrderBy(b =>
                Math.Pow((double)(b.Latitude ?? 0) - origin.Latitude, 2) + Math.Pow(((double)(b.Longitude ?? 0) - origin.Longitude) * Math.Cos(origin.Latitude * Math.PI / 180), 2)),
            // Relevance: paid placement first (clearly labelled "Sponsored" in the UI), then quality signals.
            _ => query.OrderByDescending(sponsored).ThenByDescending(b => b.IsFeatured)
                      .ThenByDescending(b => !Unavailable.Contains(b.AvailabilityStatus))
                      .ThenByDescending(b => b.AverageRating).ThenByDescending(b => b.ReviewCount)
        };

        var items = await ordered.ThenBy(b => b.Name)
            .Skip(r.Skip).Take(r.PageSize)
            .Select(BusinessCards.ToCard(now, origin))
            .ToListAsync(ct);

        var meta = PagedResult<BusinessCardDto>.Create(items, r.Page, r.PageSize, total).Meta;
        return new SearchResultDto(items, meta,
            new AppliedFiltersDto(string.Join(' ', terms), r.Category, categoryName, r.Sub, subName, city?.Slug, cityName, openNow, availability));
    }
}

// ===================== Detail =====================

public sealed record ServiceDto(Guid Id, string Name, string Description, decimal Price, string? PriceUnit, int DurationMinutes, string Type,
    string? ImageUrl, bool IsPopular);

public sealed record HoursDto(int DayOfWeek, string Day, string? Open, string? Close, bool IsClosed, bool IsToday);

public sealed record ImageDto(Guid Id, string ImageUrl, string? ThumbnailUrl, string? MobileImageUrl, string? DesktopImageUrl, string? AltText,
    string? Caption, bool IsPrimary);

public sealed record ReviewDto(Guid Id, byte Rating, string? Title, string Comment, string CustomerName, DateTimeOffset CreatedOn,
    string? OwnerReply, DateTimeOffset? RepliedOn, bool IsVerifiedVisit, int HelpfulCount);

public sealed record BusinessDetailDto
{
    public required BusinessCardDto Card { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? AddressLine { get; init; }
    public string? Landmark { get; init; }
    public string? Pincode { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? PhoneNumber { get; init; }
    public string? WhatsAppNumber { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public int? YearEstablished { get; init; }
    public int? TeamSize { get; init; }
    public string? Languages { get; init; }
    public DateTimeOffset? VerifiedOn { get; init; }
    public string? CategoryColor { get; init; }
    public string? CitySlug { get; init; }
    public string? PlanName { get; init; }
    public bool IsFavorite { get; init; }
    public IReadOnlyList<ServiceDto> Services { get; init; } = [];
    public IReadOnlyList<HoursDto> Hours { get; init; } = [];
    public IReadOnlyList<ImageDto> Images { get; init; } = [];
    public IReadOnlyDictionary<int, int> RatingBreakdown { get; init; } = new Dictionary<int, int>();
    public IReadOnlyList<ReviewDto> RecentReviews { get; init; } = [];
    public IReadOnlyList<BusinessCardDto> Similar { get; init; } = [];
}

public sealed record GetBusinessBySlugQuery(string Slug) : IRequest<BusinessDetailDto>;

public sealed class GetBusinessBySlugHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetBusinessBySlugQuery, BusinessDetailDto>
{
    public async Task<BusinessDetailDto> Handle(GetBusinessBySlugQuery request, CancellationToken ct)
    {
        var now = IndianTime.Now;
        var today = now.Date;
        var businesses = uow.Repository<Business>().QueryNoTracking();

        var card = await businesses.Listed().Where(b => b.Slug == request.Slug).Select(BusinessCards.ToCard(now)).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Business", request.Slug);

        var info = await businesses.Where(b => b.Id == card.Id).Select(b => new
        {
            b.Description, b.AddressLine, b.Landmark, b.Pincode, b.Latitude, b.Longitude, b.PhoneNumber, b.WhatsAppNumber, b.Email, b.Website,
            b.YearEstablished, b.TeamSize, b.Languages, b.VerifiedOn, CategoryColor = b.Category.ColorHex, CitySlug = b.CityRef != null ? b.CityRef.Slug : null,
            b.CategoryId, b.SubCategoryId, b.CityId,
            PlanName = b.Subscriptions.Where(s => (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today)
                .OrderByDescending(s => s.StartDate).Select(s => s.Plan.Name).FirstOrDefault()
        }).FirstAsync(ct);

        var services = await uow.Repository<BusinessService>().QueryNoTracking()
            .Where(s => s.BusinessId == card.Id && s.IsActive)
            .OrderByDescending(s => s.IsPopular).ThenBy(s => s.Price)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Description, s.Price, s.PriceUnit, s.DurationMinutes, s.Type, s.ImageUrl, s.IsPopular))
            .ToListAsync(ct);

        var hours = (await uow.Repository<BusinessHour>().QueryNoTracking().Where(h => h.BusinessId == card.Id).ToListAsync(ct))
            .OrderBy(h => (h.DayOfWeek + 6) % 7) // Monday first
            .Select(h => new HoursDto(h.DayOfWeek, ((DayOfWeek)h.DayOfWeek).ToString(), h.OpenTime?.ToString(@"hh\:mm"), h.CloseTime?.ToString(@"hh\:mm"),
                h.IsClosed, h.DayOfWeek == (byte)now.DayOfWeek))
            .ToList();

        var images = await uow.Repository<BusinessImage>().QueryNoTracking()
            .Where(i => i.BusinessId == card.Id).OrderBy(i => i.SortOrder)
            .Select(i => new ImageDto(i.Id, i.ImageUrl, i.ThumbnailUrl, i.MobileImageUrl, i.DesktopImageUrl, i.AltText, i.Caption, i.IsPrimary))
            .ToListAsync(ct);

        var published = uow.Repository<Review>().QueryNoTracking().Where(r => r.BusinessId == card.Id && r.Status == ReviewStatuses.Published);
        var breakdownRows = await published.GroupBy(r => r.Rating).Select(g => new { Rating = (int)g.Key, Count = g.Count() }).ToListAsync(ct);
        var breakdown = Enumerable.Range(1, 5).ToDictionary(i => i, i => breakdownRows.FirstOrDefault(x => x.Rating == i)?.Count ?? 0);
        var recent = await published.OrderByDescending(r => r.CreatedOn).Take(5).Select(ReviewQueries.ToDto).ToListAsync(ct);

        var similar = await businesses.Listed()
            .Where(b => b.Id != card.Id && b.SubCategoryId == info.SubCategoryId)
            .OrderByDescending(b => b.CityId == info.CityId).ThenByDescending(b => b.AverageRating)
            .Take(4).Select(BusinessCards.ToCard(now)).ToListAsync(ct);

        var isFavorite = user.UserId is { } uid && await uow.Repository<Favorite>().QueryNoTracking().AnyAsync(f => f.UserId == uid && f.BusinessId == card.Id, ct);

        return new BusinessDetailDto
        {
            Card = card,
            Description = info.Description, AddressLine = info.AddressLine, Landmark = info.Landmark, Pincode = info.Pincode,
            Latitude = info.Latitude, Longitude = info.Longitude, PhoneNumber = info.PhoneNumber, WhatsAppNumber = info.WhatsAppNumber,
            Email = info.Email, Website = info.Website, YearEstablished = info.YearEstablished, TeamSize = info.TeamSize, Languages = info.Languages,
            VerifiedOn = info.VerifiedOn, CategoryColor = info.CategoryColor, CitySlug = info.CitySlug, PlanName = info.PlanName,
            IsFavorite = isFavorite, Services = services, Hours = hours, Images = images, RatingBreakdown = breakdown,
            RecentReviews = recent, Similar = similar
        };
    }
}

// ===================== Reviews (public list) =====================

internal static class ReviewQueries
{
    public static readonly System.Linq.Expressions.Expression<Func<Review, ReviewDto>> ToDto = r =>
        new ReviewDto(r.Id, r.Rating, r.Title, r.Comment, r.Customer.DisplayName, r.CreatedOn, r.OwnerReply, r.RepliedOn, r.IsVerifiedVisit, r.HelpfulCount);
}

public sealed record GetBusinessReviewsQuery : PagedRequest, IRequest<PagedResult<ReviewDto>>
{
    public string Slug { get; init; } = string.Empty;
    public int? Rating { get; init; }
    public string? Sort { get; init; }
}

public sealed class GetBusinessReviewsHandler(IUnitOfWork uow) : IRequestHandler<GetBusinessReviewsQuery, PagedResult<ReviewDto>>
{
    public async Task<PagedResult<ReviewDto>> Handle(GetBusinessReviewsQuery r, CancellationToken ct)
    {
        var query = uow.Repository<Review>().QueryNoTracking()
            .Where(x => x.Business.Slug == r.Slug && x.Status == ReviewStatuses.Published);
        if (r.Rating is { } rating) query = query.Where(x => x.Rating == rating);

        query = r.Sort switch
        {
            "highest" => query.OrderByDescending(x => x.Rating).ThenByDescending(x => x.CreatedOn),
            "lowest" => query.OrderBy(x => x.Rating).ThenByDescending(x => x.CreatedOn),
            "helpful" => query.OrderByDescending(x => x.HelpfulCount).ThenByDescending(x => x.CreatedOn),
            _ => query.OrderByDescending(x => x.CreatedOn)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip(r.Skip).Take(r.PageSize).Select(ReviewQueries.ToDto).ToListAsync(ct);
        return PagedResult<ReviewDto>.Create(items, r.Page, r.PageSize, total);
    }
}

// ===================== Slots =====================

public sealed record SlotDto(string Time, DateTimeOffset Start, bool Available);

public sealed record GetAvailableSlotsQuery(Guid BusinessId, Guid ServiceId, DateTime Date) : IRequest<IReadOnlyList<SlotDto>>;

public sealed class GetAvailableSlotsHandler(IUnitOfWork uow) : IRequestHandler<GetAvailableSlotsQuery, IReadOnlyList<SlotDto>>
{
    public Task<IReadOnlyList<SlotDto>> Handle(GetAvailableSlotsQuery request, CancellationToken ct) =>
        SlotCalculator.GetSlotsAsync(uow, request.BusinessId, request.ServiceId, request.Date, ct);
}

public static class SlotCalculator
{
    private static readonly string[] Blocking = [BookingStatuses.Pending, BookingStatuses.Confirmed];

    public static async Task<IReadOnlyList<SlotDto>> GetSlotsAsync(IUnitOfWork uow, Guid businessId, Guid serviceId, DateTime date, CancellationToken ct)
    {
        var business = await uow.Repository<Business>().QueryNoTracking()
            .Where(b => b.Id == businessId && b.Status == BusinessStatuses.Active)
            .Select(b => new { b.AcceptsOnlineBooking, b.TeamSize })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Business", businessId);
        if (!business.AcceptsOnlineBooking) return [];

        var service = await uow.Repository<BusinessService>().QueryNoTracking()
            .Where(s => s.Id == serviceId && s.BusinessId == businessId && s.IsActive)
            .Select(s => new { s.DurationMinutes })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Service", serviceId);

        var day = (byte)date.DayOfWeek;
        var hours = await uow.Repository<BusinessHour>().QueryNoTracking()
            .FirstOrDefaultAsync(h => h.BusinessId == businessId && h.DayOfWeek == day, ct);
        if (hours is null || hours.IsClosed || hours.OpenTime is null || hours.CloseTime is null) return [];

        // 24x7 businesses still take bookings only during sensible hours.
        var open = hours.OpenTime.Value < TimeSpan.FromHours(7) ? TimeSpan.FromHours(7) : hours.OpenTime.Value;
        var close = hours.CloseTime.Value > TimeSpan.FromHours(22) ? TimeSpan.FromHours(22) : hours.CloseTime.Value;
        var duration = TimeSpan.FromMinutes(Math.Clamp(service.DurationMinutes, 15, 240));
        var capacity = Math.Clamp((business.TeamSize ?? 1) / 3, 1, 5);

        var dayStart = IndianTime.At(date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var booked = await uow.Repository<Booking>().QueryNoTracking()
            .Where(b => b.BusinessId == businessId && Blocking.Contains(b.Status) && b.ScheduledStart < dayEnd && b.ScheduledEnd > dayStart)
            .Select(b => new { b.ScheduledStart, b.ScheduledEnd })
            .ToListAsync(ct);

        var earliest = IndianTime.Now.AddMinutes(60);
        var slots = new List<SlotDto>();
        for (var t = open; t + duration <= close; t += TimeSpan.FromMinutes(30))
        {
            var start = IndianTime.At(date, t);
            var end = start + duration;
            var overlapping = booked.Count(b => b.ScheduledStart < end && b.ScheduledEnd > start);
            slots.Add(new SlotDto(t.ToString(@"hh\:mm"), start, start >= earliest && overlapping < capacity));
        }
        return slots;
    }
}
