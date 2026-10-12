using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

/// <summary>
/// Development only: a browser on the same machine reaches the API from 127.0.0.1, which can't be geolocated. This supplies the
/// machine's public IP instead (GeoIp:DevelopmentClientIp, else looked up from GeoIp:PublicIpLookupUrl) so country and district
/// detection behave locally as they do in production. The looked-up IP is re-checked every minute, so switching network or turning a
/// VPN on or off shows the new location without restarting the API.
/// </summary>
public sealed class DevelopmentPublicIp(IHttpClientFactory httpFactory, IOptions<GeoIpOptions> options, ILogger<DevelopmentPublicIp> logger)
{
    public const string HttpClientName = "public-ip";
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(1);
    private readonly object gate = new();
    private (string? Ip, DateTimeOffset CheckedAt)? last;
    private Task<string?>? lookup;

    public Task<string?> GetAsync()
    {
        var configured = options.Value.DevelopmentClientIp?.Trim();
        if (!string.IsNullOrEmpty(configured)) return Task.FromResult<string?>(configured);
        if (string.IsNullOrWhiteSpace(options.Value.PublicIpLookupUrl)) return Task.FromResult<string?>(null);
        lock (gate)
        {
            if (last is { } known && DateTimeOffset.UtcNow - known.CheckedAt < RecheckAfter) return Task.FromResult(known.Ip);
            // One lookup at a time; requests arriving meanwhile share it. (A finished one is never reused: its answer is in `last`.)
            if (lookup is null || lookup.IsCompleted) lookup = LookupAsync();
            return lookup;
        }
    }

    private async Task<string?> LookupAsync()
    {
        string? ip = null;
        try
        {
            var text = (await httpFactory.CreateClient(HttpClientName).GetStringAsync(options.Value.PublicIpLookupUrl)).Trim();
            if (IPAddress.TryParse(text, out _)) ip = text;
            else logger.LogWarning("Development: public IP lookup returned an unexpected response");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Development: public IP lookup failed ({Message})", ex.Message);
        }
        lock (gate)
        {
            var previous = last?.Ip;
            if (ip is not null && ip != previous) logger.LogInformation("Development: geolocating local requests as this machine's public IP {Ip}", ip);
            // A failed lookup keeps the previous IP (if any) until the next check, rather than retrying on every request.
            last = (ip ?? previous, DateTimeOffset.UtcNow);
            lookup = null;
            return last.Value.Ip;
        }
    }
}
