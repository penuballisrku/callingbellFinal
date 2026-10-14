using System.Text;
using System.Threading.RateLimiting;
using CallingBell.Api.Hubs;
using CallingBell.Api.Infrastructure;
using CallingBell.Api.Seo;
using CallingBell.Application;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Common.Models;
using CallingBell.Domain.Constants;
using CallingBell.Infrastructure;
using CallingBell.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // As a Windows service the working folder is System32: keep App_Data and appsettings relative to the app.
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});
// Runs as a Windows service in production (no effect under dotnet run).
builder.Host.UseWindowsService(o => o.ServiceName = "CallingBell");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton<CallingBell.Api.Hubs.OwnerPresenceTracker>();
builder.Services.AddSingleton<IPlatformStatsSignal, CallingBell.Api.Hubs.PlatformStatsBroadcaster>();
builder.Services.AddScoped<CallingBell.Application.Features.Chat.IChatRealtime, ChatRealtime>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Model-binding errors use the same envelope as FluentValidation errors.
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
            return new BadRequestObjectResult(ApiResponse.Fail("One or more validation errors occurred.", errors));
        };
    });

// ---------- Authentication & authorization (JWT + RBAC + permission policies) ----------
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be configured with at least 32 bytes (use user-secrets or an environment variable).");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        // SignalR sends the token in the query string for WebSocket connections.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in typeof(Permissions).GetFields().Where(f => f.IsLiteral && f.Name != nameof(Permissions.ClaimType)))
    {
        var value = (string)permission.GetValue(null)!;
        options.AddPolicy(value, policy => policy.RequireClaim(Permissions.ClaimType, value));
    }
});

// ---------- Rate limiting ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("submissions", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10) }));
    // Search beyond the platform calls OpenStreetMap and (when configured) the paid Google Places API.
    options.AddPolicy("public-search", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    // The AI search assistant: one request per message, plus a few repeats while the local AI is still working on one.
    // Chat messages and attachments: per signed-in user.
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 40, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("assistant", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 40, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Fail("Too many requests. Please slow down and try again shortly."), ct);
    };
});

// ---------- Real-time ----------
builder.Services.AddSignalR();

// ---------- CORS ----------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ---------- OpenAPI ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "CallInBell API", Version = "v1", Description = "Discover. Connect. Book. Grow." });
    options.CustomSchemaIds(type => type.FullName!.Replace("+", "."));
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});

// Performance: compress JSON and SVG responses. Responses are not cached on the server, so every request shows current data.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml", "application/problem+json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddHostedService<StartupWarmup>();

// ---------- SEO ----------
builder.Services.Configure<CallingBell.Application.Features.Seo.SeoOptions>(builder.Configuration.GetSection(CallingBell.Application.Features.Seo.SeoOptions.Section));
builder.Services.AddSingleton<CallingBell.Api.Seo.SeoHtmlRenderer>();

// HTTP to HTTPS is a permanent move (308, not the default 307), so search engines index only the https:// address.
builder.Services.AddHttpsRedirection(o => o.RedirectStatusCode = StatusCodes.Status308PermanentRedirect);

var app = builder.Build();

// Simulated payments activate plans without money: never outside development.
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Payments:Test:Enabled"))
    throw new InvalidOperationException("Payments:Test:Enabled is only allowed in the Development environment.");

// Behind Cloudflare every request arrives from Cloudflare's edge (proxied DNS) or from cloudflared on this machine (tunnel): take the
// visitor's IP and the https scheme from Cloudflare's headers so rate limits, audit logs and location work per visitor. Trusted only
// from loopback and Cloudflare's published ranges (Cloudflare:TrustedRanges), so a direct caller can't spoof CF-Connecting-IP.
if (app.Configuration.GetValue<bool>("Cloudflare:Enabled"))
{
    var cloudflareRanges = (app.Configuration.GetSection("Cloudflare:TrustedRanges").Get<string[]>() ?? [])
        .Select(System.Net.IPNetwork.Parse).ToArray();
    app.Use((context, next) =>
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is { IsIPv4MappedToIPv6: true }) remote = remote.MapToIPv4();
        if (remote is not null && (System.Net.IPAddress.IsLoopback(remote) || cloudflareRanges.Any(r => r.Contains(remote))))
        {
            if (System.Net.IPAddress.TryParse(context.Request.Headers["CF-Connecting-IP"].ToString(), out var visitor))
                context.Connection.RemoteIpAddress = visitor;
            if (context.Request.Headers["X-Forwarded-Proto"] == "https") context.Request.Scheme = "https";
        }
        return next(context);
    });
}

// One address for search engines: the bare domain (callinbell.com) moves permanently to the canonical host of Seo:SiteUrl
// (www.callinbell.com) with the same path and query, so its pages never compete with themselves as duplicates.
if (!app.Environment.IsDevelopment() && Uri.TryCreate(app.Configuration["Seo:SiteUrl"], UriKind.Absolute, out var canonicalSite)
    && canonicalSite.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
{
    var apexHost = canonicalSite.Host[4..];
    app.Use((context, next) =>
    {
        if (!string.Equals(context.Request.Host.Host, apexHost, StringComparison.OrdinalIgnoreCase)) return next(context);
        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = $"{canonicalSite.Scheme}://{canonicalSite.Authority}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        return Task.CompletedTask;
    });
}

app.UseExceptionHandler();
app.UseResponseCompression();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "CallInBell API");
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// The built React app with each public page's SEO rendered in, when Seo:SpaRoot points at it (production). In development Vite serves
// the app and proxies robots.txt and the sitemaps here.
if (CallingBell.Api.Seo.SpaHosting.Root(app.Configuration, app.Environment) is { } spaRoot) app.UseSpaWithSeo(spaRoot);

app.MapControllers();
app.MapHub<PresenceHub>("/hubs/presence");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<VideoHub>("/hubs/video");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();

app.Run();
