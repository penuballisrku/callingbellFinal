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

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction (all-or-nothing). The whole unit is retried on transient
    /// SQL failures, so the delegate must be safe to re-run.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
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
    Task<bool> IsEmailAvailableAsync(string email, CancellationToken ct);
    Task<AccountSettingsDto> GetAccountSettingsAsync(string userId, CancellationToken ct);
    Task<CurrentUserDto> UpdateAccountAsync(string userId, string displayName, string phoneNumber, CancellationToken ct);
    /// <summary>Changes the password (or sets one for Google-only accounts, when <paramref name="currentPassword"/> is null).</summary>
    Task ChangePasswordAsync(string userId, string? currentPassword, string newPassword, CancellationToken ct);
    /// <summary>Revokes every refresh token of the user except <paramref name="keepRefreshToken"/>; returns how many were revoked.</summary>
    Task<int> RevokeOtherSessionsAsync(string userId, string? keepRefreshToken, CancellationToken ct);
}

/// <summary>Pushes real-time events (SignalR). Implemented in the API layer.</summary>
public interface IRealtimeNotifier
{
    Task AvailabilityChangedAsync(Guid businessId, string status, DateTimeOffset lastSeenOn, CancellationToken ct = default);
    Task NotifyUserAsync(string userId, NotificationDto notification, CancellationToken ct = default);
}

/// <summary>An order created at the payment gateway (amounts in the smallest currency unit, i.e. paise).</summary>
public sealed record GatewayOrder(string Id, long Amount, string Currency, string Status);

/// <summary>A payment as reported by the gateway.</summary>
public sealed record GatewayPayment(string Id, string? OrderId, long Amount, string Currency, string Status, string? Method, string? ErrorDescription);

/// <summary>Online payment gateway (implemented with Razorpay in Infrastructure).</summary>
public interface IPaymentGateway
{
    string Name { get; }
    /// <summary>True when API keys are configured; checkout is disabled otherwise.</summary>
    bool IsConfigured { get; }
    /// <summary>Publishable key id used by the browser checkout.</summary>
    string? PublicKey { get; }
    Task<GatewayOrder> CreateOrderAsync(string receipt, long amountInPaise, string currency, IReadOnlyDictionary<string, string> notes, CancellationToken ct);
    Task<GatewayPayment> GetPaymentAsync(string paymentId, CancellationToken ct);
    /// <summary>Verifies the signature the checkout returns after a successful payment.</summary>
    bool VerifyPaymentSignature(string gatewayOrderId, string paymentId, string signature);
    /// <summary>Verifies a webhook body against its signature header. False when no webhook secret is configured.</summary>
    bool VerifyWebhookSignature(string body, string signature);
}

/// <summary>IP-to-country lookup (MaxMind GeoLite2 database on the server; visitor IPs never leave the server).</summary>
public interface IGeoLocationService
{
    /// <summary>ISO 3166-1 alpha-2 country used when the country can't be determined.</summary>
    string DefaultCountryCode { get; }
    /// <summary>Country code for a public IP address, or null when unknown (not in the database, or no database installed).</summary>
    string? CountryCodeFor(System.Net.IPAddress address);
    /// <summary>Credit the database licence requires on the site (e.g. DB-IP Lite, CC BY 4.0); null when none is needed.</summary>
    GeoAttribution? Attribution { get; }
}

public sealed record GeoAttribution(string Text, string Url);
