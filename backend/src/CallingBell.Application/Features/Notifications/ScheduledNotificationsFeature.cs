using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Notifications;

/// <summary>What one run of the scheduled notifications sent.</summary>
public sealed record ScheduledNotificationsResult(int BookingReminders, int SubscriptionReminders, int Offers);

/// <summary>
/// Notifications nobody's action triggers, run every few minutes by a background worker. Each is sent once (idempotency key), so runs
/// can overlap or repeat safely. Every one reaches the person in the app and, through its route, as a push notification.
/// <list type="bullet">
/// <item>Booking reminder: the day before a confirmed booking (customer).</item>
/// <item>Subscription: 7 days and 1 day before a paid plan ends without a renewal (business owner).</item>
/// <item>Offers: active campaigns near customers who turned offers on, at most one a week each.</item>
/// </list>
/// </summary>
public sealed record SendScheduledNotificationsRequest : IRequest<ScheduledNotificationsResult>;

public sealed class SendScheduledNotificationsHandler(IUnitOfWork uow, IRealtimeNotifier notifier, INotificationDispatchSignal dispatch)
    : IRequestHandler<SendScheduledNotificationsRequest, ScheduledNotificationsResult>
{
    /// <summary>A booking made less than this long before its start already has it fresh in mind: no reminder.</summary>
    private static readonly TimeSpan MinLeadTimeForReminder = TimeSpan.FromHours(12);
    private static readonly int[] SubscriptionReminderDays = [7, 1];
    private static readonly TimeSpan OffersEvery = TimeSpan.FromDays(7);
    /// <summary>Offers sent per run, so a big campaign is spread over a few runs instead of one long one.</summary>
    private const int MaxOffersPerRun = 500;

    public async Task<ScheduledNotificationsResult> Handle(SendScheduledNotificationsRequest request, CancellationToken ct) =>
        new(await BookingRemindersAsync(ct), await SubscriptionRemindersAsync(ct), await OffersAsync(ct));

    private async Task<int> BookingRemindersAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await uow.Repository<Booking>().QueryNoTracking()
            .Where(b => b.Status == BookingStatuses.Confirmed && b.ScheduledStart > now && b.ScheduledStart <= now.AddHours(24))
            .Select(b => new { b.Id, b.CustomerUserId, b.ScheduledStart, b.CreatedOn, Service = b.Service.Name, Business = b.Business.Name })
            .ToListAsync(ct);
        var sent = 0;
        foreach (var b in due.Where(b => b.ScheduledStart - b.CreatedOn >= MinLeadTimeForReminder))
        {
            var start = b.ScheduledStart.ToOffset(IndianTime.Offset);
            var tomorrow = start.Date > IndianTime.Today;
            var key = $"{NotificationRoutes.BookingReminder}:{b.Id}";
            if (await AlreadySentAsync(key, ct)) continue;
            await NotificationPublisher.PublishAsync(uow, notifier, b.CustomerUserId,
                tomorrow ? "Your appointment is tomorrow" : "Your appointment is today",
                $"{b.Service} with {b.Business} {(tomorrow ? "tomorrow" : "today")} at {start:h:mm tt}.", "Booking", "/account?tab=upcoming", ct,
                new NotificationRouting(NotificationRoutes.BookingReminder, "Booking", b.Id, key), dispatch);
            sent++;
        }
        return sent;
    }

    private async Task<int> SubscriptionRemindersAsync(CancellationToken ct)
    {
        var today = IndianTime.Today;
        var last = today.AddDays(SubscriptionReminderDays.Max());
        var subs = uow.Repository<BusinessSubscription>().QueryNoTracking();
        // Paid plans ending soon that won't renew, and that no later subscription of the same business takes over from.
        var ending = await subs
            .Where(s => s.Status == SubscriptionStatuses.Active && !s.AutoRenew && s.Plan.MonthlyPrice > 0 && s.EndDate >= today && s.EndDate <= last
                        && !subs.Any(n => n.BusinessId == s.BusinessId && n.Id != s.Id && n.StartDate > s.EndDate.AddDays(-1)
                                          && (n.Status == SubscriptionStatuses.Active || n.Status == SubscriptionStatuses.Trial)))
            .Select(s => new { s.Id, s.EndDate, Plan = s.Plan.Name, Business = s.Business.Name, s.Business.OwnerUserId })
            .ToListAsync(ct);
        var sent = 0;
        foreach (var s in ending)
        {
            var daysLeft = (s.EndDate.Date - today).Days;
            // The nearest threshold reached (7 days, then 1 day); each once.
            var threshold = SubscriptionReminderDays.Where(d => daysLeft <= d).Min();
            var key = $"{NotificationRoutes.Subscription}:{s.Id}:{threshold}";
            if (await AlreadySentAsync(key, ct)) continue;
            var when = daysLeft switch { 0 => "today", 1 => "tomorrow", _ => $"on {s.EndDate:d MMM}" };
            await NotificationPublisher.PublishAsync(uow, notifier, s.OwnerUserId,
                daysLeft <= 1 ? $"Your subscription ends {when}" : "Your subscription expires soon",
                $"Your {s.Plan} plan for {s.Business} ends {when}. Renew from Plan & billing to keep your leads and placement.",
                "Subscription", "/owner/plan", ct,
                new NotificationRouting(NotificationRoutes.Subscription, "Subscription", s.Id, key), dispatch);
            sent++;
        }
        return sent;
    }

    private async Task<int> OffersAsync(CancellationToken ct)
    {
        var today = IndianTime.Today;
        var campaigns = await uow.Repository<Advertisement>().QueryNoTracking()
            .Where(a => a.Status == AdStatuses.Active && a.StartDate <= today && a.EndDate >= today)
            .OrderByDescending(a => a.StartDate)
            .Select(a => new { a.Id, a.Title, a.Description, CityId = a.TargetCityId ?? a.Business.CityId, a.Business.OwnerUserId, Business = a.Business.Name, a.Business.Slug })
            .ToListAsync(ct);
        if (campaigns.Count == 0) return 0;

        var since = DateTimeOffset.UtcNow - OffersEvery;
        var notifications = uow.Repository<Notification>().QueryNoTracking();
        var sent = 0;
        // Only campaigns with a place (the target city, else the business's own city): never everyone without a city.
        foreach (var c in campaigns.Where(c => c.CityId != null))
        {
            if (sent >= MaxOffersPerRun) break;
            var keyPrefix = $"{NotificationRoutes.Offers}:{c.Id}:";
            // Customers in the campaign's city who want offers, haven't had one this week and haven't had this one.
            var audience = await uow.Repository<ApplicationUser>().QueryNoTracking()
                .Where(u => u.ReceiveOffers && u.IsActive && u.CityId == c.CityId && u.Id != c.OwnerUserId
                            && !notifications.Any(n => n.UserId == u.Id && n.RouteCode == NotificationRoutes.Offers && n.CreatedOn >= since)
                            && !notifications.Any(n => n.UserId == u.Id && n.IdempotencyKey == keyPrefix + u.Id))
                .OrderBy(u => u.Id).Select(u => u.Id).Take(MaxOffersPerRun - sent).ToListAsync(ct);
            foreach (var userId in audience)
            {
                var detail = string.IsNullOrWhiteSpace(c.Description) ? c.Title : $"{c.Title}: {c.Description}";
                await NotificationPublisher.PublishAsync(uow, notifier, userId, "Discover offers near you",
                    $"{c.Business} · {(detail.Length > 140 ? detail[..140].TrimEnd() + "…" : detail)}", "Offer", $"/business/{c.Slug}", ct,
                    new NotificationRouting(NotificationRoutes.Offers, "Advertisement", c.Id, keyPrefix + userId), dispatch);
                sent++;
            }
        }
        return sent;
    }

    private Task<bool> AlreadySentAsync(string key, CancellationToken ct) =>
        uow.Repository<Notification>().QueryNoTracking().AnyAsync(n => n.IdempotencyKey == key, ct);
}
