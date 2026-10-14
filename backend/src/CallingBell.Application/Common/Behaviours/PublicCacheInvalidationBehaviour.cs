using CallingBell.Application.Common.Interfaces;
using MediatR;

namespace CallingBell.Application.Common.Behaviours;

/// <summary>
/// Clears the cached SEO output (titles, structured data, sitemaps) after a command that can change what public pages show succeeds:
/// admin and business-owner edits, business sign-up, payments (plans) and customer engagement (reviews change ratings). API responses
/// themselves are not cached on the server. Sign-in, notifications and the search assistant don't clear it. The same commands tell
/// <see cref="IPlatformStatsSignal"/>, so open pages get the new business, review and completed-booking counts pushed to them.
/// </summary>
public sealed class PublicCacheInvalidationBehaviour<TRequest, TResponse>(Features.Seo.SeoCache seo, IPlatformStatsSignal stats)
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
        // So crawlers never get information the edit replaced.
        seo.Reset();
        stats.Changed();
        return response;
    }
}
