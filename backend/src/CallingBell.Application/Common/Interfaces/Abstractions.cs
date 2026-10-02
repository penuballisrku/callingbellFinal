using CallingBell.Application.Features.Auth;
using CallingBell.Application.Features.Notifications;

namespace CallingBell.Application.Common.Interfaces;

/// <summary>Generic repository over an aggregate/entity set.</summary>
public interface IRepository<T> where T : class
{
    /// <summary>Tracked query - use when the result will be modified.</summary>
    IQueryable<T> Query();

    /// <summary>Untracked query - use for read models / projections.</summary>
    IQueryable<T> QueryNoTracking();

    void Add(T entity);
    void Remove(T entity);
}

/// <summary>Unit of work: one transactional boundary for all repositories in a request.</summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
    string? IpAddress { get; }
}

public interface IIdentityService
{
    Task<AuthResultDto> LoginAsync(string email, string password, CancellationToken ct);
    Task<AuthResultDto> RegisterAsync(RegisterRequest request, CancellationToken ct);
    /// <summary>Signs in (or signs up) with a Google ID token issued to this application's client id.</summary>
    Task<AuthResultDto> ExternalGoogleAsync(string idToken, string accountType, CancellationToken ct);
    Task<AuthResultDto> RefreshAsync(string refreshToken, CancellationToken ct);
    Task RevokeAsync(string refreshToken, CancellationToken ct);
    Task<CurrentUserDto> GetCurrentUserAsync(string userId, CancellationToken ct);
}

/// <summary>Pushes real-time events (SignalR). Implemented in the API layer.</summary>
public interface IRealtimeNotifier
{
    Task AvailabilityChangedAsync(Guid businessId, string status, DateTimeOffset lastSeenOn, CancellationToken ct = default);
    Task NotifyUserAsync(string userId, NotificationDto notification, CancellationToken ct = default);
}
