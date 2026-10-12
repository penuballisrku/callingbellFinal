using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Presence;

/// <summary>
/// An owner's businesses follow whether the owner has CallInBell open (their live connections are tracked in the API).
/// Arriving: a business that is Offline goes Online; a status the owner chose (Busy, Available for call…) is kept. Leaving: every
/// business goes Offline. Both update "last seen" and are pushed live to customers viewing the business.
/// </summary>
/// <remarks>Deliberately not a "*Command" in an Owner feature: it runs on every visit, and must not clear the public caches each time.</remarks>
/// <param name="Online">True when the owner opens the site after being away; false once the last tab has been closed for a while.</param>
public sealed record SetOwnerPresenceRequest(string UserId, bool Online) : IRequest<int>;

public sealed class SetOwnerPresenceHandler(IUnitOfWork uow, IRealtimeNotifier notifier) : IRequestHandler<SetOwnerPresenceRequest, int>
{
    public async Task<int> Handle(SetOwnerPresenceRequest r, CancellationToken ct)
    {
        var businesses = await uow.Repository<Business>().Query().Where(b => b.OwnerUserId == r.UserId).ToListAsync(ct);
        if (businesses.Count == 0) return 0;

        var now = DateTimeOffset.UtcNow;
        foreach (var b in businesses)
        {
            if (!r.Online) b.AvailabilityStatus = AvailabilityStatuses.Offline;
            else if (b.AvailabilityStatus == AvailabilityStatuses.Offline) b.AvailabilityStatus = AvailabilityStatuses.Online;
            b.LastSeenOn = now;
        }
        await uow.SaveChangesAsync(ct);

        foreach (var b in businesses) await notifier.AvailabilityChangedAsync(b.Id, b.AvailabilityStatus, now, ct);
        return businesses.Count;
    }
}
