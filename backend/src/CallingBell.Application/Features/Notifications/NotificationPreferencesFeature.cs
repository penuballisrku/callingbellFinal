using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Notifications;

/// <param name="Offers">Push notifications about offers near the person (promotional; off until they turn it on).</param>
/// <param name="HasCity">Offers are matched to the person's city; without one there is nothing to match.</param>
public sealed record NotificationPreferencesDto(bool Offers, bool HasCity);

public sealed record GetNotificationPreferencesQuery : IRequest<NotificationPreferencesDto>;

public sealed class GetNotificationPreferencesHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto>
{
    public async Task<NotificationPreferencesDto> Handle(GetNotificationPreferencesQuery r, CancellationToken ct)
    {
        var id = user.UserId ?? throw new ForbiddenAccessException();
        return await uow.Repository<ApplicationUser>().QueryNoTracking().Where(u => u.Id == id)
            .Select(u => new NotificationPreferencesDto(u.ReceiveOffers, u.CityId != null)).FirstAsync(ct);
    }
}

public sealed record UpdateNotificationPreferencesCommand(bool Offers) : IRequest<NotificationPreferencesDto>;

public sealed class UpdateNotificationPreferencesHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<UpdateNotificationPreferencesCommand, NotificationPreferencesDto>
{
    public async Task<NotificationPreferencesDto> Handle(UpdateNotificationPreferencesCommand r, CancellationToken ct)
    {
        var id = user.UserId ?? throw new ForbiddenAccessException();
        var me = await uow.Repository<ApplicationUser>().Query().FirstAsync(u => u.Id == id, ct);
        me.ReceiveOffers = r.Offers;
        await uow.SaveChangesAsync(ct);
        return new NotificationPreferencesDto(me.ReceiveOffers, me.CityId != null);
    }
}
