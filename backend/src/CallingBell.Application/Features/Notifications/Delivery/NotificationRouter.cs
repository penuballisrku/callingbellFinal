using System.Text.RegularExpressions;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CallingBell.Application.Features.Notifications.Delivery;

/// <summary>
/// Works through a route's channels in priority order (dbo.NotificationRoutingRules), one at a time: a channel whose provider accepts the
/// message ends the route; an unavailable or failed channel moves on to the next. Channels are never sent all at once. Within a channel the
/// active providers for the recipient's country are tried in priority order (dbo.NotificationProviders). Each attempt is recorded in
/// dbo.NotificationDeliveries; its later fate (delivered, read, failed) comes from provider webhooks.
/// </summary>
public sealed partial class NotificationRouter(IUnitOfWork uow, IEnumerable<INotificationProvider> providers, IMemoryCache cache,
    ILogger<NotificationRouter> logger)
{
    private static readonly TimeSpan ConfigLifetime = TimeSpan.FromMinutes(5);

    /// <param name="fromStep">Route position to start at (0 = first channel); a webhook-reported failure continues from the next one.</param>
    /// <param name="maxAttemptsPerProvider">Tries per provider for temporary failures (timeouts, rate limits, 5xx), with exponential backoff.</param>
    /// <param name="skipChannels">Channels not to use this time (e.g. WhatsApp when the previous code sent there never arrived).</param>
    public async Task<NotificationDeliveryResult> RouteAsync(NotificationMessage message, DeliveryOwner owner, int fromStep = 0,
        int maxAttemptsPerProvider = 3, IReadOnlySet<string>? skipChannels = null, CancellationToken ct = default)
    {
        var rules = await RulesAsync(message.RouteCode, ct);
        var attempts = new List<DeliveryAttempt>();
        if (rules.Count == 0)
        {
            logger.LogWarning("Notification route {Route} has no active channels", message.RouteCode);
            return new NotificationDeliveryResult(false, null, null, null, fromStep, attempts);
        }

        var lastStep = fromStep;
        for (var step = Math.Max(0, fromStep); step < rules.Count; step++)
        {
            lastStep = step;
            var channel = rules[step];
            if (skipChannels?.Contains(channel) == true) continue;

            var template = await TemplateAsync(message.RouteCode, channel, message.Recipient.LanguageCode, ct);
            var candidates = await ProvidersAsync(channel, message.Recipient.CountryCode, ct);
            if (candidates.Count == 0 || NeedsTemplate(channel) && template is null)
            {
                var (code, reason) = candidates.Count == 0 ? ("NOT_CONFIGURED", "No configured provider for this channel.") : ("NO_TEMPLATE", "No active template for this channel.");
                await RecordAsync(owner, message, step, channel, "-", NotificationResult.Unavailable(code, reason), 0, ct);
                attempts.Add(new DeliveryAttempt(step, channel, "-", DeliveryStatuses.Unavailable, code));
                continue;
            }

            foreach (var provider in candidates)
            {
                var delivery = new NotificationDelivery
                {
                    NotificationId = owner.NotificationId, OtpCodeId = owner.OtpCodeId, RouteCode = message.RouteCode, RouteStep = step,
                    Channel = channel, Provider = provider.ProviderName, Status = DeliveryStatuses.Queued, CreatedAt = DateTimeOffset.UtcNow,
                };
                uow.Repository<NotificationDelivery>().Add(delivery);
                await uow.SaveChangesAsync(ct);

                var attempt = message with
                {
                    Channel = channel, DeliveryId = delivery.Id, TemplateName = template?.TemplateName, TemplateLanguage = template?.LanguageCode,
                    RenderedText = template is null ? null : Render(template.TemplateContent, message.TemplateParameters),
                    TemplateParameterCount = template is null ? 0 : ParameterCount(template.TemplateContent),
                };
                var (result, tries) = await SendWithRetryAsync(provider, attempt, maxAttemptsPerProvider, ct);
                Apply(delivery, result, tries);
                await uow.SaveChangesAsync(ct);
                attempts.Add(new DeliveryAttempt(step, channel, provider.ProviderName, delivery.Status, result.ErrorCode));

                logger.LogInformation("Notification {Route} via {Channel}/{Provider} to {Recipient}: {Status}{Error}", message.RouteCode, channel,
                    provider.ProviderName, delivery.Recipient ?? Phones.Mask(message.Recipient.PhoneE164), delivery.Status,
                    result.ErrorCode is null ? "" : $" ({result.ErrorCode})");

                if (result.Outcome == NotificationOutcome.Accepted)
                    return new NotificationDeliveryResult(true, channel, provider.ProviderName, delivery.Id, step, attempts);
            }
        }
        return new NotificationDeliveryResult(false, null, null, null, lastStep, attempts);
    }

    /// <summary>The route's channels in order (cached for a few minutes; edits to the table apply within that time).</summary>
    public async Task<IReadOnlyList<string>> RulesAsync(string routeCode, CancellationToken ct) =>
        (await cache.GetOrCreateAsync($"notify|rules|{routeCode}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = ConfigLifetime;
            return (IReadOnlyList<string>)await uow.Repository<NotificationRoutingRule>().QueryNoTracking()
                .Where(r => r.RouteCode == routeCode && r.IsActive).OrderBy(r => r.Priority).Select(r => r.Channel).ToListAsync(ct);
        }))!;

    private async Task<IReadOnlyList<INotificationProvider>> ProvidersAsync(string channel, string? country, CancellationToken ct)
    {
        var rows = (await cache.GetOrCreateAsync($"notify|providers|{channel}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = ConfigLifetime;
            return await uow.Repository<NotificationProvider>().QueryNoTracking()
                .Where(p => p.Channel == channel && p.IsActive)
                .Select(p => new ProviderRow(p.ProviderName, p.Priority, p.CountryCode)).ToListAsync(ct);
        }))!;
        return rows
            .Where(r => r.CountryCode == null || string.Equals(r.CountryCode, country, StringComparison.OrdinalIgnoreCase))
            // Same priority: the country's own provider before the worldwide one.
            .OrderBy(r => r.Priority).ThenBy(r => r.CountryCode == null)
            .Select(r => providers.FirstOrDefault(p => p.Channel == channel && string.Equals(p.ProviderName, r.ProviderName, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is { IsConfigured: true })
            .Cast<INotificationProvider>()
            .ToList();
    }

    private async Task<TemplateRow?> TemplateAsync(string code, string channel, string language, CancellationToken ct)
    {
        var rows = (await cache.GetOrCreateAsync($"notify|templates|{code}|{channel}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = ConfigLifetime;
            return await uow.Repository<NotificationTemplate>().QueryNoTracking()
                .Where(t => t.TemplateCode == code && t.Channel == channel && t.IsActive)
                .Select(t => new TemplateRow(t.LanguageCode, t.TemplateName, t.TemplateContent)).ToListAsync(ct);
        }))!;
        return rows.FirstOrDefault(t => t.LanguageCode == language) ?? rows.FirstOrDefault(t => t.LanguageCode == "en") ?? rows.FirstOrDefault();
    }

    private static bool NeedsTemplate(string channel) => channel != NotificationChannels.WebPush;

    private async Task<(NotificationResult Result, int Tries)> SendWithRetryAsync(INotificationProvider provider, NotificationMessage message,
        int maxAttempts, CancellationToken ct)
    {
        NotificationResult result = NotificationResult.Failed("NOT_SENT", "Not sent.");
        for (var attempt = 1; attempt <= Math.Max(1, maxAttempts); attempt++)
        {
            try
            {
                result = await provider.SendAsync(message, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result = NotificationResult.Failed("TIMEOUT", "The provider did not answer in time.", retryable: true);
            }
            catch (HttpRequestException ex)
            {
                result = NotificationResult.Failed("NETWORK", Truncate(ex.Message), retryable: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification provider {Provider} threw", provider.ProviderName);
                result = NotificationResult.Failed("PROVIDER_ERROR", "The provider failed unexpectedly.");
            }
            if (result.Outcome != NotificationOutcome.Failed || !result.Retryable || attempt == maxAttempts) return (result, attempt);
            // 0.5 s, 1 s, 2 s, … with a little jitter, so retries from many requests don't arrive together.
            var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 250));
            await Task.Delay(delay, ct);
        }
        return (result, maxAttempts);
    }

    private static void Apply(NotificationDelivery d, NotificationResult r, int tries)
    {
        var now = DateTimeOffset.UtcNow;
        d.AttemptCount = tries;
        d.UpdatedAt = now;
        d.Recipient = r.RecipientLabel ?? d.Recipient;
        d.ErrorCode = r.ErrorCode is null ? null : Truncate(r.ErrorCode, 60);
        d.ErrorMessage = r.ErrorMessage is null ? null : Truncate(r.ErrorMessage, 500);
        switch (r.Outcome)
        {
            case NotificationOutcome.Accepted:
                d.Status = DeliveryStatuses.Accepted;
                d.ProviderMessageId = r.ProviderMessageId is null ? null : Truncate(r.ProviderMessageId, 200);
                d.SentAt = now;
                break;
            case NotificationOutcome.Unavailable:
                d.Status = DeliveryStatuses.Unavailable;
                break;
            default:
                d.Status = DeliveryStatuses.Failed;
                break;
        }
    }

    private async Task RecordAsync(DeliveryOwner owner, NotificationMessage m, int step, string channel, string provider, NotificationResult r,
        int tries, CancellationToken ct)
    {
        var d = new NotificationDelivery
        {
            NotificationId = owner.NotificationId, OtpCodeId = owner.OtpCodeId, RouteCode = m.RouteCode, RouteStep = step, Channel = channel,
            Provider = provider, CreatedAt = DateTimeOffset.UtcNow,
        };
        Apply(d, r, tries);
        uow.Repository<NotificationDelivery>().Add(d);
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Fills {{1}}, {{2}}, … with the parameters (missing ones become empty).</summary>
    public static string Render(string template, IReadOnlyList<string> parameters) =>
        Placeholder().Replace(template, m => int.TryParse(m.Groups[1].Value, out var i) && i >= 1 && i <= parameters.Count ? parameters[i - 1] : "");

    /// <summary>How many parameters the template uses (its highest {{n}}).</summary>
    public static int ParameterCount(string template) =>
        Placeholder().Matches(template).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();

    [GeneratedRegex(@"\{\{(\d{1,2})\}\}")] private static partial Regex Placeholder();

    private static string Truncate(string s, int max = 500) => s.Length <= max ? s : s[..max];

    private sealed record ProviderRow(string ProviderName, int Priority, string? CountryCode);
    private sealed record TemplateRow(string LanguageCode, string TemplateName, string TemplateContent);
}
