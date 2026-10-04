using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Owner;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Admin;

public sealed record MonthlyPointDto(string Month, DateTime MonthStart, IReadOnlyDictionary<string, decimal> Values);
public sealed record DailyPointDto(DateTime Date, int Dau, int Mau, int NewUsers);
public sealed record DistributionDto(string Key, string Name, int Count, decimal? Value = null, string? ColorHex = null);
public sealed record AttentionDto(int PendingApprovals, int PendingVerifications, int FlaggedReviews, int PendingAds);

public sealed record AdminDashboardDto(
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<MonthlyPointDto> Registrations,
    IReadOnlyList<MonthlyPointDto> Leads,
    IReadOnlyList<MonthlyPointDto> Bookings,
    IReadOnlyList<MonthlyPointDto> Revenue,
    IReadOnlyList<DailyPointDto> ActiveUsers,
    IReadOnlyList<DistributionDto> Categories,
    IReadOnlyList<DistributionDto> Cities,
    IReadOnlyList<DistributionDto> Subscriptions,
    AttentionDto Attention);

public sealed record GetAdminDashboardQuery : IRequest<AdminDashboardDto>;

public sealed class GetAdminDashboardHandler(IUnitOfWork uow) : IRequestHandler<GetAdminDashboardQuery, AdminDashboardDto>
{
    public async Task<AdminDashboardDto> Handle(GetAdminDashboardQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = IndianTime.Today;
        var d30 = now.AddDays(-30);
        var d60 = now.AddDays(-60);

        var businesses = uow.Repository<Business>().QueryNoTracking();
        var users = uow.Repository<ApplicationUser>().QueryNoTracking();
        var leads = uow.Repository<Enquiry>().QueryNoTracking();
        var bookings = uow.Repository<Booking>().QueryNoTracking();
        var payments = uow.Repository<Payment>().QueryNoTracking().Where(p => p.Status == "Success");
        var reviews = uow.Repository<Review>().QueryNoTracking();
        var subs = uow.Repository<BusinessSubscription>().QueryNoTracking()
            .Where(s => (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today);
        var ads = uow.Repository<Advertisement>().QueryNoTracking();
        var platform = uow.Repository<PlatformDailyStat>().QueryNoTracking();

        var newBiz30 = await businesses.CountAsync(b => b.CreatedOn >= d30, ct);
        var newBizPrev = await businesses.CountAsync(b => b.CreatedOn >= d60 && b.CreatedOn < d30, ct);
        var leads30 = await leads.CountAsync(e => e.CreatedOn >= d30, ct);
        var leadsPrev = await leads.CountAsync(e => e.CreatedOn >= d60 && e.CreatedOn < d30, ct);
        var bookings30 = await bookings.CountAsync(b => b.CreatedOn >= d30, ct);
        var bookingsPrev = await bookings.CountAsync(b => b.CreatedOn >= d60 && b.CreatedOn < d30, ct);
        var revenue30 = await payments.Where(p => p.PaidOn >= d30).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var revenuePrev = await payments.Where(p => p.PaidOn >= d60 && p.PaidOn < d30).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var customers = await users.CountAsync(u => u.UserType == "Customer", ct);
        var customersNew30 = await users.CountAsync(u => u.UserType == "Customer" && u.CreatedOn >= d30, ct);
        var latestDay = await platform.OrderByDescending(p => p.StatDate).FirstOrDefaultAsync(ct);
        var dauPrev = await platform.Where(p => p.StatDate <= today.AddDays(-30)).OrderByDescending(p => p.StatDate).Select(p => p.ActiveUsers).FirstOrDefaultAsync(ct);
        var publishedReviews = reviews.Where(r => r.Status == ReviewStatuses.Published);

        var kpis = new List<KpiDto>
        {
            new("totalBusinesses", "Total businesses", await businesses.CountAsync(ct), null, "number"),
            new("activeBusinesses", "Active businesses", await businesses.CountAsync(b => b.Status == BusinessStatuses.Active, ct), null, "number"),
            new("newBusinesses", "New businesses (30d)", newBiz30, OwnerAccess.Delta(newBiz30, newBizPrev), "number"),
            new("customers", "Total customers", customers, customersNew30 == 0 ? null : Math.Round(customersNew30 * 100m / Math.Max(1, customers - customersNew30), 1), "number"),
            new("leads", "Leads (30d)", leads30, OwnerAccess.Delta(leads30, leadsPrev), "number"),
            new("newEnquiries", "New enquiries", await leads.CountAsync(e => e.Status == EnquiryStatuses.New, ct), null, "number"),
            new("bookings", "Bookings (30d)", bookings30, OwnerAccess.Delta(bookings30, bookingsPrev), "number"),
            new("pendingBookings", "Pending bookings", await bookings.CountAsync(b => b.Status == BookingStatuses.Pending, ct), null, "number"),
            new("revenue", "Revenue (30d)", revenue30, OwnerAccess.Delta(revenue30, revenuePrev), "currency"),
            new("activeSubscriptions", "Paid subscriptions", await subs.CountAsync(s => s.Plan.Code != "FREE", ct), null, "number"),
            new("advertisements", "Live campaigns", await ads.CountAsync(a => a.Status == AdStatuses.Active && a.StartDate <= today && a.EndDate >= today, ct), null, "number"),
            new("reviews", "Published reviews", await publishedReviews.CountAsync(ct), null, "number"),
            new("averageRating", "Average rating", Math.Round(await publishedReviews.AverageAsync(r => (decimal?)r.Rating, ct) ?? 0, 2), null, "rating"),
            new("dau", "Daily active users", latestDay?.ActiveUsers ?? 0, latestDay is null ? null : OwnerAccess.Delta(latestDay.ActiveUsers, dauPrev), "number"),
            new("mau", "Monthly active users", latestDay?.MonthlyActiveUsers ?? 0, null, "number")
        };

        // ---- Last 12 complete months (the current, partial month would show a false drop) ----
        var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-12);
        var firstMonthOffset = IndianTime.At(firstMonth, TimeSpan.Zero);
        var months = Enumerable.Range(0, 12).Select(i => firstMonth.AddMonths(i)).ToList();

        var bizCreated = await businesses.Where(b => b.CreatedOn >= firstMonthOffset).Select(b => b.CreatedOn).ToListAsync(ct);
        var custCreated = await users.Where(u => u.UserType == "Customer" && u.CreatedOn >= firstMonthOffset).Select(u => u.CreatedOn).ToListAsync(ct);
        var leadRows = await leads.Where(e => e.CreatedOn >= firstMonthOffset).Select(e => new { e.CreatedOn, e.Status }).ToListAsync(ct);
        var bookingRows = await bookings.Where(b => b.ScheduledStart >= firstMonthOffset && b.ScheduledStart <= now).Select(b => new { b.ScheduledStart, b.Status }).ToListAsync(ct);
        var paymentRows = await payments.Where(p => p.PaidOn >= firstMonthOffset).Select(p => new { p.PaidOn, p.PaymentType, p.Amount }).ToListAsync(ct);

        static DateTime MonthOf(DateTimeOffset d) { var l = d.ToOffset(IndianTime.Offset); return new DateTime(l.Year, l.Month, 1); }
        MonthlyPointDto Point(DateTime m, Dictionary<string, decimal> v) => new(m.ToString("MMM yy"), m, v);

        var registrations = months.Select(m => Point(m, new()
        {
            ["businesses"] = bizCreated.Count(d => MonthOf(d) == m),
            ["customers"] = custCreated.Count(d => MonthOf(d) == m)
        })).ToList();
        var leadSeries = months.Select(m => Point(m, new()
        {
            ["total"] = leadRows.Count(r => MonthOf(r.CreatedOn) == m),
            ["converted"] = leadRows.Count(r => MonthOf(r.CreatedOn) == m && r.Status == EnquiryStatuses.Converted)
        })).ToList();
        var bookingSeries = months.Select(m => Point(m, new()
        {
            ["total"] = bookingRows.Count(r => MonthOf(r.ScheduledStart) == m),
            ["completed"] = bookingRows.Count(r => MonthOf(r.ScheduledStart) == m && r.Status == BookingStatuses.Completed),
            ["cancelled"] = bookingRows.Count(r => MonthOf(r.ScheduledStart) == m && r.Status == BookingStatuses.Cancelled)
        })).ToList();
        var revenueSeries = months.Select(m => Point(m, new()
        {
            ["Subscription"] = paymentRows.Where(p => MonthOf(p.PaidOn) == m && p.PaymentType == "Subscription").Sum(p => p.Amount),
            ["Advertisement"] = paymentRows.Where(p => MonthOf(p.PaidOn) == m && p.PaymentType == "Advertisement").Sum(p => p.Amount),
            ["BookingCommission"] = paymentRows.Where(p => MonthOf(p.PaidOn) == m && p.PaymentType == "BookingCommission").Sum(p => p.Amount),
            ["LeadCredits"] = paymentRows.Where(p => MonthOf(p.PaidOn) == m && p.PaymentType == "LeadCredits").Sum(p => p.Amount)
        })).ToList();

        var activeUsers = await platform.Where(p => p.StatDate > today.AddDays(-90)).OrderBy(p => p.StatDate)
            .Select(p => new DailyPointDto(p.StatDate, p.ActiveUsers, p.MonthlyActiveUsers, p.NewUsers)).ToListAsync(ct);

        // ---- Distributions ----
        var listed = businesses.Where(b => b.Status == BusinessStatuses.Active);
        var categories = (await uow.Repository<Category>().QueryNoTracking().Where(c => c.IsActive)
            .Select(c => new DistributionDto(c.Slug, c.Name, listed.Count(b => b.CategoryId == c.Id), null, c.ColorHex))
            .ToListAsync(ct)).OrderByDescending(d => d.Count).ToList();
        var cities = (await uow.Repository<City>().QueryNoTracking().Where(c => c.IsActive)
            .Select(c => new DistributionDto(c.Slug, c.Name, listed.Count(b => b.CityId == c.Id), null, null))
            .ToListAsync(ct)).Where(d => d.Count > 0).OrderByDescending(d => d.Count).ToList(); // the catalogue has every city of the country
        var subscriptionRows = await subs.GroupBy(s => new { s.Plan.Code, s.Plan.Name, s.Plan.SortOrder, s.Plan.BadgeColor })
            .Select(g => new
            {
                g.Key.Code, g.Key.Name, g.Key.SortOrder, g.Key.BadgeColor, Count = g.Count(),
                Mrr = g.Sum(s => s.BillingCycle == "Annual" ? s.Amount / 12 : s.Amount)
            })
            .ToListAsync(ct);
        var subscriptions = subscriptionRows.OrderBy(s => s.SortOrder)
            .Select(s => new DistributionDto(s.Code, s.Name, s.Count, Math.Round(s.Mrr, 0), s.BadgeColor)).ToList();

        var attention = new AttentionDto(
            await businesses.CountAsync(b => b.Status == BusinessStatuses.PendingApproval, ct),
            await businesses.CountAsync(b => b.VerificationStatus == VerificationStatuses.Pending, ct),
            await reviews.CountAsync(r => r.Status == ReviewStatuses.Flagged, ct),
            await ads.CountAsync(a => a.Status == AdStatuses.PendingApproval, ct));

        return new AdminDashboardDto(kpis, registrations, leadSeries, bookingSeries, revenueSeries, activeUsers, categories, cities, subscriptions, attention);
    }
}
