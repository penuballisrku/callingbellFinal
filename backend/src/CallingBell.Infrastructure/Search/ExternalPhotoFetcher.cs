using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Onboarding;
using CallingBell.Application.Features.Owner;
using Microsoft.Extensions.Logging;

namespace CallingBell.Infrastructure.Search;

/// <summary>
/// Downloads a Google Maps photo on the server for <see cref="ImportSourcePhotosCommand"/>: the photo's address comes from Google (with the
/// API key, server side), then the image is read with a size cap and only kept if it really is an image.
/// </summary>
internal sealed class ExternalPhotoFetcher(IGooglePlacesSearch google, IHttpClientFactory http, ILogger<ExternalPhotoFetcher> logger) : IExternalPhotoFetcher
{
    public const string HttpClientName = "external-photo-import";
    private const int WidthPx = 1600;

    public async Task<byte[]?> FetchAsync(string provider, string reference, CancellationToken ct)
    {
        if (provider != "google") return null;
        try
        {
            var uri = await google.GetPhotoUriAsync(reference, WidthPx, ct);
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps) return null;
            using var response = await http.CreateClient(HttpClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true) return null;
            if (response.Content.Headers.ContentLength > MediaRules.MaxImageBytes) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MediaRules.MaxImageBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            var bytes = buffer.ToArray();
            return MediaRules.DetectImage(bytes) is null ? null : bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Photo import: a Google Maps photo could not be downloaded ({Error})", ex.GetType().Name);
            return null;
        }
    }
}
