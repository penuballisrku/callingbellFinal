using CallingBell.Application.Common.Behaviours;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CallingBell.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PublicCacheInvalidationBehaviour<,>));
        services.AddScoped<Features.Geo.VisitorOriginResolver>();
        services.AddMemoryCache();
        services.AddScoped<Common.ReferenceDataCache>();
        services.AddScoped<Common.ReverseGeocoder>();
        services.AddSingleton<Features.Seo.SeoCache>();
        services.AddScoped<Features.Seo.ISeoMetadataService, Features.Seo.SeoMetadataService>();
        services.AddScoped<Features.Seo.SitemapService>();
        services.AddScoped<Common.CountryPricingService>();

        return services;
    }
}
