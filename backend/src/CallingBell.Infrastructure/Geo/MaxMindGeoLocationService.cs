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
    /// <summary>
    /// Optional city database (MaxMind GeoLite2-City or DB-IP City Lite): the fallback for district detection when ip-api.com is
    /// unavailable or rate-limited. Download it with database/tools/download_geoip.ps1 -City.
    /// </summary>
    public string CityDatabasePath { get; set; } = "App_Data/city.mmdb";
    public string DefaultCountryCode { get; set; } = "IN";
    /// <summary>
    /// Read the client IP from X-Forwarded-For for the country lookup. Enable only behind a reverse proxy you control
    /// (otherwise clients could claim any IP). Rate limiting is unaffected either way.
    /// </summary>
    public bool TrustForwardedFor { get; set; }
    /// <summary>
    /// Furthest an IP location may be from a listed city's centre and still count as that city's district (km).
    /// </summary>
    public double DistrictRadiusKm { get; set; } = 60;
    /// <summary>
    /// Development only: IP to geolocate for requests from this machine (127.0.0.1 / private networks) instead of looking up the
    /// machine's public IP. Ignored outside Development.
    /// </summary>
    public string? DevelopmentClientIp { get; set; }
    /// <summary>Development only: service that returns this machine's public IP as plain text. Empty disables the lookup.</summary>
    public string? PublicIpLookupUrl { get; set; } = "http://ip-api.com/line/?fields=query";
}

/// <summary>Singleton readers over MaxMind-DB databases (GeoLite2 or DB-IP Lite; memory-mapped, thread-safe).</summary>
internal sealed class MaxMindGeoLocationService : IGeoLocationService, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly DatabaseReader? _cityReader;

    public MaxMindGeoLocationService(IOptions<GeoIpOptions> options, IHostEnvironment env, ILogger<MaxMindGeoLocationService> logger)
    {
        DefaultCountryCode = options.Value.DefaultCountryCode.ToUpperInvariant();
        _reader = Open(options.Value.DatabasePath, env, logger);
        _cityReader = Open(options.Value.CityDatabasePath, env, logger);
        if (_reader is null && _cityReader is null)
            logger.LogWarning("No GeoIP database found; visitors will see the default country ({Country}). " +
                              "Run database/tools/download_geoip.ps1 to install it.", DefaultCountryCode);
        else if (_cityReader is null)
            logger.LogInformation("GeoIP city database not installed; the visitor's district won't be detected. " +
                                  "Run database/tools/download_geoip.ps1 -City to install it.");

        // DB-IP Lite is licensed CC BY 4.0, which requires a visible credit; MaxMind GeoLite2 has no on-page requirement.
        if (new[] { _reader, _cityReader }.Any(r => r?.Metadata.DatabaseType.StartsWith("DBIP", StringComparison.OrdinalIgnoreCase) == true))
            Attribution = new GeoAttribution("IP geolocation by DB-IP", "https://db-ip.com");
    }

    private static DatabaseReader? Open(string configuredPath, IHostEnvironment env, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;
        var path = Path.GetFullPath(Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(env.ContentRootPath, configuredPath));
        if (!File.Exists(path)) return null;
        var reader = new DatabaseReader(path);
        logger.LogInformation("GeoIP database loaded: {Type}, built {Date:d}", reader.Metadata.DatabaseType, reader.Metadata.BuildDate);
        return reader;
    }

    public string DefaultCountryCode { get; }
    public GeoAttribution? Attribution { get; }

    public string? CountryCodeFor(IPAddress address)
    {
        try
        {
            if (_reader is not null && _reader.TryCountry(address, out var country) && country?.Country.IsoCode is { } code) return code;
            return _cityReader is not null && _cityReader.TryCity(address, out var city) ? city?.Country.IsoCode : null;
        }
        catch (GeoIP2Exception) { return null; }
    }

    public IpLocation? LocationFor(IPAddress address)
    {
        if (_cityReader is null) return null;
        try
        {
            if (!_cityReader.TryCity(address, out var r) || r is null) return null;
            return new IpLocation(r.Country.IsoCode, r.MostSpecificSubdivision.Name, r.City.Name, r.Location.Latitude, r.Location.Longitude);
        }
        catch (GeoIP2Exception) { return null; }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _cityReader?.Dispose();
    }
}
