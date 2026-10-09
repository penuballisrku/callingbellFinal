using CallingBell.Infrastructure.Notifications.Providers;
using CallingBell.Infrastructure.Notifications;
using CallingBell.Application.Features.Notifications.Delivery;
using CallingBell.Application.Features.Notifications;
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
using Microsoft.Extensions.Options;

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
        services.AddScoped<IIdentityService, IdentityService>();
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.Section));
        services.AddScoped<IPhoneOtpService, PhoneOtpService>();
        services.AddSingleton<OtpCodeVault>();
        services.AddScoped<IOtpFallback, OtpSmsFallback>();

        // ---------- Notifications beyond the app: web push, WhatsApp, RCS, SMS (credentials from env / Key Vault only) ----------
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.Section));
        var notify = configuration.GetSection(NotificationOptions.Section).Get<NotificationOptions>() ?? new NotificationOptions();
        services.AddHttpClient(FcmWebPushProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(GoogleServiceAccountTokens.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(MetaWhatsAppClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(Math.Clamp(notify.WhatsApp.TimeoutSeconds, 2, 30)));
        services.AddHttpClient(ProviderHttp.RcsClient, c => c.Timeout = TimeSpan.FromSeconds(Math.Clamp(notify.Rcs.TimeoutSeconds, 2, 30)));
        services.AddHttpClient(ProviderHttp.SmsClient, c => c.Timeout = TimeSpan.FromSeconds(Math.Clamp(notify.Sms.TimeoutSeconds, 2, 30)));
        services.AddSingleton<GoogleServiceAccountTokens>();
        services.AddSingleton<IDeliveryAckSigner, DeliveryAckSigner>();
        services.AddSingleton<CallingBell.Application.Features.Chat.IChatAttachmentSigner, ChatAttachmentSigner>();
        services.Configure<CallingBell.Application.Features.Video.VideoOptions>(configuration.GetSection(CallingBell.Application.Features.Video.VideoOptions.Section));
        services.AddSingleton<IWebPushClientConfig, WebPushClientConfig>();
        services.AddScoped<MetaWhatsAppClient>();
        services.AddScoped<INotificationProvider, FcmWebPushProvider>();
        services.AddScoped<INotificationProvider, WhatsAppProvider>();
        services.AddScoped<INotificationProvider, WhatsAppAuthenticationProvider>();
        services.AddScoped<INotificationProvider, RcsProvider>();
        services.AddScoped<INotificationProvider, Msg91SmsProvider>();
        services.AddScoped<INotificationProvider, TwilioSmsProvider>();
        services.AddScoped<INotificationProvider, LogSmsProvider>();
        services.AddSingleton<NotificationDispatchSignal>();
        services.AddSingleton<INotificationDispatchSignal>(sp => sp.GetRequiredService<NotificationDispatchSignal>());
        services.AddHostedService<NotificationDispatcher>();

        services.Configure<GeoIpOptions>(configuration.GetSection(GeoIpOptions.Section));
        services.AddSingleton<IGeoLocationService, MaxMindGeoLocationService>();
        services.Configure<IpApiOptions>(configuration.GetSection(IpApiOptions.Section));
        services.AddSingleton<IIpLocationService, IpApiLocationService>();
        services.AddHttpClient(IpApiLocationService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(4));
        services.AddSingleton<DevelopmentPublicIp>();
        services.AddHttpClient(DevelopmentPublicIp.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5));

        // Area discovery agent: OpenStreetMap places + India Post / OSM PIN codes, cleaned up by the local AI model, in the background.
        services.Configure<AreaDiscoveryOptions>(configuration.GetSection(AreaDiscoveryOptions.Section));
        services.AddHttpClient(AreaDiscoveryRun.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(120))
            // The free OpenStreetMap / Wikidata servers drop connections when busy (even mid TLS handshake): connect within 15 s, and don't
            // keep reusing a connection for long, so a retry opens a fresh one instead of a dead one.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            });
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
        services.AddHostedService<AiModelWarmup>();
        services.AddSingleton<OllamaChatClient>();
        services.AddSingleton<AiWorkQueue>();
        services.AddSingleton(typeof(AiResultCache<>));
        services.AddHostedService<AiWorker>();
        services.AddSingleton<IServiceRecommender, OllamaServiceRecommender>();
        services.AddSingleton<ICityCategoryRecommender, OllamaCityCategoryRecommender>();
        services.AddSingleton<IReviewSummarizer, OllamaReviewSummarizer>();
        services.AddSingleton<ISearchAssistant, OllamaSearchAssistant>();
        // Fast meaning-based search (Ollama embeddings): matches free text to categories in milliseconds, inside the request.
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.Section));
        services.AddSingleton<SemanticCatalog>();
        services.AddSingleton<ISemanticCatalog>(sp => sp.GetRequiredService<SemanticCatalog>());
        services.AddHostedService<SemanticCatalogWorker>();

        // Search results beyond the platform: OpenStreetMap places (free) and Google Maps (needs a Places API key).
        services.Configure<ExternalSearchOptions>(configuration.GetSection(ExternalSearchOptions.Section));
        services.Configure<GooglePlacesOptions>(configuration.GetSection(GooglePlacesOptions.Section));
        services.AddHttpClient(OsmPlaceSearch.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60)); // per-search budget from ExternalSearchOptions
        // Keeps the TLS connection to Google open between searches (the default drops it after a minute idle, and a new one costs
        // ~1-2 s), and multiplexes concurrent requests (results plus per-card contact lookups) over HTTP/2.
        services.AddHttpClient(GooglePlacesSearch.HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
                c.DefaultRequestVersion = System.Net.HttpVersion.Version20;
                c.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                EnableMultipleHttp2Connections = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            });
        services.AddMemoryCache();
        services.AddSingleton<IOsmPlaceSearch, OsmPlaceSearch>();
        services.AddSingleton<IGooglePlacesSearch, GooglePlacesSearch>();
        services.AddScoped<IPopularSearchCounter, PopularSearchCounter>();
        services.AddHttpClient(PhotonPlaceGeocoder.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<IPlaceGeocoder, PhotonPlaceGeocoder>();
        // Join Calling Bell: photos copied from the source only when Onboarding:AllowExternalPhotoImport is on (licence permitting).
        services.Configure<CallingBell.Application.Features.Onboarding.OnboardingOptions>(configuration.GetSection(CallingBell.Application.Features.Onboarding.OnboardingOptions.Section));
        services.AddHttpClient(ExternalPhotoFetcher.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<CallingBell.Application.Features.Onboarding.IExternalPhotoFetcher, ExternalPhotoFetcher>();

        services.Configure<RazorpayOptions>(configuration.GetSection(RazorpayOptions.Section));
        services.AddHttpClient<RazorpayGateway>(c => c.Timeout = TimeSpan.FromSeconds(20));
        // Razorpay when its keys are set; otherwise, in development, simulated test payments (Payments:Test:Enabled).
        services.Configure<TestPaymentOptions>(configuration.GetSection(TestPaymentOptions.Section));
        services.AddSingleton<TestPaymentGateway>();
        services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<RazorpayGateway>() is { IsConfigured: true } razorpay ? razorpay
            : sp.GetRequiredService<IOptions<TestPaymentOptions>>().Value.Enabled ? sp.GetRequiredService<TestPaymentGateway>()
            : sp.GetRequiredService<RazorpayGateway>());

        return services;
    }
}
