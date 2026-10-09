using CallingBell.Application.Common;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CallingBell.Application.Features.Seo;

namespace CallingBell.Application.Features.Notifications.Delivery;

/// <summary>
/// Turns a routed notification into the message its channels send: who (the user's browsers; the business's WhatsApp/phone number for an
/// owner, the booking's contact number for a customer), the push title and text, and the template parameters, all from the database.
/// </summary>
public sealed class NotificationContentBuilder(IUnitOfWork uow, IOptions<SeoOptions> site)
{
    /// <summary>Links in SMS / RCS texts must be absolute.</summary>
    private string Absolute(string path) => path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : site.Value.SiteUrl.TrimEnd('/') + path;

    public async Task<NotificationMessage?> BuildAsync(Notification n, CancellationToken ct) => n.RouteCode switch
    {
        NotificationRoutes.NewLead when n.ReferenceId is { } id => await LeadAsync(n, id, ct),
        NotificationRoutes.Booking when n.ReferenceId is { } id => await BookingAsync(n, id, ct),
        NotificationRoutes.Video when n.ReferenceId is { } id => await VideoAsync(n, id, ct),
        NotificationRoutes.Chat => Plain(n),
        _ => null,
    };

    /// <summary>A browser alert built from the notification itself (chat messages: the in-app text is the message).</summary>
    private static NotificationMessage Plain(Notification n) => new()
    {
        RouteCode = n.RouteCode!,
        Recipient = new NotificationRecipient(n.UserId, null, null),
        Title = n.Title,
        Body = n.Message,
        Url = n.LinkUrl,
        Data = new Dictionary<string, string> { ["type"] = n.RouteCode!, ["url"] = n.LinkUrl ?? "/" },
    };

    /// <summary>A video consultation is ready: to the customer's browsers, else WhatsApp (business, when, link).</summary>
    private async Task<NotificationMessage?> VideoAsync(Notification n, Guid roomId, CancellationToken ct)
    {
        var v = await uow.Repository<VideoRoom>().QueryNoTracking().Where(x => x.Id == roomId)
            .Select(x => new { x.Id, BusinessName = x.Business.Name, Start = x.Booking != null ? (DateTimeOffset?)x.Booking.ScheduledStart : x.OpensAt,
                Phone = x.Booking != null ? x.Booking.CustomerPhone : null,
                Country = x.Business.CityRef != null ? x.Business.CityRef.State.CountryCode : null })
            .FirstOrDefaultAsync(ct);
        if (v is null) return null;
        var url = n.LinkUrl ?? $"/video/{v.Id}";
        var phone = Phones.ToE164(v.Phone);
        var when = v.Start?.ToOffset(IndianTime.Offset).ToString("ddd d MMM, h:mm tt") ?? "now";
        return new NotificationMessage
        {
            RouteCode = NotificationRoutes.Video,
            Recipient = new NotificationRecipient(n.UserId, phone, v.Country ?? Phones.CountryOf(phone)),
            Title = n.Title,
            Body = n.Message,
            Url = url,
            Data = new Dictionary<string, string> { ["type"] = NotificationRoutes.Video, ["roomId"] = v.Id.ToString(), ["url"] = url },
            TemplateParameters = [v.BusinessName, when, Absolute(url)],
        };
    }

    private async Task<NotificationMessage?> LeadAsync(Notification n, Guid enquiryId, CancellationToken ct)
    {
        var e = await uow.Repository<Enquiry>().QueryNoTracking().Where(x => x.Id == enquiryId)
            .Select(x => new
            {
                x.Id, x.BusinessId, x.CustomerName, x.EnquiryType,
                Service = x.Service != null ? x.Service.Name : x.Business.SubCategory != null ? x.Business.SubCategory.Name : x.Business.Category.Name,
                Area = x.Business.AreaRef != null ? x.Business.AreaRef.Name : x.Business.Area ?? x.Business.City,
                x.Business.WhatsAppNumber, BusinessPhone = x.Business.PhoneNumber, OwnerPhone = x.Business.Owner.PhoneNumber,
                Country = x.Business.CityRef != null ? x.Business.CityRef.State.CountryCode : null,
            })
            .FirstOrDefaultAsync(ct);
        if (e is null) return null;

        var url = n.LinkUrl ?? $"/owner/leads?lead={e.Id}";
        var phone = Phones.ToE164(e.WhatsAppNumber) ?? Phones.ToE164(e.BusinessPhone) ?? Phones.ToE164(e.OwnerPhone);
        var kind = e.EnquiryType switch { "Quotation" => "quotation request", "Callback" => "callback request", _ => "enquiry" };
        return new NotificationMessage
        {
            RouteCode = NotificationRoutes.NewLead,
            Recipient = new NotificationRecipient(n.UserId, phone, e.Country ?? Phones.CountryOf(phone)),
            // "AC Repair enquiry near Kukatpally"
            Title = "New Lead",
            Body = $"{e.Service} {kind} near {e.Area} · {FirstName(e.CustomerName)}",
            Url = url,
            Data = new Dictionary<string, string>
            {
                ["type"] = NotificationRoutes.NewLead, ["leadId"] = e.Id.ToString(), ["businessId"] = e.BusinessId.ToString(), ["url"] = url,
            },
            // Service, area, customer, link (the SMS / RCS texts use the link; the WhatsApp template the first three).
            TemplateParameters = [e.Service, e.Area, FirstName(e.CustomerName), Absolute(url)],
        };
    }

    private async Task<NotificationMessage?> BookingAsync(Notification n, Guid bookingId, CancellationToken ct)
    {
        var b = await uow.Repository<Booking>().QueryNoTracking().Where(x => x.Id == bookingId)
            .Select(x => new
            {
                x.Id, x.BookingNumber, x.BusinessId, x.CustomerUserId, x.CustomerName, x.CustomerPhone, x.ScheduledStart, Service = x.Service.Name,
                BusinessName = x.Business.Name, x.Business.WhatsAppNumber, BusinessPhone = x.Business.PhoneNumber, OwnerPhone = x.Business.Owner.PhoneNumber,
                Country = x.Business.CityRef != null ? x.Business.CityRef.State.CountryCode : null,
            })
            .FirstOrDefaultAsync(ct);
        if (b is null) return null;

        // The same booking notifies the owner (new booking) and the customer (confirmed): the number depends on who this is for.
        var forCustomer = n.UserId == b.CustomerUserId;
        var phone = forCustomer
            ? Phones.ToE164(b.CustomerPhone)
            : Phones.ToE164(b.WhatsAppNumber) ?? Phones.ToE164(b.BusinessPhone) ?? Phones.ToE164(b.OwnerPhone);
        var when = b.ScheduledStart.ToOffset(IndianTime.Offset).ToString("ddd d MMM, h:mm tt");
        var url = n.LinkUrl ?? (forCustomer ? "/account/bookings" : "/owner/bookings");
        var summary = forCustomer ? $"{b.BusinessName} confirmed your {b.Service} booking." : $"{FirstName(b.CustomerName)} booked {b.Service}.";
        return new NotificationMessage
        {
            RouteCode = NotificationRoutes.Booking,
            Recipient = new NotificationRecipient(n.UserId, phone, b.Country ?? Phones.CountryOf(phone)),
            Title = n.Title,
            Body = $"{summary} {when}",
            Url = url,
            Data = new Dictionary<string, string>
            {
                ["type"] = NotificationRoutes.Booking, ["bookingId"] = b.Id.ToString(), ["businessId"] = b.BusinessId.ToString(), ["url"] = url,
            },
            // Heading, summary, when.
            TemplateParameters = [n.Title, summary, when],
        };
    }

    private static string FirstName(string name)
    {
        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "a customer" : first;
    }
}

/// <summary>
/// Delivers one queued notification through its route, from where it left off. Called by the background dispatcher once it has claimed the
/// notification, so several app instances never send the same one.
/// </summary>
public sealed class NotificationDispatchProcessor(IUnitOfWork uow, NotificationContentBuilder content, NotificationRouter router,
    ILogger<NotificationDispatchProcessor> logger)
{
    /// <summary>Gives up after this many rounds of unexpected errors (provider failures are not errors: they move to the next channel).</summary>
    public const int MaxDispatchAttempts = 5;

    public async Task ProcessAsync(Guid notificationId, CancellationToken ct)
    {
        var n = await uow.Repository<Notification>().Query().FirstOrDefaultAsync(x => x.Id == notificationId, ct);
        if (n is null || n.DispatchStatus != DispatchStatuses.Processing) return;

        try
        {
            var message = await content.BuildAsync(n, ct);
            if (message is null)
            {
                logger.LogWarning("Notification {NotificationId} ({Route}) has nothing to send; its reference is gone", n.Id, n.RouteCode);
                Finish(n, DispatchStatuses.Exhausted, n.DispatchStep);
            }
            else
            {
                var result = await router.RouteAsync(message, new DeliveryOwner(n.Id, null), n.DispatchStep, ct: ct);
                Finish(n, result.Success ? DispatchStatuses.Completed : DispatchStatuses.Exhausted, result.Step);
                if (!result.Success)
                    logger.LogWarning("Notification {NotificationId} ({Route}) could not be delivered on any channel: {Attempts}", n.Id, n.RouteCode,
                        string.Join(", ", result.Attempts.Select(a => $"{a.Channel}/{a.Provider}={a.Status}{(a.ErrorCode is null ? "" : $"({a.ErrorCode})")}")));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Unexpected (database, bug): try again later, a few times.
            logger.LogError(ex, "Dispatching notification {NotificationId} failed (round {Attempt})", n.Id, n.DispatchAttempts);
            if (n.DispatchAttempts >= MaxDispatchAttempts) Finish(n, DispatchStatuses.Exhausted, n.DispatchStep);
            else
            {
                n.DispatchStatus = DispatchStatuses.Pending;
                n.DispatchLockedUntil = null;
                n.NextDispatchAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, n.DispatchAttempts));
            }
        }
        await uow.SaveChangesAsync(CancellationToken.None);
    }

    private static void Finish(Notification n, string status, int step)
    {
        n.DispatchStatus = status;
        n.DispatchStep = step;
        n.DispatchLockedUntil = null;
        n.NextDispatchAt = null;
        n.DispatchedAt = DateTimeOffset.UtcNow;
    }
}
