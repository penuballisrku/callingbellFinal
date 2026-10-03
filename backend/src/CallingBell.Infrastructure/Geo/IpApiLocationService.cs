using System.Net;
using System.Net.Http.Json;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

public sealed class IpApiOptions
{
    public const string Section = "GeoIp:IpApi";
    /// <summary>When false, only the local GeoIP city database is used.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Free endpoint: HTTP only, 45 requests/minute per server IP, non-commercial use only.
    /// For production set <see cref="ApiKey"/> (ip-api Pro), which switches to <see cref="ProBaseUrl"/> over HTTPS.
    /// </summary>
    public string BaseUrl { get; set; } = "http://ip-api.com/json/";
    public string ProBaseUrl { get; set; } = "https://pro.ip-api.com/json/";
    public string? ApiKey { get; set; }
    /// <summary>How long a location is remembered per IP (also keeps traffic well under the free rate limit).</summary>
    public int CacheHours { get; set; } = 12;
}

/// <summary>
/// Locates visitors with ip-api.com (city, district, PIN code, coordinates). Results are cached per IP; when ip-api is unreachable or
/// rate-limited, the local GeoIP city database (if installed) answers instead.
/// </summary>
internal sealed class IpApiLocationService(
    IHttpClientFactory httpFactory, IGeoLocationService localDb, IOptions<IpApiOptions> options, ILogger<IpApiLocationService> logger) : IIpLocationService
{
    public const string HttpClientName = "ip-api";
    private const string Fields = "status,message,countryCode,regionName,city,district,zip,lat,lon";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 50_000 });
    private long _blockedUntilTicks; // UTC ticks; read and written across concurrent requests

    public async Task<IpLocation?> LocateAsync(IPAddress address, CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.Enabled || DateTime.UtcNow.Ticks < Interlocked.Read(ref _blockedUntilTicks)) return localDb.LocationFor(address);

        var key = address.ToString();
        if (_cache.TryGetValue(key, out IpLocation? cached)) return cached;

        try
        {
            var pro = !string.IsNullOrWhiteSpace(o.ApiKey);
            var url = $"{(pro ? o.ProBaseUrl : o.BaseUrl)}{Uri.EscapeDataString(key)}?fields={Fields}" + (pro ? $"&key={Uri.EscapeDataString(o.ApiKey!)}" : "");
            using var response = await httpFactory.CreateClient(HttpClientName).GetAsync(url, ct);
            NoteRateLimit(response);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return localDb.LocationFor(address);
            response.EnsureSuccessStatusCode();

            var r = await response.Content.ReadFromJsonAsync<IpApiResponse>(ct);
            // "fail" = private/reserved range or invalid query: nothing to locate, so remember that too.
            var location = r is { Status: "success" }
                ? new IpLocation(Blank(r.CountryCode), Blank(r.RegionName), Blank(r.City), r.Lat, r.Lon, Blank(r.District), Blank(r.Zip))
                : null;
            _cache.Set(key, location, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(o.CacheHours) });
            return location;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("ip-api.com lookup failed ({Message}); using the local GeoIP database", ex.Message);
            return localDb.LocationFor(address);
        }
    }

    /// <summary>ip-api reports remaining requests (X-Rl) and seconds until the window resets (X-Ttl); pause before hitting the limit.</summary>
    private void NoteRateLimit(HttpResponseMessage response)
    {
        static int? Header(HttpResponseMessage m, string name) =>
            m.Headers.TryGetValues(name, out var v) && int.TryParse(v.FirstOrDefault(), out var n) ? n : null;
        if (Header(response, "X-Rl") is not 0 && response.StatusCode != HttpStatusCode.TooManyRequests) return;
        var ttl = Header(response, "X-Ttl") ?? 60;
        Interlocked.Exchange(ref _blockedUntilTicks, DateTime.UtcNow.AddSeconds(ttl).Ticks);
        logger.LogWarning("ip-api.com rate limit reached; using the local GeoIP database for {Seconds}s", ttl);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record IpApiResponse(string? Status, string? Message, string? CountryCode, string? RegionName, string? City,
        string? District, string? Zip, double? Lat, double? Lon);
}
