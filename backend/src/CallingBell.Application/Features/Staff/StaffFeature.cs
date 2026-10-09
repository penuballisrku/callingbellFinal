using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Notifications;
using CallingBell.Application.Features.Onboarding;
using CallingBell.Application.Features.Owner;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;

namespace CallingBell.Application.Features.Staff;

// ===================== DTOs =====================

/// <summary>One day's hours (0 = Sunday), "HH:mm"; no open/close and not closed = the business's hours.</summary>
public sealed record StaffHourDto(int DayOfWeek, string? Open, string? Close, bool IsClosed);

/// <summary>A team member as the owner sees them (with private contact details and workload).</summary>
public sealed record OwnerStaffDto(Guid Id, string FullName, string? Title, string? Phone, string? Email, string? Bio, int? YearsExperience,
    string? Languages, bool AcceptsBookings, bool IsActive, int SortOrder, IReadOnlyList<Guid> ServiceIds, IReadOnlyList<StaffHourDto> Hours,
    int UpcomingBookings, int CompletedThisMonth);

/// <summary>A team member as customers see them: no phone or email.</summary>
public sealed record TeamMemberDto(Guid Id, string FullName, string? Title, string? Bio, int? YearsExperience, string? Languages,
    IReadOnlyList<Guid> ServiceIds, bool AcceptsBookings);

// ===================== Schedule (slots and assignment) =====================

/// <summary>
/// Who can do a service on a day: active team members who accept bookings and do that service, with their working window (their own
/// hours, else the business's) and their pending/confirmed bookings. Shared by booking slots, automatic assignment and the owner's
/// manual assignment, so all three agree.
/// </summary>
public sealed class StaffSchedule
{
    private static readonly string[] Blocking = [BookingStatuses.Pending, BookingStatuses.Confirmed];

    public sealed record Member(Guid Id, string FullName, TimeSpan? Open, TimeSpan? Close, List<(DateTimeOffset Start, DateTimeOffset End, Guid BookingId)> Busy)
    {
        /// <summary>Working then and not booked (another booking than <paramref name="except"/>).</summary>
        public bool IsFree(DateTimeOffset start, DateTimeOffset end, Guid? except = null)
        {
            if (Open is not { } open || Close is not { } close) return false;
            var s = start.ToOffset(IndianTime.Offset);
            var e = end.ToOffset(IndianTime.Offset);
            if (s.Date != e.Date && e.TimeOfDay != TimeSpan.Zero) return false;
            var endTime = e.TimeOfDay == TimeSpan.Zero && e.Date > s.Date ? TimeSpan.FromDays(1) : e.TimeOfDay;
            if (s.TimeOfDay < open || endTime > close) return false;
            return !Busy.Any(b => b.BookingId != except && b.Start < end && b.End > start);
        }
    }

    public IReadOnlyList<Member> Members { get; private init; } = [];
    /// <summary>Bookings at the business on that day that nobody is assigned to yet (they still take someone's time).</summary>
    public List<(DateTimeOffset Start, DateTimeOffset End, Guid BookingId)> Unassigned { get; private init; } = [];

    public bool HasTeam => Members.Count > 0;

    /// <summary>Members free then, minus the unassigned bookings overlapping it (each needs one of them).</summary>
    public int FreeCount(DateTimeOffset start, DateTimeOffset end, Guid? except = null) =>
        Members.Count(m => m.IsFree(start, end, except)) - Unassigned.Count(b => b.BookingId != except && b.Start < end && b.End > start);

    public static async Task<StaffSchedule> LoadAsync(IUnitOfWork uow, Guid businessId, Guid serviceId, DateTime date, CancellationToken ct)
    {
        var day = (byte)date.DayOfWeek;
        var businessHours = await uow.Repository<BusinessHour>().QueryNoTracking().FirstOrDefaultAsync(h => h.BusinessId == businessId && h.DayOfWeek == day, ct);
        var staff = await uow.Repository<BusinessStaff>().QueryNoTracking()
            .Where(s => s.BusinessId == businessId && s.IsActive && s.AcceptsBookings && s.Services.Any(x => x.ServiceId == serviceId))
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id, s.FullName, Hours = s.Hours.Where(h => h.DayOfWeek == day).Select(h => new { h.OpenTime, h.CloseTime, h.IsClosed }).FirstOrDefault() })
            .ToListAsync(ct);
        if (staff.Count == 0) return new StaffSchedule();

        var dayStart = IndianTime.At(date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var bookings = await uow.Repository<Booking>().QueryNoTracking()
            .Where(b => b.BusinessId == businessId && Blocking.Contains(b.Status) && b.ScheduledStart < dayEnd && b.ScheduledEnd > dayStart)
            .Select(b => new { b.Id, b.StaffId, b.ScheduledStart, b.ScheduledEnd }).ToListAsync(ct);

        var members = staff.Select(s =>
        {
            TimeSpan? open, close;
            if (s.Hours is { } h) (open, close) = h.IsClosed ? (null, null) : (h.OpenTime, h.CloseTime);
            else (open, close) = businessHours is { IsClosed: false } ? (businessHours.OpenTime, businessHours.CloseTime) : (null, null);
            return new Member(s.Id, s.FullName, open, close,
                bookings.Where(b => b.StaffId == s.Id).Select(b => (b.ScheduledStart, b.ScheduledEnd, b.Id)).ToList());
        }).ToList();
        return new StaffSchedule
        {
            Members = members,
            // Bookings of people who don't do this service still block them, but they aren't in this list: only unassigned ones count here.
            Unassigned = bookings.Where(b => b.StaffId is null).Select(b => (b.ScheduledStart, b.ScheduledEnd, b.Id)).ToList(),
        };
    }

    /// <summary>The free member with the fewest bookings that day (spreads the work), or null.</summary>
    public Member? PickFree(DateTimeOffset start, DateTimeOffset end) =>
        FreeCount(start, end) <= 0 ? null : Members.Where(m => m.IsFree(start, end)).OrderBy(m => m.Busy.Count).FirstOrDefault();
}

// ===================== Owner: list and edit =====================

public sealed record GetOwnerStaffQuery(Guid BusinessId) : IRequest<IReadOnlyList<OwnerStaffDto>>;

public sealed class GetOwnerStaffHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerStaffQuery, IReadOnlyList<OwnerStaffDto>>
{
    public async Task<IReadOnlyList<OwnerStaffDto>> Handle(GetOwnerStaffQuery r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var now = DateTimeOffset.UtcNow;
        var monthStart = IndianTime.At(new DateTime(IndianTime.Today.Year, IndianTime.Today.Month, 1), TimeSpan.Zero);
        var rows = await uow.Repository<BusinessStaff>().QueryNoTracking().Where(s => s.BusinessId == r.BusinessId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.FullName)
            .Select(s => new
            {
                s.Id, s.FullName, s.Title, s.Phone, s.Email, s.Bio, s.YearsExperience, s.Languages, s.AcceptsBookings, s.IsActive, s.SortOrder,
                ServiceIds = s.Services.Select(x => x.ServiceId).ToList(),
                Hours = s.Hours.Select(h => new { h.DayOfWeek, h.OpenTime, h.CloseTime, h.IsClosed }).ToList(),
            })
            .ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var bookings = await uow.Repository<Booking>().QueryNoTracking()
            .Where(b => b.StaffId != null && ids.Contains(b.StaffId.Value) && (b.ScheduledStart >= now && (b.Status == BookingStatuses.Pending || b.Status == BookingStatuses.Confirmed)
                        || b.Status == BookingStatuses.Completed && b.ScheduledStart >= monthStart))
            .GroupBy(b => b.StaffId!.Value)
            .Select(g => new { StaffId = g.Key, Upcoming = g.Count(b => b.Status != BookingStatuses.Completed), Completed = g.Count(b => b.Status == BookingStatuses.Completed) })
            .ToListAsync(ct);
        return rows.Select(s =>
        {
            var w = bookings.FirstOrDefault(b => b.StaffId == s.Id);
            return new OwnerStaffDto(s.Id, s.FullName, s.Title, s.Phone, s.Email, s.Bio, s.YearsExperience, s.Languages, s.AcceptsBookings, s.IsActive,
                s.SortOrder, s.ServiceIds,
                s.Hours.OrderBy(h => h.DayOfWeek).Select(h => new StaffHourDto(h.DayOfWeek, h.OpenTime?.ToString(@"hh\:mm"), h.CloseTime?.ToString(@"hh\:mm"), h.IsClosed)).ToList(),
                w?.Upcoming ?? 0, w?.Completed ?? 0);
        }).ToList();
    }
}

/// <param name="StaffId">Null to add a new member.</param>
/// <param name="Hours">Days with their own hours; days left out follow the business's hours.</param>
public sealed record SaveStaffCommand(Guid BusinessId, Guid? StaffId, string FullName, string? Title, string? Phone, string? Email, string? Bio,
    int? YearsExperience, string? Languages, bool AcceptsBookings, bool IsActive, IReadOnlyList<Guid> ServiceIds, IReadOnlyList<StaffHourDto>? Hours)
    : IRequest<OwnerStaffDto>;

public sealed class SaveStaffValidator : AbstractValidator<SaveStaffCommand>
{
    public SaveStaffValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Enter the name.").MaximumLength(120);
        RuleFor(x => x.Title).MaximumLength(80);
        RuleFor(x => x.Phone).Must(p => Phones.ToE164(p) is not null).When(x => !string.IsNullOrWhiteSpace(x.Phone)).WithMessage("Enter a valid phone number.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Bio).MaximumLength(500);
        RuleFor(x => x.YearsExperience).InclusiveBetween(0, 70).When(x => x.YearsExperience.HasValue);
        RuleFor(x => x.Languages).MaximumLength(200);
        RuleFor(x => x.ServiceIds).NotNull().Must(s => s.Count <= 200);
        RuleFor(x => x.Hours).Must(h => h!.Count <= 7 && h.Select(d => d.DayOfWeek).Distinct().Count() == h.Count).When(x => x.Hours is { Count: > 0 })
            .WithMessage("Give each day's hours once.");
        RuleForEach(x => x.Hours).ChildRules(h =>
        {
            h.RuleFor(x => x.DayOfWeek).InclusiveBetween(0, 6);
            h.RuleFor(x => x).Must(x => x.IsClosed || OnboardingRules.TryTime(x.Open, out var o) && OnboardingRules.TryTime(x.Close, out var c) && c > o)
                .WithName("hours").WithMessage("Enter an opening time before the closing time, or mark the day off.");
        });
    }
}

public sealed class SaveStaffHandler(IUnitOfWork uow, ICurrentUser user, ISender sender) : IRequestHandler<SaveStaffCommand, OwnerStaffDto>
{
    public async Task<OwnerStaffDto> Handle(SaveStaffCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var serviceIds = r.ServiceIds.Distinct().ToList();
        var valid = await uow.Repository<BusinessService>().QueryNoTracking().Where(s => s.BusinessId == r.BusinessId && serviceIds.Contains(s.Id))
            .Select(s => s.Id).ToListAsync(ct);
        if (valid.Count != serviceIds.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["serviceIds"] = ["Choose services this business offers."] });

        var repo = uow.Repository<BusinessStaff>();
        BusinessStaff staff;
        if (r.StaffId is { } id)
        {
            staff = await repo.Query().Include(s => s.Services).Include(s => s.Hours).FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == r.BusinessId, ct)
                    ?? throw new NotFoundException("Team member", id);
        }
        else
        {
            var last = await repo.QueryNoTracking().Where(s => s.BusinessId == r.BusinessId).MaxAsync(s => (int?)s.SortOrder, ct) ?? -1;
            staff = new BusinessStaff { BusinessId = r.BusinessId, SortOrder = last + 1 };
            repo.Add(staff);
        }

        staff.FullName = JoinImport.Clean(r.FullName, 120)!;
        staff.Title = JoinImport.Clean(r.Title, 80);
        staff.Phone = string.IsNullOrWhiteSpace(r.Phone) ? null : OnboardingRules.IsIndianPhone(r.Phone) ? Phones.Normalize(r.Phone) : Phones.ToE164(r.Phone);
        staff.Email = JoinImport.CleanEmail(r.Email);
        staff.Bio = JoinImport.Clean(r.Bio, 500);
        staff.YearsExperience = r.YearsExperience;
        staff.Languages = JoinImport.Clean(r.Languages, 200);
        staff.AcceptsBookings = r.AcceptsBookings;
        staff.IsActive = r.IsActive;

        staff.Services.Where(x => !serviceIds.Contains(x.ServiceId)).ToList().ForEach(x => staff.Services.Remove(x));
        foreach (var sid in serviceIds.Where(sid => staff.Services.All(x => x.ServiceId != sid)))
            staff.Services.Add(new BusinessStaffService { StaffId = staff.Id, ServiceId = sid });

        if (r.Hours is not null)
        {
            staff.Hours.ToList().ForEach(h => staff.Hours.Remove(h));
            foreach (var h in r.Hours)
            {
                OnboardingRules.TryTime(h.Open, out var open);
                OnboardingRules.TryTime(h.Close, out var close);
                staff.Hours.Add(new BusinessStaffHour { StaffId = staff.Id, DayOfWeek = (byte)h.DayOfWeek, IsClosed = h.IsClosed, OpenTime = h.IsClosed ? null : open, CloseTime = h.IsClosed ? null : close });
            }
        }
        await uow.SaveChangesAsync(ct);
        return (await sender.Send(new GetOwnerStaffQuery(r.BusinessId), ct)).Single(s => s.Id == staff.Id);
    }
}

/// <summary>Removes a team member. Their upcoming bookings become unassigned (the count is returned) so the owner can reassign them.</summary>
public sealed record DeleteStaffCommand(Guid BusinessId, Guid StaffId) : IRequest<int>;

public sealed class DeleteStaffHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<DeleteStaffCommand, int>
{
    public async Task<int> Handle(DeleteStaffCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var staff = await uow.Repository<BusinessStaff>().Query().FirstOrDefaultAsync(s => s.Id == r.StaffId && s.BusinessId == r.BusinessId, ct)
                    ?? throw new NotFoundException("Team member", r.StaffId);
        var now = DateTimeOffset.UtcNow;
        var upcoming = await uow.Repository<Booking>().Query()
            .Where(b => b.StaffId == staff.Id && b.ScheduledStart >= now && (b.Status == BookingStatuses.Pending || b.Status == BookingStatuses.Confirmed)).ToListAsync(ct);
        upcoming.ForEach(b => b.StaffId = null);
        staff.IsDeleted = true;
        staff.IsActive = false;
        await uow.SaveChangesAsync(ct);
        return upcoming.Count;
    }
}

// ===================== Public team =====================

public sealed record GetBusinessTeamQuery(Guid BusinessId) : IRequest<IReadOnlyList<TeamMemberDto>>;

public sealed class GetBusinessTeamHandler(IUnitOfWork uow) : IRequestHandler<GetBusinessTeamQuery, IReadOnlyList<TeamMemberDto>>
{
    public async Task<IReadOnlyList<TeamMemberDto>> Handle(GetBusinessTeamQuery r, CancellationToken ct) =>
        await uow.Repository<BusinessStaff>().QueryNoTracking()
            .Where(s => s.BusinessId == r.BusinessId && s.IsActive && s.Business.Status == BusinessStatuses.Active)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.FullName)
            .Select(s => new TeamMemberDto(s.Id, s.FullName, s.Title, s.Bio, s.YearsExperience, s.Languages,
                s.Services.Select(x => x.ServiceId).ToList(), s.AcceptsBookings))
            .ToListAsync(ct);
}

// ===================== Assign a booking =====================

/// <param name="StaffId">Null to unassign.</param>
public sealed record AssignBookingStaffCommand(Guid BusinessId, Guid BookingId, Guid? StaffId) : IRequest<Unit>;

/// <summary>
/// Gives a booking to a team member who does the service, works at that time and has nothing else then. The customer is told who is coming.
/// </summary>
public sealed class AssignBookingStaffHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier) : IRequestHandler<AssignBookingStaffCommand, Unit>
{
    public async Task<Unit> Handle(AssignBookingStaffCommand r, CancellationToken ct)
    {
        await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var booking = await uow.Repository<Booking>().Query().Include(b => b.Service).Include(b => b.Business)
            .FirstOrDefaultAsync(b => b.Id == r.BookingId && b.BusinessId == r.BusinessId, ct) ?? throw new NotFoundException("Booking", r.BookingId);
        if (booking.Status is not (BookingStatuses.Pending or BookingStatuses.Confirmed))
            throw new BadRequestException("Only pending or confirmed bookings can be assigned.");
        if (booking.StaffId == r.StaffId) return Unit.Value;

        string? name = null;
        if (r.StaffId is { } staffId)
        {
            var schedule = await StaffSchedule.LoadAsync(uow, r.BusinessId, booking.ServiceId, booking.ScheduledStart.ToOffset(IndianTime.Offset).Date, ct);
            var member = schedule.Members.FirstOrDefault(m => m.Id == staffId)
                         ?? throw new BadRequestException("This team member doesn't do this service or isn't taking bookings.");
            if (!member.IsFree(booking.ScheduledStart, booking.ScheduledEnd, booking.Id))
                throw new ConflictException($"{member.FullName} isn't working then or already has a booking at that time.");
            name = member.FullName;
        }
        booking.StaffId = r.StaffId;
        await uow.SaveChangesAsync(ct);

        if (name is not null)
        {
            var when = booking.ScheduledStart.ToOffset(IndianTime.Offset).ToString("ddd d MMM, h:mm tt");
            await NotificationPublisher.PublishAsync(uow, notifier, booking.CustomerUserId, "Your professional is assigned",
                $"{name} from {booking.Business.Name} will take care of your {booking.Service.Name} on {when}.", "Booking", "/account/bookings", ct);
        }
        return Unit.Value;
    }
}
