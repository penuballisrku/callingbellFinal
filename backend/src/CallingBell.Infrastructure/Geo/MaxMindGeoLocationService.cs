using System.Net;
using CallingBell.Application.Common.Interfaces;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

public sealed class GeoIpOptions
{
    public const string Section = "GeoIp";
    /// <summary>
    /// Country database in MaxMind DB format (MaxMind GeoLite2-Country or DB-IP Country Lite), relative to the API's content root.
    /// Download it with database/tools/download_geoip.ps1.
    /// </summary>
    public string DatabasePath { get; set; } = "App_Data/country.mmdb";
    public string DefaultCountryCode { get; set; } = "IN";
    /// <summary>
    /// Read the client IP from X-Forwarded-For for the country lookup. Enable only behind a reverse proxy you control
    /// (otherwise clients could claim any IP). Rate limiting is unaffected either way.
    /// </summary>
    public bool TrustForwardedFor { get; set; }
}

/// <summary>Singleton reader over a MaxMind-DB country database (GeoLite2 or DB-IP Lite; memory-mapped, thread-safe).</summary>
internal sealed class MaxMindGeoLocationService : IGeoLocationService, IDisposable
{
    private readonly DatabaseReader? _reader;

    public MaxMindGeoLocationService(IOptions<GeoIpOptions> options, IHostEnvironment env, ILogger<MaxMindGeoLocationService> logger)
    {
        DefaultCountryCode = options.Value.DefaultCountryCode.ToUpperInvariant();
        var path = Path.GetFullPath(Path.IsPathRooted(options.Value.DatabasePath)
            ? options.Value.DatabasePath : Path.Combine(env.ContentRootPath, options.Value.DatabasePath));
        if (File.Exists(path))
        {
            _reader = new DatabaseReader(path);
            // DB-IP Lite is licensed CC BY 4.0, which requires a visible credit; MaxMind GeoLite2 has no on-page requirement.
            if (_reader.Metadata.DatabaseType.StartsWith("DBIP", StringComparison.OrdinalIgnoreCase))
                Attribution = new GeoAttribution("IP geolocation by DB-IP", "https://db-ip.com");
            logger.LogInformation("GeoIP database loaded: {Type}, built {Date:d}", _reader.Metadata.DatabaseType, _reader.Metadata.BuildDate);
        }
        else
        {
            logger.LogWarning("GeoIP database not found at {Path}; visitors will see the default country ({Country}). " +
                              "Run database/tools/download_geoip.ps1 to install it.", path, DefaultCountryCode);
        }
    }

    public string DefaultCountryCode { get; }
    public GeoAttribution? Attribution { get; }

    public string? CountryCodeFor(IPAddress address)
    {
        if (_reader is null) return null;
        try
        {
            return _reader.TryCountry(address, out var response) ? response?.Country.IsoCode : null;
        }
        catch (GeoIP2Exception) { return null; }
    }

    public void Dispose() => _reader?.Dispose();
}
