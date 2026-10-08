using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Catalog;
using CallingBell.Application.Features.Engagement;
using CallingBell.Application.Features.Notifications;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Owner;

internal static class OwnerAccess
{
    /// <summary>Ensures the current user owns the business (administrators may act on any business).</summary>
    public static async Task<Business> GetOwnedAsync(IUnitOfWork uow, ICurrentUser user, Guid businessId, CancellationToken ct, bool track = false)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var query = track ? uow.Repository<Business>().Query() : uow.Repository<Business>().QueryNoTracking();
        var business = await query.FirstOrDefaultAsync(b => b.Id == businessId, ct) ?? throw new NotFoundException("Business", businessId);
        if (business.OwnerUserId != userId && !user.IsInRole(Roles.Administrator)) throw new ForbiddenAccessException();
        return business;
    }

    public static decimal? Delta(decimal current, decimal previous) =>
        previous == 0 ? null : Math.Round((current - previous) / previous * 100, 1);
}

// ===================== My businesses =====================

public sealed record OwnerBusinessDto(Guid Id, string Name, string Slug, string? LogoUrl, string City, string? Area, string Status,
    string VerificationStatus, string AvailabilityStatus, string? PlanName, decimal AverageRating, int ReviewCount, string CategoryName);

public sealed record GetOwnerBusinessesQuery : IRequest<IReadOnlyList<OwnerBusinessDto>>;

public sealed class GetOwnerBusinessesHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerBusinessesQuery, IReadOnlyList<OwnerBusinessDto>>
{
    public async Task<IReadOnlyList<OwnerBusinessDto>> Handle(GetOwnerBusinessesQuery request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var today = IndianTime.Today;
        return await uow.Repository<Business>().QueryNoTracking()
            .Where(b => b.OwnerUserId == userId)
            .OrderBy(b => b.Name)
            .Select(b => new OwnerBusinessDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.City, b.Area, b.Status, b.VerificationStatus, b.AvailabilityStatus,
                b.Subscriptions.Where(s => (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today)
                    .OrderByDescending(s => s.StartDate).Select(s => s.Plan.Name).FirstOrDefault(),
                b.AverageRating, b.ReviewCount, b.Category.Name))
            .ToListAsync(ct);
    }
}

// ===================== Dashboard =====================

public sealed record KpiDto(string Key, string Label, decimal Value, decimal? ChangePercent, string Format);
public sealed record WeeklyPointDto(string Week, DateTime WeekStart, int Views, int Leads, int Bookings, decimal Revenue);
public sealed record NameCountDto(string Name, int Count);

public sealed record OwnerLeadDto(Guid Id, string EnquiryNumber, string CustomerName, string CustomerPhone, string? CustomerEmail, string EnquiryType,
    string? ServiceName, string Message, string Status, string Source, decimal? Budget, decimal? QuotedAmount, DateTime? PreferredDate,
    DateTimeOffset CreatedOn, DateTimeOffset? RespondedOn);

public sealed record OwnerBookingDto(Guid Id, string BookingNumber, string CustomerName, string CustomerPhone, string ServiceName,
    DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, string Status, decimal Amount, string PaymentStatus, string? ServiceAddress,
    string? Notes, string? CancellationReason, DateTimeOffset CreatedOn);

public sealed record OwnerPlanSummaryDto(string Code, string Name, int LeadCredits, int LeadsThisMonth, DateTime? RenewsOn, string BillingCycle);

public sealed record OwnerDashboardDto(
    OwnerBusinessDto Business,
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<WeeklyPointDto> Weekly,
    IReadOnlyList<NameCountDto> LeadSources,
    IReadOnlyList<NameCountDto> LeadStatuses,
    IReadOnlyDictionary<int, int> RatingBreakdown,
    IReadOnlyList<OwnerLeadDto> RecentLeads,
    IReadOnlyList<OwnerBookingDto> UpcomingBookings,
    OwnerPlanSummaryDto? Plan);

public sealed record GetOwnerDashboardQuery(Guid BusinessId) : IRequest<OwnerDashboardDto>;

public sealed class GetOwnerDashboardHandler(IUnitOfWork uow, ICurrentUser user, IMediator mediator) : IRequestHandler<GetOwnerDashboardQuery, OwnerDashboardDto>
{
    public async Task<OwnerDashboardDto> Handle(GetOwnerDashboardQuery request, CancellationToken ct)
    {
        var business = await OwnerAccess.GetOwnedAsync(uow, user, request.BusinessId, ct);
        var id = business.Id;
        var now = DateTimeOffset.UtcNow;
        var today = IndianTime.Today;
        var d30 = now.AddDays(-30);
        var d60 = now.AddDays(-60);
        var d90 = now.AddDays(-90);
        var day30 = today.AddDays(-30);
        var day60 = today.AddDays(-60);

        var stats = uow.Repository<BusinessDailyStat>().QueryNoTracking().Where(s => s.BusinessId == id);
        var leads = uow.Repository<Enquiry>().QueryNoTracking().Where(e => e.BusinessId == id);
        var bookings = uow.Repository<Booking>().QueryNoTracking().Where(b => b.BusinessId == id);

        var views30 = await stats.Where(s => s.StatDate > day30).SumAsync(s => (int?)s.ProfileViews, ct) ?? 0;
        var viewsPrev = await stats.Where(s => s.StatDate > day60 && s.StatDate <= day30).SumAsync(s => (int?)s.ProfileViews, ct) ?? 0;
        var calls30 = await stats.Where(s => s.StatDate > day30).SumAsync(s => (int?)(s.CallClicks + s.WhatsAppClicks), ct) ?? 0;
        var callsPrev = await stats.Where(s => s.StatDate > day60 && s.StatDate <= day30).SumAsync(s => (int?)(s.CallClicks + s.WhatsAppClicks), ct) ?? 0;
        var leads30 = await leads.CountAsync(e => e.CreatedOn >= d30, ct);
        var leadsPrev = await leads.CountAsync(e => e.CreatedOn >= d60 && e.CreatedOn < d30, ct);
        var bookings30 = await bookings.CountAsync(b => b.CreatedOn >= d30, ct);
        var bookingsPrev = await bookings.CountAsync(b => b.CreatedOn >= d60 && b.CreatedOn < d30, ct);
        var revenue30 = await bookings.Where(b => b.Status == BookingStatuses.Completed && b.ScheduledStart >= d30).SumAsync(b => (decimal?)b.Amount, ct) ?? 0;
        var revenuePrev = await bookings.Where(b => b.Status == BookingStatuses.Completed && b.ScheduledStart >= d60 && b.ScheduledStart < d30).SumAsync(b => (decimal?)b.Amount, ct) ?? 0;
        var leads90 = await leads.CountAsync(e => e.CreatedOn >= d90, ct);
        var converted90 = await leads.CountAsync(e => e.CreatedOn >= d90 && e.Status == EnquiryStatuses.Converted, ct);
        var pendingBookings = await bookings.CountAsync(b => b.Status == BookingStatuses.Pending, ct);
        var newLeads = await leads.CountAsync(e => e.Status == EnquiryStatuses.New, ct);

        var kpis = new List<KpiDto>
        {
            new("views", "Profile views", views30, OwnerAccess.Delta(views30, viewsPrev), "number"),
            new("leads", "Leads", leads30, OwnerAccess.Delta(leads30, leadsPrev), "number"),
            new("bookings", "Bookings", bookings30, OwnerAccess.Delta(bookings30, bookingsPrev), "number"),
            new("revenue", "Revenue", revenue30, OwnerAccess.Delta(revenue30, revenuePrev), "currency"),
            new("conversion", "Lead conversion", leads90 == 0 ? 0 : Math.Round(converted90 * 100m / leads90, 1), null, "percent"),
            new("contacts", "Calls & WhatsApp", calls30, OwnerAccess.Delta(calls30, callsPrev), "number"),
            new("newLeads", "New leads to respond", newLeads, null, "number"),
            new("pendingBookings", "Bookings to confirm", pendingBookings, null, "number"),
            new("rating", "Average rating", business.AverageRating, null, "rating")
        };

        // Weekly trend for the last 12 complete weeks (weeks start on Monday, IST).
        var firstWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(-7 * 12); // last 12 complete weeks
        var firstWeekOffset = IndianTime.At(firstWeek, TimeSpan.Zero);
        var viewRows = await stats.Where(s => s.StatDate >= firstWeek).Select(s => new { s.StatDate, s.ProfileViews }).ToListAsync(ct);
        var leadRows = await leads.Where(e => e.CreatedOn >= firstWeekOffset).Select(e => e.CreatedOn).ToListAsync(ct);
        var bookingRows = await bookings.Where(b => b.CreatedOn >= firstWeekOffset).Select(b => new { b.CreatedOn, b.Status, b.Amount }).ToListAsync(ct);
        var weekly = Enumerable.Range(0, 12).Select(i =>
        {
            var start = firstWeek.AddDays(7 * i);
            var end = start.AddDays(7);
            bool InWeek(DateTimeOffset d) { var local = d.ToOffset(IndianTime.Offset).Date; return local >= start && local < end; }
            return new WeeklyPointDto(start.ToString("dd MMM"), start,
                viewRows.Where(v => v.StatDate >= start && v.StatDate < end).Sum(v => v.ProfileViews),
                leadRows.Count(InWeek),
                bookingRows.Count(b => InWeek(b.CreatedOn)),
                bookingRows.Where(b => InWeek(b.CreatedOn) && b.Status == BookingStatuses.Completed).Sum(b => b.Amount));
        }).ToList();

        var sources = (await leads.Where(e => e.CreatedOn >= d90).GroupBy(e => e.Source)
            .Select(g => new NameCountDto(g.Key, g.Count())).ToListAsync(ct)).OrderByDescending(x => x.Count).ToList();
        var statuses = await leads.Where(e => e.CreatedOn >= d90).GroupBy(e => e.Status)
            .Select(g => new NameCountDto(g.Key, g.Count())).ToListAsync(ct);

        var ratingRows = await uow.Repository<Review>().QueryNoTracking().Where(r => r.BusinessId == id && r.Status == ReviewStatuses.Published)
            .GroupBy(r => r.Rating).Select(g => new { Rating = (int)g.Key, Count = g.Count() }).ToListAsync(ct);

        var recentLeads = await leads.OrderByDescending(e => e.CreatedOn).Take(6).Select(OwnerProjections.Lead).ToListAsync(ct);
        var upcoming = await bookings.Where(b => b.ScheduledStart >= now && (b.Status == BookingStatuses.Pending || b.Status == BookingStatuses.Confirmed))
            .OrderBy(b => b.ScheduledStart).Take(6).Select(OwnerProjections.Booking).ToListAsync(ct);

        var monthStart = IndianTime.At(new DateTime(today.Year, today.Month, 1), TimeSpan.Zero);
        var leadsThisMonth = await leads.CountAsync(e => e.CreatedOn >= monthStart, ct);
        var plan = await uow.Repository<BusinessSubscription>().QueryNoTracking()
            .Where(s => s.BusinessId == id && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new OwnerPlanSummaryDto(s.Plan.Code, s.Plan.Name, s.Plan.LeadCredits, leadsThisMonth,
                s.Plan.Code == "FREE" ? null : s.EndDate, s.BillingCycle))
            .FirstOrDefaultAsync(ct);

        var summary = (await mediator.Send(new GetOwnerBusinessesQuery(), ct)).FirstOrDefault(b => b.Id == id)
                      ?? new OwnerBusinessDto(id, business.Name, business.Slug, business.LogoUrl, business.City, business.Area, business.Status,
                          business.VerificationStatus, business.AvailabilityStatus, plan?.Name, business.AverageRating, business.ReviewCount, string.Empty);

        return new OwnerDashboardDto(summary, kpis, weekly, sources, statuses,
            Enumerable.Range(1, 5).ToDictionary(i => i, i => ratingRows.FirstOrDefault(x => x.Rating == i)?.Count ?? 0),
            recentLeads, upcoming, plan);
    }
}

internal static class OwnerProjections
{
    public static readonly System.Linq.Expressions.Expression<Func<Enquiry, OwnerLeadDto>> Lead = e =>
        new OwnerLeadDto(e.Id, e.EnquiryNumber, e.CustomerName, e.CustomerPhone, e.CustomerEmail, e.EnquiryType,
            e.Service != null ? e.Service.Name : null, e.Message, e.Status, e.Source, e.Budget, e.QuotedAmount, e.PreferredDate, e.CreatedOn, e.RespondedOn);

    public static readonly System.Linq.Expressions.Expression<Func<Booking, OwnerBookingDto>> Booking = b =>
        new OwnerBookingDto(b.Id, b.BookingNumber, b.CustomerName, b.CustomerPhone, b.Service.Name, b.ScheduledStart, b.ScheduledEnd, b.Status,
            b.Amount, b.PaymentStatus, b.ServiceAddress, b.Notes, b.CancellationReason, b.CreatedOn);
}

// ===================== Leads =====================

public sealed record OwnerListResult<T>(PagedResult<T> Page, IReadOnlyDictionary<string, int> StatusCounts);

public sealed record GetOwnerLeadsQuery : PagedRequest, IRequest<OwnerListResult<OwnerLeadDto>>
{
    public Guid BusinessId { get; init; }
    public string? Status { get; init; }
    public string? Type { get; init; }
    public string? Q { get; init; }
}

public sealed class GetOwnerLeadsHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerLeadsQuery, OwnerListResult<OwnerLeadDto>>
{
    public async Task<OwnerListResult<OwnerLeadDto>> Handle(GetOwnerLeadsQuery r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var all = uow.Repository<Enquiry>().QueryNoTracking().Where(e => e.BusinessId == r.BusinessId);
        var counts = await all.GroupBy(e => e.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var query = all;
        if (!string.IsNullOrWhiteSpace(r.Status)) query = query.Where(e => e.Status == r.Status);
        if (!string.IsNullOrWhiteSpace(r.Type)) query = query.Where(e => e.EnquiryType == r.Type);
        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var q = r.Q.Trim();
            query = query.Where(e => e.CustomerName.Contains(q) || e.CustomerPhone.Contains(q) || e.EnquiryNumber.Contains(q) || e.Message.Contains(q));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.CreatedOn).Skip(r.Skip).Take(r.PageSize).Select(OwnerProjections.Lead).ToListAsync(ct);
        return new OwnerListResult<OwnerLeadDto>(PagedResult<OwnerLeadDto>.Create(items, r.Page, r.PageSize, total), counts);
    }
}

public sealed record UpdateLeadCommand(Guid BusinessId, Guid LeadId, string Status, decimal? QuotedAmount) : IRequest<OwnerLeadDto>;

public sealed class UpdateLeadValidator : AbstractValidator<UpdateLeadCommand>
{
    public UpdateLeadValidator()
    {
        RuleFor(x => x.Status).Must(s => s is EnquiryStatuses.New or EnquiryStatuses.Contacted or EnquiryStatuses.Quoted or EnquiryStatuses.Converted or EnquiryStatuses.Lost)
            .WithMessage("Invalid lead status.");
        RuleFor(x => x.QuotedAmount).NotNull().GreaterThan(0).When(x => x.Status == EnquiryStatuses.Quoted).WithMessage("Enter the quoted amount.");
    }
}

public sealed class UpdateLeadHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<UpdateLeadCommand, OwnerLeadDto>
{
    public async Task<OwnerLeadDto> Handle(UpdateLeadCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var lead = await uow.Repository<Enquiry>().Query().FirstOrDefaultAsync(e => e.Id == r.LeadId && e.BusinessId == r.BusinessId, ct)
                   ?? throw new NotFoundException("Lead", r.LeadId);
        lead.Status = r.Status;
        if (r.QuotedAmount.HasValue) lead.QuotedAmount = r.QuotedAmount;
        lead.RespondedOn ??= r.Status == EnquiryStatuses.New ? null : DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
        return await uow.Repository<Enquiry>().QueryNoTracking().Where(e => e.Id == lead.Id).Select(OwnerProjections.Lead).FirstAsync(ct);
    }
}

// ===================== Bookings =====================

public sealed record GetOwnerBookingsQuery : PagedRequest, IRequest<OwnerListResult<OwnerBookingDto>>
{
    public Guid BusinessId { get; init; }
    public string? Status { get; init; }
    /// <summary>upcoming | past | today | all</summary>
    public string? Scope { get; init; }
    public string? Q { get; init; }
}

public sealed class GetOwnerBookingsHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerBookingsQuery, OwnerListResult<OwnerBookingDto>>
{
    public async Task<OwnerListResult<OwnerBookingDto>> Handle(GetOwnerBookingsQuery r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var all = uow.Repository<Booking>().QueryNoTracking().Where(b => b.BusinessId == r.BusinessId);
        var counts = await all.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var now = DateTimeOffset.UtcNow;
        var todayStart = IndianTime.At(IndianTime.Today, TimeSpan.Zero);
        var query = all;
        if (!string.IsNullOrWhiteSpace(r.Status)) query = query.Where(b => b.Status == r.Status);
        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var q = r.Q.Trim();
            query = query.Where(b => b.CustomerName.Contains(q) || b.CustomerPhone.Contains(q) || b.BookingNumber.Contains(q) || b.Service.Name.Contains(q));
        }

        query = r.Scope switch
        {
            "upcoming" => query.Where(b => b.ScheduledStart >= now).OrderBy(b => b.ScheduledStart),
            "today" => query.Where(b => b.ScheduledStart >= todayStart && b.ScheduledStart < todayStart.AddDays(1)).OrderBy(b => b.ScheduledStart),
            "past" => query.Where(b => b.ScheduledStart < now).OrderByDescending(b => b.ScheduledStart),
            _ => query.OrderByDescending(b => b.ScheduledStart)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip(r.Skip).Take(r.PageSize).Select(OwnerProjections.Booking).ToListAsync(ct);
        return new OwnerListResult<OwnerBookingDto>(PagedResult<OwnerBookingDto>.Create(items, r.Page, r.PageSize, total), counts);
    }
}

public sealed record UpdateBookingStatusCommand(Guid BusinessId, Guid BookingId, string Status, string? Reason) : IRequest<OwnerBookingDto>;

public sealed class UpdateBookingStatusHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier)
    : IRequestHandler<UpdateBookingStatusCommand, OwnerBookingDto>
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [BookingStatuses.Pending] = [BookingStatuses.Confirmed, BookingStatuses.Cancelled],
        [BookingStatuses.Confirmed] = [BookingStatuses.Completed, BookingStatuses.Cancelled, BookingStatuses.NoShow]
    };

    public async Task<OwnerBookingDto> Handle(UpdateBookingStatusCommand r, CancellationToken ct)
    {
        var business = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var booking = await uow.Repository<Booking>().Query().Include(b => b.Service)
            .FirstOrDefaultAsync(b => b.Id == r.BookingId && b.BusinessId == r.BusinessId, ct) ?? throw new NotFoundException("Booking", r.BookingId);

        if (!Allowed.TryGetValue(booking.Status, out var next) || !next.Contains(r.Status))
            throw new BadRequestException($"A {booking.Status.ToLowerInvariant()} booking cannot be marked as {r.Status}.");

        booking.Status = r.Status;
        if (r.Status == BookingStatuses.Completed)
        {
            booking.CommissionAmount = Math.Round(booking.Amount * 0.08m, 2);
            if (booking.Amount > 0) booking.PaymentStatus = "Paid";
        }
        if (r.Status == BookingStatuses.Cancelled)
        {
            booking.CancellationReason = string.IsNullOrWhiteSpace(r.Reason) ? "Cancelled by business" : r.Reason.Trim();
            if (booking.PaymentStatus == "Paid") booking.PaymentStatus = "Refunded";
        }
        await uow.SaveChangesAsync(ct);

        var when = booking.ScheduledStart.ToOffset(IndianTime.Offset).ToString("dd MMM, h:mm tt");
        var message = r.Status switch
        {
            BookingStatuses.Confirmed => $"{business.Name} confirmed your {booking.Service.Name} booking for {when}.",
            BookingStatuses.Completed => $"Your {booking.Service.Name} with {business.Name} is complete. Tell others how it went!",
            BookingStatuses.Cancelled => $"{business.Name} cancelled your {booking.Service.Name} booking for {when}.",
            _ => $"Your booking {booking.BookingNumber} was updated to {r.Status}."
        };
        await NotificationPublisher.PublishAsync(uow, notifier, booking.CustomerUserId, $"Booking {r.Status.ToLowerInvariant()}", message,
            "Booking", "/account/bookings", ct);

        return await uow.Repository<Booking>().QueryNoTracking().Where(b => b.Id == booking.Id).Select(OwnerProjections.Booking).FirstAsync(ct);
    }
}

// ===================== Reviews =====================

public sealed record OwnerReviewDto(Guid Id, byte Rating, string? Title, string Comment, string CustomerName, string Status, string? OwnerReply,
    DateTimeOffset? RepliedOn, bool IsVerifiedVisit, DateTimeOffset CreatedOn);

public sealed record GetOwnerReviewsQuery : PagedRequest, IRequest<PagedResult<OwnerReviewDto>>
{
    public Guid BusinessId { get; init; }
    /// <summary>all | unreplied | replied</summary>
    public string? Filter { get; init; }
    public int? Rating { get; init; }
}

public sealed class GetOwnerReviewsHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerReviewsQuery, PagedResult<OwnerReviewDto>>
{
    public async Task<PagedResult<OwnerReviewDto>> Handle(GetOwnerReviewsQuery r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var query = uow.Repository<Review>().QueryNoTracking().Where(x => x.BusinessId == r.BusinessId && x.Status != ReviewStatuses.Rejected);
        if (r.Filter == "unreplied") query = query.Where(x => x.OwnerReply == null);
        if (r.Filter == "replied") query = query.Where(x => x.OwnerReply != null);
        if (r.Rating is { } rating) query = query.Where(x => x.Rating == rating);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedOn).Skip(r.Skip).Take(r.PageSize)
            .Select(x => new OwnerReviewDto(x.Id, x.Rating, x.Title, x.Comment, x.Customer.DisplayName, x.Status, x.OwnerReply, x.RepliedOn, x.IsVerifiedVisit, x.CreatedOn))
            .ToListAsync(ct);
        return PagedResult<OwnerReviewDto>.Create(items, r.Page, r.PageSize, total);
    }
}

public sealed record ReplyToReviewCommand(Guid BusinessId, Guid ReviewId, string Reply) : IRequest;

public sealed class ReplyToReviewValidator : AbstractValidator<ReplyToReviewCommand>
{
    public ReplyToReviewValidator() => RuleFor(x => x.Reply).NotEmpty().MinimumLength(5).MaximumLength(1000);
}

public sealed class ReplyToReviewHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<ReplyToReviewCommand>
{
    public async Task Handle(ReplyToReviewCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var review = await uow.Repository<Review>().Query().FirstOrDefaultAsync(x => x.Id == r.ReviewId && x.BusinessId == r.BusinessId, ct)
                     ?? throw new NotFoundException("Review", r.ReviewId);
        review.OwnerReply = r.Reply.Trim();
        review.RepliedOn = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);
    }
}

// ===================== Availability =====================

public sealed record UpdateAvailabilityCommand(Guid BusinessId, string Status) : IRequest<string>;

public sealed class UpdateAvailabilityHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier) : IRequestHandler<UpdateAvailabilityCommand, string>
{
    public async Task<string> Handle(UpdateAvailabilityCommand r, CancellationToken ct)
    {
        var valid = await uow.Repository<LookupValue>().QueryNoTracking()
            .AnyAsync(l => l.LookupType == LookupTypes.AvailabilityStatus && l.Code == r.Status && l.IsActive, ct);
        if (!valid) throw new BadRequestException("Unknown availability status.");

        var business = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct, track: true);
        business.AvailabilityStatus = r.Status;
        business.LastSeenOn = DateTimeOffset.UtcNow;
        await uow.SaveChangesAsync(ct);

        await notifier.AvailabilityChangedAsync(business.Id, business.AvailabilityStatus, business.LastSeenOn.Value, ct);
        return business.AvailabilityStatus;
    }
}

// ===================== Services =====================

public sealed record OwnerServiceDto(Guid Id, string Name, string Description, decimal Price, string? PriceUnit, int DurationMinutes, string Type,
    bool IsPopular, bool IsActive, int BookingCount);

public sealed record GetOwnerServicesQuery(Guid BusinessId) : IRequest<IReadOnlyList<OwnerServiceDto>>;

public sealed class GetOwnerServicesHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerServicesQuery, IReadOnlyList<OwnerServiceDto>>
{
    public async Task<IReadOnlyList<OwnerServiceDto>> Handle(GetOwnerServicesQuery r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var bookings = uow.Repository<Booking>().QueryNoTracking();
        return await uow.Repository<BusinessService>().QueryNoTracking()
            .Where(s => s.BusinessId == r.BusinessId)
            .OrderByDescending(s => s.IsActive).ThenByDescending(s => s.IsPopular).ThenBy(s => s.Name)
            .Select(s => new OwnerServiceDto(s.Id, s.Name, s.Description, s.Price, s.PriceUnit, s.DurationMinutes, s.Type, s.IsPopular, s.IsActive,
                bookings.Count(b => b.ServiceId == s.Id)))
            .ToListAsync(ct);
    }
}

public sealed record UpsertServiceCommand(Guid BusinessId, Guid? ServiceId, string Name, string Description, decimal Price, string? PriceUnit,
    int DurationMinutes, string Type, bool IsPopular, bool IsActive) : IRequest<Guid>;

public sealed class UpsertServiceValidator : AbstractValidator<UpsertServiceCommand>
{
    public UpsertServiceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).LessThan(10_000_000);
        RuleFor(x => x.PriceUnit).MaximumLength(40);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(10, 600);
        RuleFor(x => x.Type).Must(t => t is "AtHome" or "InStore" or "Online" or "Consultation").WithMessage("Choose a valid service type.");
    }
}

public sealed class UpsertServiceHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<UpsertServiceCommand, Guid>
{
    public async Task<Guid> Handle(UpsertServiceCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var services = uow.Repository<BusinessService>();
        if (await services.QueryNoTracking().AnyAsync(s => s.BusinessId == r.BusinessId && s.Name == r.Name.Trim() && s.Id != r.ServiceId, ct))
            throw new ConflictException("A service with this name already exists.");

        BusinessService service;
        if (r.ServiceId is { } id)
        {
            service = await services.Query().FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == r.BusinessId, ct) ?? throw new NotFoundException("Service", id);
        }
        else
        {
            var imageUrl = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Id == r.BusinessId)
                .Select(b => b.SubCategory != null ? b.SubCategory.ImageUrl : b.Category.ImageUrl).FirstOrDefaultAsync(ct);
            service = new BusinessService { BusinessId = r.BusinessId, ImageUrl = imageUrl };
            services.Add(service);
        }

        service.Name = r.Name.Trim();
        service.Description = r.Description.Trim();
        service.Price = r.Price;
        service.PriceUnit = r.PriceUnit?.Trim();
        service.DurationMinutes = r.DurationMinutes;
        service.Type = r.Type;
        service.IsPopular = r.IsPopular;
        service.IsActive = r.IsActive;
        await uow.SaveChangesAsync(ct);
        return service.Id;
    }
}

// ===================== Profile & hours =====================

public sealed record OwnerHoursInput(int DayOfWeek, string? Open, string? Close, bool IsClosed);

public sealed record OwnerProfileDto(Guid Id, string Name, string? Tagline, string Description, string? PhoneNumber, string? WhatsAppNumber,
    string? Email, string? Website, string? AddressLine, string? Landmark, bool AcceptsOnlineBooking, bool OffersVideoConsultation,
    bool OffersHomeService, IReadOnlyList<HoursDto> Hours);

public sealed record GetOwnerProfileQuery(Guid BusinessId) : IRequest<OwnerProfileDto>;

public sealed class GetOwnerProfileHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerProfileQuery, OwnerProfileDto>
{
    public async Task<OwnerProfileDto> Handle(GetOwnerProfileQuery r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var today = (byte)IndianTime.Now.DayOfWeek;
        var hours = (await uow.Repository<BusinessHour>().QueryNoTracking().Where(h => h.BusinessId == b.Id).ToListAsync(ct))
            .OrderBy(h => (h.DayOfWeek + 6) % 7)
            .Select(h => new HoursDto(h.DayOfWeek, ((DayOfWeek)h.DayOfWeek).ToString(), h.OpenTime?.ToString(@"hh\:mm"), h.CloseTime?.ToString(@"hh\:mm"), h.IsClosed, h.DayOfWeek == today))
            .ToList();
        return new OwnerProfileDto(b.Id, b.Name, b.Tagline, b.Description, b.PhoneNumber, b.WhatsAppNumber, b.Email, b.Website, b.AddressLine, b.Landmark,
            b.AcceptsOnlineBooking, b.OffersVideoConsultation, b.OffersHomeService, hours);
    }
}

public sealed record UpdateOwnerProfileCommand(Guid BusinessId, string? Tagline, string Description, string? PhoneNumber, string? WhatsAppNumber,
    string? Email, string? Website, string? AddressLine, string? Landmark, bool AcceptsOnlineBooking, bool OffersVideoConsultation,
    bool OffersHomeService, IReadOnlyList<OwnerHoursInput> Hours) : IRequest;

public sealed class UpdateOwnerProfileValidator : AbstractValidator<UpdateOwnerProfileCommand>
{
    public UpdateOwnerProfileValidator()
    {
        RuleFor(x => x.Tagline).MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(30).MaximumLength(2000);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Website).Must(w => Uri.TryCreate(w, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            .When(x => !string.IsNullOrWhiteSpace(x.Website)).WithMessage("Enter a valid website URL.");
        RuleForEach(x => x.Hours).ChildRules(h =>
        {
            h.RuleFor(x => x.DayOfWeek).InclusiveBetween(0, 6);
            h.RuleFor(x => x.Open).NotEmpty().Matches(@"^\d{2}:\d{2}$").When(x => !x.IsClosed);
            h.RuleFor(x => x.Close).NotEmpty().Matches(@"^\d{2}:\d{2}$").When(x => !x.IsClosed);
        });
    }
}

public sealed class UpdateOwnerProfileHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<UpdateOwnerProfileCommand>
{
    public async Task Handle(UpdateOwnerProfileCommand r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct, track: true);
        b.Tagline = r.Tagline?.Trim();
        b.Description = r.Description.Trim();
        b.PhoneNumber = r.PhoneNumber?.Trim();
        b.WhatsAppNumber = r.WhatsAppNumber?.Trim();
        b.Email = r.Email?.Trim();
        b.Website = r.Website?.Trim();
        b.AddressLine = r.AddressLine?.Trim();
        b.Landmark = r.Landmark?.Trim();
        b.AcceptsOnlineBooking = r.AcceptsOnlineBooking;
        b.OffersVideoConsultation = r.OffersVideoConsultation;
        b.OffersHomeService = r.OffersHomeService;

        var hours = await uow.Repository<BusinessHour>().Query().Where(h => h.BusinessId == b.Id).ToListAsync(ct);
        foreach (var input in r.Hours)
        {
            var row = hours.FirstOrDefault(h => h.DayOfWeek == input.DayOfWeek);
            if (row is null)
            {
                row = new BusinessHour { BusinessId = b.Id, DayOfWeek = (byte)input.DayOfWeek };
                uow.Repository<BusinessHour>().Add(row);
            }
            row.IsClosed = input.IsClosed;
            row.OpenTime = input.IsClosed ? null : TimeSpan.Parse(input.Open!);
            row.CloseTime = input.IsClosed ? null : TimeSpan.Parse(input.Close!);
            if (!row.IsClosed && row.CloseTime <= row.OpenTime) throw new BadRequestException($"Closing time must be after opening time on {(DayOfWeek)input.DayOfWeek}.");
        }
        await uow.SaveChangesAsync(ct);
    }
}

// ===================== Subscription =====================

public sealed record SubscriptionHistoryDto(string SubscriptionNumber, string PlanName, string BillingCycle, DateTime StartDate, DateTime EndDate,
    decimal Amount, string Status, string Currency = "INR");

public sealed record InvoiceDto(string InvoiceNumber, string PaymentType, decimal Amount, decimal TaxAmount, decimal TotalAmount, string PaymentMode,
    string Status, DateTimeOffset PaidOn, string Currency = "INR");

public sealed record OwnerSubscriptionDto(SubscriptionHistoryDto? Current, string? CurrentPlanCode, IReadOnlyList<PlanDto> Plans,
    IReadOnlyList<SubscriptionHistoryDto> History, IReadOnlyList<InvoiceDto> Invoices);

public sealed record GetOwnerSubscriptionQuery(Guid BusinessId) : IRequest<OwnerSubscriptionDto>;

public sealed class GetOwnerSubscriptionHandler(IUnitOfWork uow, ICurrentUser user, IMediator mediator) : IRequestHandler<GetOwnerSubscriptionQuery, OwnerSubscriptionDto>
{
    public async Task<OwnerSubscriptionDto> Handle(GetOwnerSubscriptionQuery r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var today = IndianTime.Today;
        var history = await uow.Repository<BusinessSubscription>().QueryNoTracking()
            .Where(s => s.BusinessId == r.BusinessId).OrderByDescending(s => s.StartDate)
            .Select(s => new { Dto = new SubscriptionHistoryDto(s.SubscriptionNumber, s.Plan.Name, s.BillingCycle, s.StartDate, s.EndDate, s.Amount, s.Status, s.Currency), s.Plan.Code, s.StartDate, s.EndDate, s.Status })
            .ToListAsync(ct);
        var current = history.FirstOrDefault(h => (h.Status == SubscriptionStatuses.Active || h.Status == SubscriptionStatuses.Trial) && h.StartDate <= today && h.EndDate >= today);

        var invoices = await uow.Repository<Payment>().QueryNoTracking()
            .Where(p => p.BusinessId == r.BusinessId).OrderByDescending(p => p.PaidOn).Take(24)
            .Select(p => new InvoiceDto(p.InvoiceNumber, p.PaymentType, p.Amount, p.TaxAmount, p.TotalAmount, p.PaymentMode, p.Status, p.PaidOn, p.Currency))
            .ToListAsync(ct);

        // Priced for the business's country, as its checkout will charge.
        var plans = await mediator.Send(new GetPlansQuery(BusinessId: r.BusinessId), ct);
        return new OwnerSubscriptionDto(current?.Dto, current?.Code, plans, history.Select(h => h.Dto).Take(24).ToList(), invoices);
    }
}

// ===================== Advertising =====================

public sealed record OwnerAdDto(Guid Id, string CampaignCode, string AdType, string Title, string? Description, DateTime StartDate, DateTime EndDate,
    decimal Budget, decimal AmountSpent, int Impressions, int Clicks, decimal Ctr, string Status, string? TargetCity, string? TargetCategory);

public sealed record GetOwnerAdsQuery(Guid BusinessId) : IRequest<IReadOnlyList<OwnerAdDto>>;

public sealed class GetOwnerAdsHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerAdsQuery, IReadOnlyList<OwnerAdDto>>
{
    public async Task<IReadOnlyList<OwnerAdDto>> Handle(GetOwnerAdsQuery r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        return await uow.Repository<Advertisement>().QueryNoTracking()
            .Where(a => a.BusinessId == r.BusinessId)
            .OrderByDescending(a => a.StartDate)
            .Select(AdProjections.ToOwnerDto)
            .ToListAsync(ct);
    }
}

public static class AdProjections
{
    public static readonly System.Linq.Expressions.Expression<Func<Advertisement, OwnerAdDto>> ToOwnerDto = a =>
        new OwnerAdDto(a.Id, a.CampaignCode, a.AdType, a.Title, a.Description, a.StartDate, a.EndDate, a.Budget, a.AmountSpent, a.Impressions, a.Clicks,
            a.Impressions == 0 ? 0 : Math.Round((decimal)a.Clicks * 100 / a.Impressions, 2), a.Status,
            a.TargetCity != null ? a.TargetCity.Name : null, a.TargetCategory != null ? a.TargetCategory.Name : null);
}

public sealed record CreateAdCampaignCommand(Guid BusinessId, string AdType, string Title, string? Description, DateTime StartDate, DateTime EndDate,
    decimal Budget) : IRequest<CreatedReferenceDto>;

public sealed class CreateAdCampaignValidator : AbstractValidator<CreateAdCampaignCommand>
{
    public CreateAdCampaignValidator()
    {
        RuleFor(x => x.AdType).Must(t => t is AdTypes.FeaturedListing or AdTypes.SponsoredListing or AdTypes.HomepageBanner or AdTypes.SearchPromotion)
            .WithMessage("Choose a valid ad type.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.StartDate).GreaterThanOrEqualTo(_ => IndianTime.Today).WithMessage("Start date cannot be in the past.");
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate).WithMessage("End date must be after the start date.");
        RuleFor(x => x.Budget).GreaterThanOrEqualTo(2000).WithMessage("Minimum campaign budget is ₹2,000.").LessThanOrEqualTo(1_000_000);
    }
}

public sealed class CreateAdCampaignHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<CreateAdCampaignCommand, CreatedReferenceDto>
{
    public async Task<CreatedReferenceDto> Handle(CreateAdCampaignCommand r, CancellationToken ct)
    {
        var business = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        if (business.Status != BusinessStatuses.Active) throw new BadRequestException("Only active listings can run advertising campaigns.");

        var ad = new Advertisement
        {
            CampaignCode = $"ADV-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            BusinessId = business.Id,
            AdType = r.AdType,
            Title = r.Title.Trim(),
            Description = r.Description?.Trim(),
            ImageUrl = business.CoverImageUrl,
            MobileImageUrl = business.CoverImageUrl,
            DesktopImageUrl = business.CoverImageUrl,
            AltText = r.Title.Trim(),
            TargetCityId = r.AdType == AdTypes.HomepageBanner ? null : business.CityId,
            TargetCategoryId = r.AdType is AdTypes.FeaturedListing or AdTypes.SearchPromotion ? business.CategoryId : null,
            StartDate = r.StartDate.Date,
            EndDate = r.EndDate.Date,
            Budget = r.Budget,
            Status = AdStatuses.PendingApproval
        };
        uow.Repository<Advertisement>().Add(ad);
        await uow.SaveChangesAsync(ct);
        return new CreatedReferenceDto(ad.Id, ad.CampaignCode);
    }
}
