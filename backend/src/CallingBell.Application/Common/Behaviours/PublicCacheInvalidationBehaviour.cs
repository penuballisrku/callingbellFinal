using CallingBell.Application.Common.Interfaces;
using MediatR;

namespace CallingBell.Application.Common.Behaviours;

/// <summary>
/// Clears the cached public pages (home, categories, listings, business pages) after a command that can change what they show succeeds:
/// admin and business-owner edits, business sign-up, payments (plans) and customer engagement (reviews change ratings). This lets the
/// public responses (and SEO output) be cached for minutes while edits still show at once. Sign-in, notifications and the search assistant don't.
/// </summary>
public sealed class PublicCacheInvalidationBehaviour<TRequest, TResponse>(IPublicCache cache, Features.Seo.SeoCache seo)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string[] ChangingFeatures =
        [".Features.Admin", ".Features.Owner", ".Features.Onboarding", ".Features.Payments", ".Features.Engagement"];

    private static readonly bool Invalidates = typeof(TRequest).Name.EndsWith("Command", StringComparison.Ordinal)
        && typeof(TRequest).Namespace is { } ns && ChangingFeatures.Any(f => ns.Contains(f, StringComparison.Ordinal));

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();
        if (!Invalidates) return response;
        // SEO output (titles, structured data, sitemaps) too, so crawlers never get information the edit replaced.
        seo.Reset();
        await cache.InvalidateAsync(CancellationToken.None);
        return response;
    }
}
