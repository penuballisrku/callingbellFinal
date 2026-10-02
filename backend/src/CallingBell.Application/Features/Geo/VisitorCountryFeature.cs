using System.Net;
using System.Net.Sockets;
using CallingBell.Application.Common.Interfaces;
using MediatR;

namespace CallingBell.Application.Features.Geo;

/// <summary>The visitor's country. Source is "cdn" (edge header), "geoip" (local database) or "default".</summary>
public sealed record VisitorCountryDto(string CountryCode, string Source, GeoAttribution? Attribution);

/// <param name="CdnCountry">Country supplied by a CDN edge header (e.g. Cloudflare CF-IPCountry), if any.</param>
/// <param name="ClientIp">The client's IP address as seen by the API.</param>
public sealed record GetVisitorCountryQuery(string? CdnCountry, string? ClientIp) : IRequest<VisitorCountryDto>;

public sealed class GetVisitorCountryHandler(IGeoLocationService geo) : IRequestHandler<GetVisitorCountryQuery, VisitorCountryDto>
{
    public Task<VisitorCountryDto> Handle(GetVisitorCountryQuery r, CancellationToken ct)
    {
        var cdn = r.CdnCountry?.Trim().ToUpperInvariant();
        // "XX" = unknown and "T1" = Tor in Cloudflare's header; ignore them.
        if (cdn is { Length: 2 } && cdn.All(char.IsAsciiLetter) && cdn is not ("XX" or "T1"))
            return Task.FromResult(new VisitorCountryDto(cdn, "cdn", geo.Attribution));

        if (IPAddress.TryParse(r.ClientIp?.Trim(), out var ip))
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IsPublic(ip) && geo.CountryCodeFor(ip) is { } code)
                return Task.FromResult(new VisitorCountryDto(code, "geoip", geo.Attribution));
        }
        return Task.FromResult(new VisitorCountryDto(geo.DefaultCountryCode, "default", geo.Attribution));
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127));
        }
        return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal);
    }
}
