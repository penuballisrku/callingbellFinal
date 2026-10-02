using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Owner;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Admin;

// ===================== Businesses =====================

public sealed record AdminBusinessRowDto(Guid Id, string Name, string Slug, string? LogoUrl, string CategoryName, string? SubCategoryName, string City,
    string? Area, string OwnerName, string? OwnerEmail, string? PhoneNumber, string Status, string VerificationStatus, bool IsFeatured,
    string? PlanCode, decimal AverageRating, int ReviewCount, int Leads30, int Bookings30, int LiveAds, string AvailabilityStatus, DateTimeOffset CreatedOn);

public sealed record GetAdminBusinessesQuery : PagedRequest, IRequest<PagedResult<AdminBusinessRowDto>>
{
    public string? Q { get; init; }
    public string? Category { get; init; }
    public string? City { get; init; }
    public string? Status { get; init; }
    public string? Verification { get; init; }
    public string? Plan { get; init; }
    public bool? Featured { get; init; }
    /// <summary>name | rating | newest | leads | reviews (prefix with "-" for descending)</summary>
    public string? Sort { get; init; }
}

public sealed class GetAdminBusinessesHandler(IUnitOfWork uow) : IRequestHandler<GetAdminBusinessesQuery, PagedResult<AdminBusinessRowDto>>
{
    public async Task<PagedResult<AdminBusinessRowDto>> Handle(GetAdminBusinessesQuery r, CancellationToken ct)
    {
        var today = IndianTime.Today;
        var d30 = DateTimeOffset.UtcNow.AddDays(-30);
        var query = uow.Repository<Business>().QueryNoTracking();

        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var q = r.Q.Trim();
            query = query.Where(b => b.Name.Contains(q) || b.Owner.DisplayName.Contains(q) || (b.Owner.Email != null && b.Owner.Email.Contains(q))
                                     || (b.PhoneNumber != null && b.PhoneNumber.Contains(q)) || (b.Area != null && b.Area.Contains(q)));
        }
        if (!string.IsNullOrWhiteSpace(r.Category)) query = query.Where(b => b.Category.Slug == r.Category);
        if (!string.IsNullOrWhiteSpace(r.City)) query = query.Where(b => b.CityRef != null && b.CityRef.Slug == r.City);
        if (!string.IsNullOrWhiteSpace(r.Status)) query = query.Where(b => b.Status == r.Status);
        if (!string.IsNullOrWhiteSpace(r.Verification)) query = query.Where(b => b.VerificationStatus == r.Verification);
        if (r.Featured is { } featured) query = query.Where(b => b.IsFeatured == featured);
        if (!string.IsNullOrWhiteSpace(r.Plan))
        {
            query = query.Where(b => b.Subscriptions.Any(s => s.Plan.Code == r.Plan && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial)
                                                             && s.StartDate <= today && s.EndDate >= today));
        }

        var total = await query.CountAsync(ct);
        var desc = r.Sort?.StartsWith('-') == true;
        query = (r.Sort?.TrimStart('-')) switch
        {
            "rating" => desc ? query.OrderByDescending(b => b.AverageRating) : query.OrderBy(b => b.AverageRating),
            "reviews" => desc ? query.OrderByDescending(b => b.ReviewCount) : query.OrderBy(b => b.ReviewCount),
            "leads" => desc ? query.OrderByDescending(b => b.Enquiries.Count(e => e.CreatedOn >= d30)) : query.OrderBy(b => b.Enquiries.Count(e => e.CreatedOn >= d30)),
            "newest" => desc ? query.OrderByDescending(b => b.CreatedOn) : query.OrderBy(b => b.CreatedOn),
            "city" => desc ? query.OrderByDescending(b => b.City) : query.OrderBy(b => b.City),
            _ => desc ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name)
        };

        var items = await Project(query.Skip(r.Skip).Take(r.PageSize), today, d30).ToListAsync(ct);
        return PagedResult<AdminBusinessRowDto>.Create(items, r.Page, r.PageSize, total);
    }

    public static IQueryable<AdminBusinessRowDto> Project(IQueryable<Business> query, DateTime today, DateTimeOffset d30) =>
        query.Select(b => new AdminBusinessRowDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.Category.Name, b.SubCategory != null ? b.SubCategory.Name : null,
                b.City, b.Area, b.Owner.DisplayName, b.Owner.Email, b.PhoneNumber, b.Status, b.VerificationStatus, b.IsFeatured,
                b.Subscriptions.Where(s => (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today)
                    .OrderByDescending(s => s.StartDate).Select(s => s.Plan.Code).FirstOrDefault(),
                b.AverageRating, b.ReviewCount,
                b.Enquiries.Count(e => e.CreatedOn >= d30),
                b.Bookings.Count(x => x.CreatedOn >= d30),
                b.Advertisements.Count(a => a.Status == AdStatuses.Active && a.StartDate <= today && a.EndDate >= today),
                b.AvailabilityStatus, b.CreatedOn));
}

public sealed record AdminBusinessDetailDto(
    AdminBusinessRowDto Summary,
    BusinessDetailCoreDto Profile,
    IReadOnlyList<ServiceDto> Services,
    IReadOnlyList<HoursDto> Hours,
    IReadOnlyList<ImageDto> Images,
    IReadOnlyList<OwnerLeadDto> RecentLeads,
    IReadOnlyList<OwnerBookingDto> RecentBookings,
    IReadOnlyList<OwnerReviewDto> RecentReviews,
    IReadOnlyList<OwnerAdDto> Advertisements,
    IReadOnlyList<SubscriptionHistoryDto> Subscriptions,
    BusinessTotalsDto Totals);

public sealed record BusinessDetailCoreDto(string? Tagline, string Description, string? AddressLine, string? Landmark, string? Pincode,
    string? PhoneNumber, string? WhatsAppNumber, string? Email, string? Website, int? YearEstablished, int? TeamSize, string? Languages,
    string? CoverImageUrl, DateTimeOffset? VerifiedOn, string? OwnerPhone, DateTimeOffset? OwnerLastLogin);

public sealed record BusinessTotalsDto(int Leads, int ConvertedLeads, int Bookings, int CompletedBookings, decimal BookingRevenue, decimal PlatformRevenue,
    int ProfileViews30);

public sealed record GetAdminBusinessDetailQuery(Guid Id) : IRequest<AdminBusinessDetailDto>;

public sealed class GetAdminBusinessDetailHandler(IUnitOfWork uow) : IRequestHandler<GetAdminBusinessDetailQuery, AdminBusinessDetailDto>
{
    public async Task<AdminBusinessDetailDto> Handle(GetAdminBusinessDetailQuery r, CancellationToken ct)
    {
        var b = await uow.Repository<Business>().QueryNoTracking().Include(x => x.Owner).FirstOrDefaultAsync(x => x.Id == r.Id, ct)
                ?? throw new NotFoundException("Business", r.Id);

        var summary = await GetAdminBusinessesHandler.Project(uow.Repository<Business>().QueryNoTracking().Where(x => x.Id == b.Id), IndianTime.Today, DateTimeOffset.UtcNow.AddDays(-30)).FirstAsync(ct);
        var now = IndianTime.Now;

        var services = await uow.Repository<BusinessService>().QueryNoTracking().Where(s => s.BusinessId == b.Id)
            .OrderByDescending(s => s.IsPopular).ThenBy(s => s.Price)
            .Select(s => new ServiceDto(s.Id, s.Name, s.Description, s.Price, s.PriceUnit, s.DurationMinutes, s.Type, s.ImageUrl, s.IsPopular)).ToListAsync(ct);
        var hours = (await uow.Repository<BusinessHour>().QueryNoTracking().Where(h => h.BusinessId == b.Id).ToListAsync(ct))
            .OrderBy(h => (h.DayOfWeek + 6) % 7)
            .Select(h => new HoursDto(h.DayOfWeek, ((DayOfWeek)h.DayOfWeek).ToString(), h.OpenTime?.ToString(@"hh\:mm"), h.CloseTime?.ToString(@"hh\:mm"), h.IsClosed, h.DayOfWeek == (byte)now.DayOfWeek))
            .ToList();
        var images = await uow.Repository<BusinessImage>().QueryNoTracking().Where(i => i.BusinessId == b.Id).OrderBy(i => i.SortOrder)
            .Select(i => new ImageDto(i.Id, i.ImageUrl, i.ThumbnailUrl, i.MobileImageUrl, i.DesktopImageUrl, i.AltText, i.Caption, i.IsPrimary)).ToListAsync(ct);

        var leads = uow.Repository<Enquiry>().QueryNoTracking().Where(e => e.BusinessId == b.Id);
        var bookings = uow.Repository<Booking>().QueryNoTracking().Where(x => x.BusinessId == b.Id);
        var recentLeads = await leads.OrderByDescending(e => e.CreatedOn).Take(8).Select(OwnerProjections.Lead).ToListAsync(ct);
        var recentBookings = await bookings.OrderByDescending(x => x.ScheduledStart).Take(8).Select(OwnerProjections.Booking).ToListAsync(ct);
        var recentReviews = await uow.Repository<Review>().QueryNoTracking().Where(x => x.BusinessId == b.Id).OrderByDescending(x => x.CreatedOn).Take(8)
            .Select(x => new OwnerReviewDto(x.Id, x.Rating, x.Title, x.Comment, x.Customer.DisplayName, x.Status, x.OwnerReply, x.RepliedOn, x.IsVerifiedVisit, x.CreatedOn))
            .ToListAsync(ct);
        var ads = await uow.Repository<Advertisement>().QueryNoTracking().Where(a => a.BusinessId == b.Id).OrderByDescending(a => a.StartDate)
            .Select(AdProjections.ToOwnerDto).ToListAsync(ct);
        var subs = await uow.Repository<BusinessSubscription>().QueryNoTracking().Where(s => s.BusinessId == b.Id).OrderByDescending(s => s.StartDate).Take(12)
            .Select(s => new SubscriptionHistoryDto(s.SubscriptionNumber, s.Plan.Name, s.BillingCycle, s.StartDate, s.EndDate, s.Amount, s.Status)).ToListAsync(ct);

        var day30 = IndianTime.Today.AddDays(-30);
        var totals = new BusinessTotalsDto(
            await leads.CountAsync(ct),
            await leads.CountAsync(e => e.Status == EnquiryStatuses.Converted, ct),
            await bookings.CountAsync(ct),
            await bookings.CountAsync(x => x.Status == BookingStatuses.Completed, ct),
            await bookings.Where(x => x.Status == BookingStatuses.Completed).SumAsync(x => (decimal?)x.Amount, ct) ?? 0,
            await uow.Repository<Payment>().QueryNoTracking().Where(p => p.BusinessId == b.Id && p.Status == "Success").SumAsync(p => (decimal?)p.Amount, ct) ?? 0,
            await uow.Repository<BusinessDailyStat>().QueryNoTracking().Where(s => s.BusinessId == b.Id && s.StatDate > day30).SumAsync(s => (int?)s.ProfileViews, ct) ?? 0);

        var profile = new BusinessDetailCoreDto(b.Tagline, b.Description, b.AddressLine, b.Landmark, b.Pincode, b.PhoneNumber, b.WhatsAppNumber, b.Email,
            b.Website, b.YearEstablished, b.TeamSize, b.Languages, b.CoverImageUrl, b.VerifiedOn, b.Owner.PhoneNumber, b.Owner.LastLoginOn);

        return new AdminBusinessDetailDto(summary, profile, services, hours, images, recentLeads, recentBookings, recentReviews, ads, subs, totals);
    }
}

public sealed record UpdateBusinessAdminCommand(Guid Id, string? Status, string? VerificationStatus, bool? IsFeatured, string? Note) : IRequest;

public sealed class UpdateBusinessAdminValidator : AbstractValidator<UpdateBusinessAdminCommand>
{
    public UpdateBusinessAdminValidator()
    {
        RuleFor(x => x.Status).Must(s => s is null or BusinessStatuses.Active or BusinessStatuses.PendingApproval or BusinessStatuses.Suspended or BusinessStatuses.Inactive)
            .WithMessage("Invalid business status.");
        RuleFor(x => x.VerificationStatus).Must(s => s is null or VerificationStatuses.Verified or VerificationStatuses.Pending or VerificationStatuses.Rejected)
            .WithMessage("Invalid verification status.");
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public sealed class UpdateBusinessAdminHandler(IUnitOfWork uow, IRealtimeNotifier notifier) : IRequestHandler<UpdateBusinessAdminCommand>
{
    public async Task Handle(UpdateBusinessAdminCommand r, CancellationToken ct)
    {
        var b = await uow.Repository<Business>().Query().FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("Business", r.Id);
        var messages = new List<string>();

        if (r.Status is not null && r.Status != b.Status)
        {
            b.Status = r.Status;
            messages.Add(r.Status switch
            {
                BusinessStatuses.Active => "Your listing is now live on Calling Bell.",
                BusinessStatuses.Suspended => $"Your listing has been suspended.{(string.IsNullOrWhiteSpace(r.Note) ? "" : $" Reason: {r.Note}")}",
                _ => $"Your listing status changed to {r.Status}."
            });
        }
        if (r.VerificationStatus is not null && r.VerificationStatus != b.VerificationStatus)
        {
            b.VerificationStatus = r.VerificationStatus;
            b.VerifiedOn = r.VerificationStatus == VerificationStatuses.Verified ? DateTimeOffset.UtcNow : null;
            messages.Add(r.VerificationStatus == VerificationStatuses.Verified
                ? "Your business is verified. The verified badge is now shown on your profile."
                : $"Verification status: {r.VerificationStatus}.{(string.IsNullOrWhiteSpace(r.Note) ? "" : $" {r.Note}")}");
        }
        if (r.IsFeatured is { } featured) b.IsFeatured = featured;
        await uow.SaveChangesAsync(ct);

        foreach (var message in messages)
        {
            await NotificationPublisher.PublishAsync(uow, notifier, b.OwnerUserId, "Listing update", message, "Listing", "/business", ct);
        }
    }
}

// ===================== Categories =====================

public sealed record AdminSubCategoryDto(Guid Id, string Name, string Slug, string? IconUrl, bool IsActive, bool IsFeatured, int SortOrder, int BusinessCount);
public sealed record AdminCategoryDto(Guid Id, string Name, string Slug, string? Description, string? ImageUrl, string? IconUrl, string? ColorHex, bool IsActive,
    bool IsFeatured, int SortOrder, int BusinessCount, int LeadCount30, IReadOnlyList<AdminSubCategoryDto> SubCategories);

public sealed record GetAdminCategoriesQuery : IRequest<IReadOnlyList<AdminCategoryDto>>;

public sealed class GetAdminCategoriesHandler(IUnitOfWork uow) : IRequestHandler<GetAdminCategoriesQuery, IReadOnlyList<AdminCategoryDto>>
{
    public async Task<IReadOnlyList<AdminCategoryDto>> Handle(GetAdminCategoriesQuery request, CancellationToken ct)
    {
        var d30 = DateTimeOffset.UtcNow.AddDays(-30);
        var leads = uow.Repository<Enquiry>().QueryNoTracking();
        return await uow.Repository<Category>().QueryNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new AdminCategoryDto(c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.IconUrl, c.ColorHex, c.IsActive, c.IsFeatured, c.SortOrder,
                c.Businesses.Count(),
                leads.Count(e => e.Business.CategoryId == c.Id && e.CreatedOn >= d30),
                c.SubCategories.OrderBy(s => s.SortOrder)
                    .Select(s => new AdminSubCategoryDto(s.Id, s.Name, s.Slug, s.IconUrl, s.IsActive, s.IsFeatured, s.SortOrder, s.Businesses.Count())).ToList()))
            .ToListAsync(ct);
    }
}

public sealed record UpdateCategoryCommand(Guid Id, string Name, string? Description, bool IsActive, bool IsFeatured, int SortOrder) : IRequest;

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 999);
    }
}

public sealed class UpdateCategoryHandler(IUnitOfWork uow) : IRequestHandler<UpdateCategoryCommand>
{
    public async Task Handle(UpdateCategoryCommand r, CancellationToken ct)
    {
        var c = await uow.Repository<Category>().Query().FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("Category", r.Id);
        c.Name = r.Name.Trim();
        c.Description = r.Description?.Trim();
        c.IsActive = r.IsActive;
        c.IsFeatured = r.IsFeatured;
        c.SortOrder = r.SortOrder;
        await uow.SaveChangesAsync(ct);
    }
}

public sealed record UpdateSubCategoryCommand(Guid Id, bool IsActive, bool IsFeatured) : IRequest;

public sealed class UpdateSubCategoryHandler(IUnitOfWork uow) : IRequestHandler<UpdateSubCategoryCommand>
{
    public async Task Handle(UpdateSubCategoryCommand r, CancellationToken ct)
    {
        var s = await uow.Repository<SubCategory>().Query().FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("Sub-category", r.Id);
        s.IsActive = r.IsActive;
        s.IsFeatured = r.IsFeatured;
        await uow.SaveChangesAsync(ct);
    }
}

// ===================== Cities =====================

public sealed record AdminCityDto(Guid Id, string Name, string Slug, string State, string? ImageUrl, bool IsActive, bool IsPopular, int AreaCount,
    int BusinessCount, int CustomerCount, int Leads30);

public sealed record GetAdminCitiesQuery : IRequest<IReadOnlyList<AdminCityDto>>;

public sealed class GetAdminCitiesHandler(IUnitOfWork uow) : IRequestHandler<GetAdminCitiesQuery, IReadOnlyList<AdminCityDto>>
{
    public async Task<IReadOnlyList<AdminCityDto>> Handle(GetAdminCitiesQuery request, CancellationToken ct)
    {
        var d30 = DateTimeOffset.UtcNow.AddDays(-30);
        var businesses = uow.Repository<Business>().QueryNoTracking();
        var users = uow.Repository<ApplicationUser>().QueryNoTracking();
        var leads = uow.Repository<Enquiry>().QueryNoTracking();
        return await uow.Repository<City>().QueryNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new AdminCityDto(c.Id, c.Name, c.Slug, c.State.Name, c.ImageUrl, c.IsActive, c.IsPopular, c.Areas.Count(),
                businesses.Count(b => b.CityId == c.Id),
                users.Count(u => u.CityId == c.Id && u.UserType == "Customer"),
                leads.Count(e => e.Business.CityId == c.Id && e.CreatedOn >= d30)))
            .ToListAsync(ct);
    }
}

public sealed record UpdateCityCommand(Guid Id, bool IsActive, bool IsPopular) : IRequest;

public sealed class UpdateCityHandler(IUnitOfWork uow) : IRequestHandler<UpdateCityCommand>
{
    public async Task Handle(UpdateCityCommand r, CancellationToken ct)
    {
        var c = await uow.Repository<City>().Query().FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("City", r.Id);
        c.IsActive = r.IsActive;
        c.IsPopular = r.IsPopular;
        await uow.SaveChangesAsync(ct);
    }
}

// ===================== Users =====================

public sealed record AdminUserDto(string Id, string DisplayName, string? Email, string? PhoneNumber, string UserType, string? City, bool IsActive,
    DateTimeOffset CreatedOn, DateTimeOffset? LastLoginOn, int Bookings, int Reviews, int Businesses);

public sealed record GetAdminUsersQuery : PagedRequest, IRequest<PagedResult<AdminUserDto>>
{
    public string? Q { get; init; }
    public string? UserType { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class GetAdminUsersHandler(IUnitOfWork uow) : IRequestHandler<GetAdminUsersQuery, PagedResult<AdminUserDto>>
{
    public async Task<PagedResult<AdminUserDto>> Handle(GetAdminUsersQuery r, CancellationToken ct)
    {
        var query = uow.Repository<ApplicationUser>().QueryNoTracking();
        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var q = r.Q.Trim();
            query = query.Where(u => u.DisplayName.Contains(q) || (u.Email != null && u.Email.Contains(q)) || (u.PhoneNumber != null && u.PhoneNumber.Contains(q)));
        }
        if (!string.IsNullOrWhiteSpace(r.UserType)) query = query.Where(u => u.UserType == r.UserType);
        if (r.IsActive is { } active) query = query.Where(u => u.IsActive == active);

        var bookings = uow.Repository<Booking>().QueryNoTracking();
        var reviews = uow.Repository<Review>().QueryNoTracking();
        var businesses = uow.Repository<Business>().QueryNoTracking();

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(u => u.CreatedOn).Skip(r.Skip).Take(r.PageSize)
            .Select(u => new AdminUserDto(u.Id, u.DisplayName, u.Email, u.PhoneNumber, u.UserType, u.City != null ? u.City.Name : null, u.IsActive,
                u.CreatedOn, u.LastLoginOn,
                bookings.Count(b => b.CustomerUserId == u.Id),
                reviews.Count(x => x.CustomerUserId == u.Id),
                businesses.Count(b => b.OwnerUserId == u.Id)))
            .ToListAsync(ct);
        return PagedResult<AdminUserDto>.Create(items, r.Page, r.PageSize, total);
    }
}

public sealed record UpdateUserStatusCommand(string Id, bool IsActive) : IRequest;

public sealed class UpdateUserStatusHandler(IUnitOfWork uow, ICurrentUser currentUser) : IRequestHandler<UpdateUserStatusCommand>
{
    public async Task Handle(UpdateUserStatusCommand r, CancellationToken ct)
    {
        if (r.Id == currentUser.UserId) throw new BadRequestException("You cannot deactivate your own account.");
        var user = await uow.Repository<ApplicationUser>().Query().FirstOrDefaultAsync(u => u.Id == r.Id, ct) ?? throw new NotFoundException("User", r.Id);
        user.IsActive = r.IsActive;
        if (!r.IsActive)
        {
            // Revoke sessions immediately.
            var tokens = await uow.Repository<RefreshToken>().Query().Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(ct);
            tokens.ForEach(t => t.RevokedAt = DateTimeOffset.UtcNow);
        }
        await uow.SaveChangesAsync(ct);
    }
}

// ===================== Review moderation =====================

public sealed record AdminReviewDto(Guid Id, byte Rating, string? Title, string Comment, string Status, string? ReportReason, string CustomerName,
    string? CustomerEmail, string BusinessName, string BusinessSlug, string City, DateTimeOffset CreatedOn);

public sealed record GetAdminReviewsQuery : PagedRequest, IRequest<OwnerListResult<AdminReviewDto>>
{
    public string? Status { get; init; }
    public string? Q { get; init; }
    public int? Rating { get; init; }
}

public sealed class GetAdminReviewsHandler(IUnitOfWork uow) : IRequestHandler<GetAdminReviewsQuery, OwnerListResult<AdminReviewDto>>
{
    public async Task<OwnerListResult<AdminReviewDto>> Handle(GetAdminReviewsQuery r, CancellationToken ct)
    {
        var all = uow.Repository<Review>().QueryNoTracking();
        var counts = await all.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var query = all;
        if (!string.IsNullOrWhiteSpace(r.Status)) query = query.Where(x => x.Status == r.Status);
        if (r.Rating is { } rating) query = query.Where(x => x.Rating == rating);
        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var q = r.Q.Trim();
            query = query.Where(x => x.Comment.Contains(q) || x.Business.Name.Contains(q) || x.Customer.DisplayName.Contains(q));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedOn).Skip(r.Skip).Take(r.PageSize)
            .Select(x => new AdminReviewDto(x.Id, x.Rating, x.Title, x.Comment, x.Status, x.ReportReason, x.Customer.DisplayName, x.Customer.Email,
                x.Business.Name, x.Business.Slug, x.Business.City, x.CreatedOn))
            .ToListAsync(ct);
        return new OwnerListResult<AdminReviewDto>(PagedResult<AdminReviewDto>.Create(items, r.Page, r.PageSize, total), counts);
    }
}

public sealed record ModerateReviewCommand(Guid Id, string Action) : IRequest;

public sealed class ModerateReviewHandler(IUnitOfWork uow) : IRequestHandler<ModerateReviewCommand>
{
    public async Task Handle(ModerateReviewCommand r, CancellationToken ct)
    {
        var review = await uow.Repository<Review>().Query().Include(x => x.Business).FirstOrDefaultAsync(x => x.Id == r.Id, ct)
                     ?? throw new NotFoundException("Review", r.Id);
        review.Status = r.Action switch
        {
            "publish" => ReviewStatuses.Published,
            "reject" => ReviewStatuses.Rejected,
            _ => throw new BadRequestException("Action must be 'publish' or 'reject'.")
        };
        await uow.SaveChangesAsync(ct);
        await ReviewAggregates.RecalculateAsync(uow, review.Business, ct);
    }
}

// ===================== Advertisement moderation =====================

public sealed record AdminAdDto(OwnerAdDto Ad, Guid BusinessId, string BusinessName, string BusinessSlug, string City);

public sealed record GetAdminAdsQuery : PagedRequest, IRequest<OwnerListResult<AdminAdDto>>
{
    public string? Status { get; init; }
    public string? AdType { get; init; }
    public string? Q { get; init; }
}

public sealed class GetAdminAdsHandler(IUnitOfWork uow) : IRequestHandler<GetAdminAdsQuery, OwnerListResult<AdminAdDto>>
{
    public async Task<OwnerListResult<AdminAdDto>> Handle(GetAdminAdsQuery r, CancellationToken ct)
    {
        var all = uow.Repository<Advertisement>().QueryNoTracking();
        var counts = await all.GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var query = all;
        if (!string.IsNullOrWhiteSpace(r.Status)) query = query.Where(a => a.Status == r.Status);
        if (!string.IsNullOrWhiteSpace(r.AdType)) query = query.Where(a => a.AdType == r.AdType);
        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var q = r.Q.Trim();
            query = query.Where(a => a.Title.Contains(q) || a.Business.Name.Contains(q) || a.CampaignCode.Contains(q));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.StartDate).Skip(r.Skip).Take(r.PageSize)
            .Select(a => new { a.BusinessId, a.Business.Name, a.Business.Slug, a.Business.City, a.Id })
            .ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var dtos = await all.Where(a => ids.Contains(a.Id)).Select(AdProjections.ToOwnerDto).ToListAsync(ct);
        var items = rows.Select(x => new AdminAdDto(dtos.First(d => d.Id == x.Id), x.BusinessId, x.Name, x.Slug, x.City)).ToList();
        return new OwnerListResult<AdminAdDto>(PagedResult<AdminAdDto>.Create(items, r.Page, r.PageSize, total), counts);
    }
}

public sealed record ModerateAdCommand(Guid Id, string Action) : IRequest;

public sealed class ModerateAdHandler(IUnitOfWork uow, IRealtimeNotifier notifier) : IRequestHandler<ModerateAdCommand>
{
    public async Task Handle(ModerateAdCommand r, CancellationToken ct)
    {
        var ad = await uow.Repository<Advertisement>().Query().Include(a => a.Business).FirstOrDefaultAsync(a => a.Id == r.Id, ct)
                 ?? throw new NotFoundException("Advertisement", r.Id);
        var today = IndianTime.Today;

        switch (r.Action)
        {
            case "approve" when ad.Status == AdStatuses.PendingApproval:
                ad.Status = ad.StartDate > today ? "Scheduled" : AdStatuses.Active;
                // Campaigns are prepaid: raise the invoice on approval.
                var tax = Math.Round(ad.Budget * 0.18m, 2);
                uow.Repository<Payment>().Add(new Payment
                {
                    InvoiceNumber = $"INV-{ad.CampaignCode.Replace("ADV-", "AD")}",
                    BusinessId = ad.BusinessId, PaymentType = "Advertisement", ReferenceId = ad.Id,
                    Amount = ad.Budget, TaxAmount = tax, TotalAmount = ad.Budget + tax, PaymentMode = "Card", Status = "Success",
                    PaidOn = DateTimeOffset.UtcNow
                });
                break;
            case "reject" when ad.Status == AdStatuses.PendingApproval:
                ad.Status = AdStatuses.Rejected;
                break;
            case "pause" when ad.Status == AdStatuses.Active:
                ad.Status = "Paused";
                break;
            case "resume" when ad.Status == "Paused":
                ad.Status = ad.EndDate < today ? "Completed" : AdStatuses.Active;
                break;
            default:
                throw new BadRequestException($"Cannot {r.Action} a campaign that is {ad.Status}.");
        }
        await uow.SaveChangesAsync(ct);

        await NotificationPublisher.PublishAsync(uow, notifier, ad.Business.OwnerUserId, "Campaign update",
            $"Your campaign \"{ad.Title}\" is now {ad.Status}.", "Advertisement", "/business/advertising", ct);
    }
}

// ===================== Subscriptions overview =====================

public sealed record AdminSubscriptionRowDto(string SubscriptionNumber, Guid BusinessId, string BusinessName, string City, string PlanCode, string PlanName,
    string BillingCycle, DateTime StartDate, DateTime EndDate, decimal Amount, string Status, bool AutoRenew);

public sealed record AdminPlanSummaryDto(string Code, string Name, string? BadgeColor, int ActiveCount, decimal Mrr, decimal Revenue12m);

public sealed record AdminSubscriptionsDto(IReadOnlyList<AdminPlanSummaryDto> Plans, decimal TotalMrr, int ExpiringIn30Days, PagedResult<AdminSubscriptionRowDto> Current);

public sealed record GetAdminSubscriptionsQuery : PagedRequest, IRequest<AdminSubscriptionsDto>
{
    public string? Plan { get; init; }
    public string? Q { get; init; }
}

public sealed class GetAdminSubscriptionsHandler(IUnitOfWork uow) : IRequestHandler<GetAdminSubscriptionsQuery, AdminSubscriptionsDto>
{
    public async Task<AdminSubscriptionsDto> Handle(GetAdminSubscriptionsQuery r, CancellationToken ct)
    {
        var today = IndianTime.Today;
        var yearAgo = DateTimeOffset.UtcNow.AddYears(-1);
        var current = uow.Repository<BusinessSubscription>().QueryNoTracking()
            .Where(s => (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today);

        var payments = uow.Repository<Payment>().QueryNoTracking().Where(p => p.PaymentType == "Subscription" && p.Status == "Success" && p.PaidOn >= yearAgo);
        var subs = uow.Repository<BusinessSubscription>().QueryNoTracking();
        var plans = await uow.Repository<SubscriptionPlan>().QueryNoTracking().OrderBy(p => p.SortOrder)
            .Select(p => new AdminPlanSummaryDto(p.Code, p.Name, p.BadgeColor,
                current.Count(s => s.PlanId == p.Id),
                current.Where(s => s.PlanId == p.Id).Sum(s => (decimal?)(s.BillingCycle == "Annual" ? s.Amount / 12 : s.Amount)) ?? 0,
                payments.Where(pay => subs.Any(s => s.Id == pay.ReferenceId && s.PlanId == p.Id)).Sum(pay => (decimal?)pay.Amount) ?? 0))
            .ToListAsync(ct);

        var expiring = await current.CountAsync(s => s.Plan.Code != "FREE" && s.EndDate <= today.AddDays(30) && !s.AutoRenew, ct);

        var query = current;
        if (!string.IsNullOrWhiteSpace(r.Plan)) query = query.Where(s => s.Plan.Code == r.Plan);
        if (!string.IsNullOrWhiteSpace(r.Q)) query = query.Where(s => s.Business.Name.Contains(r.Q.Trim()));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.Plan.SortOrder).ThenBy(s => s.Business.Name).Skip(r.Skip).Take(r.PageSize)
            .Select(s => new AdminSubscriptionRowDto(s.SubscriptionNumber, s.BusinessId, s.Business.Name, s.Business.City, s.Plan.Code, s.Plan.Name,
                s.BillingCycle, s.StartDate, s.EndDate, s.Amount, s.Status, s.AutoRenew))
            .ToListAsync(ct);

        return new AdminSubscriptionsDto(plans.Select(p => p with { Mrr = Math.Round(p.Mrr, 0) }).ToList(), Math.Round(plans.Sum(p => p.Mrr), 0), expiring,
            PagedResult<AdminSubscriptionRowDto>.Create(items, r.Page, r.PageSize, total));
    }
}
