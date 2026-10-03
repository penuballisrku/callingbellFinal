using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallingBell.Infrastructure.Geo;

/// <summary>
/// Development only: a browser on the same machine reaches the API from 127.0.0.1, which can't be geolocated. This supplies the
/// machine's public IP instead (GeoIp:DevelopmentClientIp, else looked up once from GeoIp:PublicIpLookupUrl) so country and
/// district detection behave locally as they do in production.
/// </summary>
public sealed class DevelopmentPublicIp(IHttpClientFactory httpFactory, IOptions<GeoIpOptions> options, ILogger<DevelopmentPublicIp> logger)
{
    public const string HttpClientName = "public-ip";
    private Task<string?>? _lookup;

    public Task<string?> GetAsync()
    {
        var configured = options.Value.DevelopmentClientIp?.Trim();
        if (!string.IsNullOrEmpty(configured)) return Task.FromResult<string?>(configured);
        if (string.IsNullOrWhiteSpace(options.Value.PublicIpLookupUrl)) return Task.FromResult<string?>(null);
        return _lookup ??= LookupAsync();
    }

    private async Task<string?> LookupAsync()
    {
        try
        {
            var text = (await httpFactory.CreateClient(HttpClientName).GetStringAsync(options.Value.PublicIpLookupUrl)).Trim();
            if (IPAddress.TryParse(text, out _))
            {
                logger.LogInformation("Development: geolocating local requests as this machine's public IP {Ip}", text);
                return text;
            }
            logger.LogWarning("Development: public IP lookup returned an unexpected response");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Development: public IP lookup failed ({Message}); local requests won't be geolocated", ex.Message);
        }
        _lookup = null; // retry on the next request
        return null;
    }
}
