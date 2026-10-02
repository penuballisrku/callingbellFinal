using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using CallingBell.Infrastructure.Geo;
using CallingBell.Infrastructure.Identity;
using CallingBell.Infrastructure.Payments;
using CallingBell.Infrastructure.Persistence;
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

        services.Configure<RazorpayOptions>(configuration.GetSection(RazorpayOptions.Section));
        services.AddHttpClient<IPaymentGateway, RazorpayGateway>(c => c.Timeout = TimeSpan.FromSeconds(20));

        return services;
    }
}
