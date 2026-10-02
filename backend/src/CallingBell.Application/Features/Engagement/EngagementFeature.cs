using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Notifications;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;

namespace CallingBell.Application.Features.Engagement;

internal static class Validation
{
    public const string IndianMobile = @"^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$";
}

internal static class References
{
    public static string New(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}

// ===================== Enquiries (leads) =====================

public sealed record CreateEnquiryCommand(Guid BusinessId, Guid? ServiceId, string CustomerName, string CustomerPhone, string? CustomerEmail,
    string EnquiryType, string Message, DateTime? PreferredDate, decimal? Budget) : IRequest<CreatedReferenceDto>;

public sealed record CreatedReferenceDto(Guid Id, string Reference);

public sealed class CreateEnquiryValidator : AbstractValidator<CreateEnquiryCommand>
{
    public CreateEnquiryValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.CustomerPhone).NotEmpty().Matches(Validation.IndianMobile).WithMessage("Enter a valid 10-digit Indian mobile number.");
        RuleFor(x => x.CustomerEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));
        RuleFor(x => x.EnquiryType).Must(t => t is "Enquiry" or "Quotation" or "Callback").WithMessage("Choose a valid request type.");
        RuleFor(x => x.Message).NotEmpty().MinimumLength(10).MaximumLength(1000);
        RuleFor(x => x.PreferredDate).GreaterThanOrEqualTo(_ => IndianTime.Today).When(x => x.PreferredDate.HasValue)
            .WithMessage("Preferred date cannot be in the past.");
        RuleFor(x => x.Budget).GreaterThan(0).When(x => x.Budget.HasValue);
    }
}

public sealed class CreateEnquiryHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier)
    : IRequestHandler<CreateEnquiryCommand, CreatedReferenceDto>
{
    public async Task<CreatedReferenceDto> Handle(CreateEnquiryCommand r, CancellationToken ct)
    {
        var business = await uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => b.Id == r.BusinessId).Select(b => new { b.Id, b.OwnerUserId, b.Name })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Business", r.BusinessId);

        string? serviceName = null;
        if (r.ServiceId is { } serviceId)
        {
            serviceName = await uow.Repository<BusinessService>().QueryNoTracking()
                .Where(s => s.Id == serviceId && s.BusinessId == business.Id && s.IsActive).Select(s => s.Name).FirstOrDefaultAsync(ct)
                ?? throw new BadRequestException("The selected service is not offered by this business.");
        }

        var enquiry = new Enquiry
        {
            EnquiryNumber = References.New("LD"),
            BusinessId = business.Id,
            ServiceId = r.ServiceId,
            CustomerUserId = user.UserId,
            CustomerName = r.CustomerName.Trim(),
            CustomerPhone = r.CustomerPhone.Trim(),
            CustomerEmail = r.CustomerEmail?.Trim(),
            EnquiryType = r.EnquiryType,
            Message = r.Message.Trim(),
            PreferredDate = r.PreferredDate?.Date,
            Budget = r.Budget,
            Status = EnquiryStatuses.New,
            Source = "Profile"
        };
        uow.Repository<Enquiry>().Add(enquiry);
        await uow.SaveChangesAsync(ct);

        var title = r.EnquiryType switch { "Quotation" => "New quotation request", "Callback" => "Callback requested", _ => "New enquiry" };
        await NotificationPublisher.PublishAsync(uow, notifier, business.OwnerUserId, title,
            $"{enquiry.CustomerName}{(serviceName is null ? "" : $" · {serviceName}")}: {Truncate(enquiry.Message, 110)}",
            "Lead", "/business/leads", ct);

        return new CreatedReferenceDto(enquiry.Id, enquiry.EnquiryNumber);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}

// ===================== Reviews =====================

public sealed record CreateReviewCommand(Guid BusinessId, byte Rating, string? Title, string Comment) : IRequest<ReviewDto>;

public sealed class CreateReviewValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween((byte)1, (byte)5);
        RuleFor(x => x.Title).MaximumLength(120);
        RuleFor(x => x.Comment).NotEmpty().MinimumLength(20).WithMessage("Please write at least 20 characters.").MaximumLength(2000);
    }
}

public sealed class CreateReviewHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier) : IRequestHandler<CreateReviewCommand, ReviewDto>
{
    public async Task<ReviewDto> Handle(CreateReviewCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var business = await uow.Repository<Business>().Query().Listed().FirstOrDefaultAsync(b => b.Id == r.BusinessId, ct)
                       ?? throw new NotFoundException("Business", r.BusinessId);
        if (business.OwnerUserId == userId) throw new BadRequestException("You cannot review your own business.");

        var reviews = uow.Repository<Review>();
        if (await reviews.QueryNoTracking().AnyAsync(x => x.BusinessId == business.Id && x.CustomerUserId == userId, ct))
        {
            throw new ConflictException("You have already reviewed this business.");
        }

        var verifiedVisit = await uow.Repository<Booking>().QueryNoTracking()
            .AnyAsync(b => b.BusinessId == business.Id && b.CustomerUserId == userId && b.Status == BookingStatuses.Completed, ct);

        var review = new Review
        {
            BusinessId = business.Id, CustomerUserId = userId, Rating = r.Rating, Title = r.Title?.Trim(), Comment = r.Comment.Trim(),
            Status = ReviewStatuses.Published, IsVerifiedVisit = verifiedVisit
        };
        reviews.Add(review);
        await uow.SaveChangesAsync(ct);

        await ReviewAggregates.RecalculateAsync(uow, business, ct);

        await NotificationPublisher.PublishAsync(uow, notifier, business.OwnerUserId, $"New {r.Rating}★ review",
            Truncate(review.Comment, 120), "Review", "/business/reviews", ct);

        var name = await uow.Repository<ApplicationUser>().QueryNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstAsync(ct);
        return new ReviewDto(review.Id, review.Rating, review.Title, review.Comment, name, review.CreatedOn, null, null, verifiedVisit, 0);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}

public static class ReviewAggregates
{
    /// <summary>Keeps the denormalised AverageRating/ReviewCount on Businesses in sync with published reviews.</summary>
    public static async Task RecalculateAsync(IUnitOfWork uow, Business business, CancellationToken ct)
    {
        var stats = await uow.Repository<Review>().QueryNoTracking()
            .Where(x => x.BusinessId == business.Id && x.Status == ReviewStatuses.Published)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(x => (decimal)x.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        business.AverageRating = Math.Round(stats?.Avg ?? 0, 2);
        business.ReviewCount = stats?.Count ?? 0;
        await uow.SaveChangesAsync(ct);
    }
}

// ===================== Bookings =====================

public sealed record CreateBookingCommand(Guid BusinessId, Guid ServiceId, DateTimeOffset ScheduledStart, string? ServiceAddress, string? Notes,
    string? ContactPhone) : IRequest<CreatedReferenceDto>;

public sealed class CreateBookingValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingValidator()
    {
        RuleFor(x => x.ScheduledStart).GreaterThan(_ => DateTimeOffset.UtcNow).WithMessage("Choose a future time slot.");
        RuleFor(x => x.ServiceAddress).MaximumLength(300);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.ContactPhone).Matches(Validation.IndianMobile).When(x => !string.IsNullOrWhiteSpace(x.ContactPhone))
            .WithMessage("Enter a valid 10-digit Indian mobile number.");
    }
}

public sealed class CreateBookingHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier) : IRequestHandler<CreateBookingCommand, CreatedReferenceDto>
{
    public async Task<CreatedReferenceDto> Handle(CreateBookingCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var business = await uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => b.Id == r.BusinessId).Select(b => new { b.Id, b.OwnerUserId, b.AcceptsOnlineBooking })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Business", r.BusinessId);
        if (!business.AcceptsOnlineBooking) throw new BadRequestException("This business does not accept online bookings. Send an enquiry instead.");

        var service = await uow.Repository<BusinessService>().QueryNoTracking()
            .FirstOrDefaultAsync(s => s.Id == r.ServiceId && s.BusinessId == business.Id && s.IsActive, ct)
            ?? throw new BadRequestException("The selected service is not offered by this business.");
        if (service.Type == "AtHome" && string.IsNullOrWhiteSpace(r.ServiceAddress))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["serviceAddress"] = ["Service address is required for at-home services."] });
        }

        // Re-validate the slot server-side; the UI may be showing stale availability.
        var start = r.ScheduledStart.ToOffset(IndianTime.Offset);
        var slots = await SlotCalculator.GetSlotsAsync(uow, business.Id, service.Id, start.Date, ct);
        if (!slots.Any(s => s.Available && s.Start == start))
        {
            throw new ConflictException("That time slot is no longer available. Please pick another slot.");
        }

        var customer = await uow.Repository<ApplicationUser>().QueryNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.PhoneNumber }).FirstAsync(ct);

        var booking = new Booking
        {
            BookingNumber = References.New("BK"),
            BusinessId = business.Id,
            ServiceId = service.Id,
            CustomerUserId = userId,
            CustomerName = customer.DisplayName,
            CustomerPhone = string.IsNullOrWhiteSpace(r.ContactPhone) ? customer.PhoneNumber ?? string.Empty : r.ContactPhone.Trim(),
            ScheduledStart = start,
            ScheduledEnd = start.AddMinutes(Math.Clamp(service.DurationMinutes, 15, 240)),
            Status = BookingStatuses.Pending,
            Amount = service.Price,
            PaymentStatus = service.Price == 0 ? "NotApplicable" : "Pending",
            ServiceAddress = r.ServiceAddress?.Trim(),
            Notes = r.Notes?.Trim()
        };
        uow.Repository<Booking>().Add(booking);
        await uow.SaveChangesAsync(ct);

        await NotificationPublisher.PublishAsync(uow, notifier, business.OwnerUserId, "New booking",
            $"{booking.CustomerName} booked {service.Name} for {start:dd MMM, h:mm tt}", "Booking", "/business/bookings", ct);

        return new CreatedReferenceDto(booking.Id, booking.BookingNumber);
    }
}

public sealed record MyBookingDto(Guid Id, string BookingNumber, string BusinessName, string BusinessSlug, string? BusinessLogoUrl, string ServiceName,
    DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, string Status, decimal Amount, string PaymentStatus, string? ServiceAddress,
    string? BusinessPhone, bool CanCancel, bool CanReview);

public sealed record GetMyBookingsQuery : PagedRequest, IRequest<PagedResult<MyBookingDto>>
{
    /// <summary>upcoming | past | all</summary>
    public string? Scope { get; init; }
}

public sealed class GetMyBookingsHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetMyBookingsQuery, PagedResult<MyBookingDto>>
{
    public async Task<PagedResult<MyBookingDto>> Handle(GetMyBookingsQuery r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var now = DateTimeOffset.UtcNow;
        var cancelCutoff = now.AddHours(2);
        var query = uow.Repository<Booking>().QueryNoTracking().Where(b => b.CustomerUserId == userId);

        query = r.Scope switch
        {
            "upcoming" => query.Where(b => b.ScheduledStart >= now && (b.Status == BookingStatuses.Pending || b.Status == BookingStatuses.Confirmed)).OrderBy(b => b.ScheduledStart),
            "past" => query.Where(b => b.ScheduledStart < now || b.Status == BookingStatuses.Cancelled).OrderByDescending(b => b.ScheduledStart),
            _ => query.OrderByDescending(b => b.ScheduledStart)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip(r.Skip).Take(r.PageSize)
            .Select(b => new MyBookingDto(b.Id, b.BookingNumber, b.Business.Name, b.Business.Slug, b.Business.LogoUrl, b.Service.Name,
                b.ScheduledStart, b.ScheduledEnd, b.Status, b.Amount, b.PaymentStatus, b.ServiceAddress, b.Business.PhoneNumber,
                (b.Status == BookingStatuses.Pending || b.Status == BookingStatuses.Confirmed) && b.ScheduledStart > cancelCutoff,
                b.Status == BookingStatuses.Completed && !b.Business.Reviews.Any(rv => rv.CustomerUserId == userId)))
            .ToListAsync(ct);
        return PagedResult<MyBookingDto>.Create(items, r.Page, r.PageSize, total);
    }
}

public sealed record CancelMyBookingCommand(Guid BookingId, string? Reason) : IRequest;

public sealed class CancelMyBookingHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier) : IRequestHandler<CancelMyBookingCommand>
{
    public async Task Handle(CancelMyBookingCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var booking = await uow.Repository<Booking>().Query().Include(b => b.Business).Include(b => b.Service)
            .FirstOrDefaultAsync(b => b.Id == r.BookingId && b.CustomerUserId == userId, ct)
            ?? throw new NotFoundException("Booking", r.BookingId);

        if (booking.Status is not (BookingStatuses.Pending or BookingStatuses.Confirmed))
            throw new BadRequestException("Only pending or confirmed bookings can be cancelled.");
        if (booking.ScheduledStart <= DateTimeOffset.UtcNow.AddHours(2))
            throw new BadRequestException("Bookings can be cancelled up to 2 hours before the scheduled time.");

        booking.Status = BookingStatuses.Cancelled;
        booking.CancellationReason = string.IsNullOrWhiteSpace(r.Reason) ? "Cancelled by customer" : r.Reason.Trim();
        if (booking.PaymentStatus == "Paid") booking.PaymentStatus = "Refunded";
        await uow.SaveChangesAsync(ct);

        await NotificationPublisher.PublishAsync(uow, notifier, booking.Business.OwnerUserId, "Booking cancelled",
            $"{booking.CustomerName} cancelled {booking.Service.Name} on {booking.ScheduledStart.ToOffset(IndianTime.Offset):dd MMM, h:mm tt}",
            "Booking", "/business/bookings", ct);
    }
}

// ===================== Favourites =====================

public sealed record ToggleFavoriteCommand(Guid BusinessId) : IRequest<bool>;

public sealed class ToggleFavoriteHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<ToggleFavoriteCommand, bool>
{
    public async Task<bool> Handle(ToggleFavoriteCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        if (!await uow.Repository<Business>().QueryNoTracking().Listed().AnyAsync(b => b.Id == r.BusinessId, ct))
            throw new NotFoundException("Business", r.BusinessId);

        var favorites = uow.Repository<Favorite>();
        var existing = await favorites.Query().FirstOrDefaultAsync(f => f.UserId == userId && f.BusinessId == r.BusinessId, ct);
        if (existing is not null)
        {
            favorites.Remove(existing);
            await uow.SaveChangesAsync(ct);
            return false;
        }

        favorites.Add(new Favorite { UserId = userId, BusinessId = r.BusinessId, CreatedOn = DateTimeOffset.UtcNow });
        await uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record GetMyFavoritesQuery : IRequest<IReadOnlyList<BusinessCardDto>>;

public sealed class GetMyFavoritesHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetMyFavoritesQuery, IReadOnlyList<BusinessCardDto>>
{
    public async Task<IReadOnlyList<BusinessCardDto>> Handle(GetMyFavoritesQuery request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var ids = uow.Repository<Favorite>().QueryNoTracking().Where(f => f.UserId == userId).Select(f => f.BusinessId);
        return await uow.Repository<Business>().QueryNoTracking().Listed()
            .Where(b => ids.Contains(b.Id))
            .OrderBy(b => b.Name)
            .Select(BusinessCards.ToCard(IndianTime.Now))
            .ToListAsync(ct);
    }
}

// ===================== My enquiries =====================

public sealed record MyEnquiryDto(Guid Id, string EnquiryNumber, string BusinessName, string BusinessSlug, string? ServiceName, string EnquiryType,
    string Message, string Status, decimal? QuotedAmount, DateTimeOffset CreatedOn, DateTimeOffset? RespondedOn);

public sealed record GetMyEnquiriesQuery : PagedRequest, IRequest<PagedResult<MyEnquiryDto>>;

public sealed class GetMyEnquiriesHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetMyEnquiriesQuery, PagedResult<MyEnquiryDto>>
{
    public async Task<PagedResult<MyEnquiryDto>> Handle(GetMyEnquiriesQuery r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var query = uow.Repository<Enquiry>().QueryNoTracking().Where(e => e.CustomerUserId == userId).OrderByDescending(e => e.CreatedOn);
        var total = await query.CountAsync(ct);
        var items = await query.Skip(r.Skip).Take(r.PageSize)
            .Select(e => new MyEnquiryDto(e.Id, e.EnquiryNumber, e.Business.Name, e.Business.Slug, e.Service != null ? e.Service.Name : null,
                e.EnquiryType, e.Message, e.Status, e.QuotedAmount, e.CreatedOn, e.RespondedOn))
            .ToListAsync(ct);
        return PagedResult<MyEnquiryDto>.Create(items, r.Page, r.PageSize, total);
    }
}
