using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CallingBell.Infrastructure.Identity;

/// <summary>
/// Google sign-in settings. The client id is a public identifier (it is in every page that shows the button), not a secret; empty =
/// Google sign-in is off. Create it in Google Cloud Console → APIs &amp; Services → Credentials → OAuth client ID (Web application), with
/// the site's addresses as Authorised JavaScript origins.
/// </summary>
public sealed class GoogleSignInOptions
{
    public const string Section = "Authentication:Google";
    public string ClientId { get; set; } = string.Empty;
    /// <summary>Google's sign-in authority; its OpenID configuration and keys are read from here. Only changed for local testing.</summary>
    public string Authority { get; set; } = "https://accounts.google.com";
}

/// <summary>
/// Checks a Google ID token (from the "Sign in with Google" button) the way Google requires: signed with one of Google's current keys
/// (from its OpenID configuration, cached and refreshed automatically), issued by accounts.google.com for this app's client id, not expired.
/// </summary>
internal sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly IOptions<GoogleSignInOptions> options;
    private readonly ILogger<GoogleTokenValidator> logger;
    private readonly string authority;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> Google;

    public GoogleTokenValidator(IOptions<GoogleSignInOptions> options, ILogger<GoogleTokenValidator> logger)
    {
        this.options = options;
        this.logger = logger;
        authority = options.Value.Authority.TrimEnd('/');
        // HTTPS always, except a local test authority on this machine.
        var local = Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.IsLoopback;
        Google = new ConfigurationManager<OpenIdConnectConfiguration>($"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = !local })
        {
            // Google's keys are re-read every 12 hours, and at most a minute after a token arrives signed with a key we don't have yet.
            RefreshInterval = TimeSpan.FromMinutes(1),
        };
    }

    public string? ClientId => string.IsNullOrWhiteSpace(options.Value.ClientId) ? null : options.Value.ClientId.Trim();

    public async Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken ct)
    {
        var clientId = ClientId ?? throw new BadRequestException("Google sign-in isn't available right now.");
        OpenIdConnectConfiguration config;
        try { config = await Google.GetConfigurationAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't load Google's sign-in keys");
            throw new BadRequestException("Google sign-in isn't responding right now. Please try again, or sign in another way.");
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            // Google issues with or without the scheme.
            ValidIssuers = [authority, new Uri(authority).Host],
            ValidAudience = clientId,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        });
        if (!result.IsValid)
        {
            // Google rotates its keys: re-read them, then the person is asked to try again.
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException) Google.RequestRefresh();
            logger.LogInformation("Google sign-in token rejected: {Reason}", result.Exception?.GetType().Name);
            throw new BadRequestException("Google sign-in didn't work. Please try again.");
        }

        string? Claim(string type) => result.Claims.TryGetValue(type, out var v) ? v?.ToString() : null;
        var subject = Claim("sub");
        var email = Claim("email");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
            throw new BadRequestException("Your Google account didn't share an email address, so we can't sign you in with it.");
        var verified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        var name = Claim("name") is { Length: > 0 } n ? n : email.Split('@')[0];
        return new GoogleIdentity(subject, email.Trim().ToLowerInvariant(), verified, name.Trim(), Claim("picture"));
    }
}
