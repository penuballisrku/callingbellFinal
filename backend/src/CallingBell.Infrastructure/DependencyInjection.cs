using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Ai;
using CallingBell.Infrastructure.Geo;
using CallingBell.Infrastructure.Identity;
using CallingBell.Infrastructure.Payments;
using CallingBell.Infrastructure.Persistence;
using CallingBell.Infrastructure.Search;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CallingBell.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CallingBell")
                               ?? throw new InvalidOperationException("Connection string 'CallingBell' is not configured.");

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
            // Soft-delete filters on principals are intentional; dependents are filtered consistently.
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.Section));
        services.AddScoped<IIdentityService, IdentityService>();

        services.Configure<GeoIpOptions>(configuration.GetSection(GeoIpOptions.Section));
        services.AddSingleton<IGeoLocationService, MaxMindGeoLocationService>();
        services.Configure<IpApiOptions>(configuration.GetSection(IpApiOptions.Section));
        services.AddSingleton<IIpLocationService, IpApiLocationService>();
        services.AddHttpClient(IpApiLocationService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(4));
        services.AddSingleton<DevelopmentPublicIp>();
        services.AddHttpClient(DevelopmentPublicIp.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5));

        // Area discovery agent: OpenStreetMap places + India Post / OSM PIN codes, cleaned up by the local AI model, in the background.
        services.Configure<AreaDiscoveryOptions>(configuration.GetSection(AreaDiscoveryOptions.Section));
        services.AddHttpClient(AreaDiscoveryRun.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddSingleton<AreaDiscoveryQueue>();
        services.AddSingleton<IAreaDiscoveryService>(sp => sp.GetRequiredService<AreaDiscoveryQueue>());
        services.AddScoped<AreaDiscoveryRun>();
        services.AddHostedService<AreaDiscoveryWorker>();

        // City catalogue agent: every city and town of the visitor's country from GeoNames (free), imported in the background.
        services.Configure<CityCatalogOptions>(configuration.GetSection(CityCatalogOptions.Section));
        services.AddHttpClient(CityCatalogRun.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<CityCatalogQueue>();
        services.AddSingleton<ICountryCatalogService>(sp => sp.GetRequiredService<CityCatalogQueue>());
        services.AddScoped<CityCatalogRun>();
        services.AddHostedService<CityCatalogWorker>();

        // AI enrichment with a free local Ollama model. All jobs share one background worker and never run inside a request.
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.AddHttpClient(OllamaChatClient.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan); // per-call timeout from AiOptions
        services.AddSingleton<OllamaChatClient>();
        services.AddSingleton<AiWorkQueue>();
        services.AddSingleton(typeof(AiResultCache<>));
        services.AddHostedService<AiWorker>();
        services.AddSingleton<IServiceRecommender, OllamaServiceRecommender>();
        services.AddSingleton<ICityCategoryRecommender, OllamaCityCategoryRecommender>();
        services.AddSingleton<IReviewSummarizer, OllamaReviewSummarizer>();
        services.AddSingleton<ISearchAssistant, OllamaSearchAssistant>();

        // Search results beyond the platform: OpenStreetMap places (free) and Google Maps (needs a Places API key).
        services.Configure<ExternalSearchOptions>(configuration.GetSection(ExternalSearchOptions.Section));
        services.Configure<GooglePlacesOptions>(configuration.GetSection(GooglePlacesOptions.Section));
        services.AddHttpClient(OsmPlaceSearch.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60)); // per-search budget from ExternalSearchOptions
        services.AddHttpClient(GooglePlacesSearch.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddMemoryCache();
        services.AddSingleton<IOsmPlaceSearch, OsmPlaceSearch>();
        services.AddSingleton<IGooglePlacesSearch, GooglePlacesSearch>();
        services.AddHttpClient(PhotonPlaceGeocoder.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<IPlaceGeocoder, PhotonPlaceGeocoder>();

        services.Configure<RazorpayOptions>(configuration.GetSection(RazorpayOptions.Section));
        services.AddHttpClient<IPaymentGateway, RazorpayGateway>(c => c.Timeout = TimeSpan.FromSeconds(20));

        return services;
    }
}
