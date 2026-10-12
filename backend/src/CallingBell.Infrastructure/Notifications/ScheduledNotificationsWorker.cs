using CallingBell.Application.Features.Notifications;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CallingBell.Infrastructure.Notifications;

/// <summary>
/// Runs the scheduled notifications (booking reminders, subscription expiry, offers) every few minutes
/// (Notifications:Scheduled:IntervalMinutes, default 15; Notifications:Scheduled:Enabled to switch off). Each notification is sent once,
/// so several instances or a restart never send it twice.
/// </summary>
internal sealed class ScheduledNotificationsWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ScheduledNotificationsWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:Scheduled:Enabled", true)) { logger.LogInformation("Scheduled notifications are disabled"); return; }
        var every = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Notifications:Scheduled:IntervalMinutes", 15)));
        // Let the app finish starting first.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SendScheduledNotificationsRequest(), stoppingToken);
                if (result.BookingReminders + result.SubscriptionReminders + result.Offers > 0)
                    logger.LogInformation("Scheduled notifications: {Reminders} booking reminders, {Subscriptions} subscription, {Offers} offers",
                        result.BookingReminders, result.SubscriptionReminders, result.Offers);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled notifications run failed");
            }
            await Task.Delay(every, stoppingToken);
        }
    }
}
