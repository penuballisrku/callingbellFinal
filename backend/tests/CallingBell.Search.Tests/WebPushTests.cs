using System.Security.Cryptography;
using System.Text;
using CallingBell.Application.Features.Notifications;
using CallingBell.Infrastructure.Notifications;
using CallingBell.Infrastructure.Notifications.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallingBell.Search.Tests;

/// <summary>Standard Web Push (VAPID): subscriptions the server accepts, message encryption and signing.</summary>
public sealed class WebPushTests
{
    private const string P256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string Auth = "BTBZMqHH6r4Tts7J_aSIgg";

    private static string Subscription(string endpoint, string p256dh = P256dh, string auth = Auth) =>
        $$"""{"endpoint":"{{endpoint}}","p256dh":"{{p256dh}}","auth":"{{auth}}"}""";

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc123")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc")]
    [InlineData("https://web.push.apple.com/QGx")]
    public void Accepts_browser_push_services(string endpoint)
    {
        Assert.True(WebPushSubscription.TryParse(Subscription(endpoint), out var s));
        Assert.Equal(65, s!.P256dh.Length);
        Assert.Equal(16, s.Auth.Length);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]          // not HTTPS
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]    // another port
    [InlineData("https://evil.example.com/collect")]                 // not a push service
    [InlineData("https://fcm.googleapis.com.evil.com/x")]            // look-alike host
    [InlineData("https://127.0.0.1/x")]                              // internal address
    public void Rejects_other_endpoints(string endpoint) => Assert.False(WebPushSubscription.TryParse(Subscription(endpoint), out _));

    [Fact]
    public void Rejects_bad_keys_and_text()
    {
        const string ok = "https://fcm.googleapis.com/fcm/send/abc";
        Assert.False(WebPushSubscription.TryParse(Subscription(ok, p256dh: "AAAA"), out _));
        Assert.False(WebPushSubscription.TryParse(Subscription(ok, auth: "AAAA"), out _));
        Assert.False(WebPushSubscription.TryParse("not json", out _));
        Assert.False(WebPushSubscription.TryParse("[]", out _));
        Assert.False(WebPushSubscription.TryParse(null, out _));
    }

    /// <summary>RFC 8291, Appendix A: the published example must come out byte for byte.</summary>
    [Fact]
    public void Encryption_matches_the_rfc_8291_example()
    {
        static byte[] B(string s) => Base64UrlEncoder.DecodeBytes(s);
        var senderPublic = B("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256, D = B("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"),
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..] },
        });
        var body = WebPushEncryption.Encrypt(Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon"), B(P256dh), B(Auth), sender, B("DGv6ra1nlYgDCS1FRnbzlw"));
        Assert.Equal("DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            Base64UrlEncoder.Encode(body));
    }

    [Fact]
    public void Vapid_token_is_signed_for_the_push_service_origin()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        var publicKey = Base64UrlEncoder.Encode([0x04, .. p.Q.X!, .. p.Q.Y!]);
        var signer = new VapidSigner(Options.Create(new NotificationOptions { WebPush = new WebPushOptions
        {
            Enabled = true, PublicKey = publicKey, PrivateKey = Base64UrlEncoder.Encode(p.D!), Subject = "https://www.callinbell.com",
        } }), NullLogger<VapidSigner>.Instance);

        var header = signer.AuthorizationFor(new Uri("https://fcm.googleapis.com/fcm/send/abc"));
        Assert.EndsWith($", k={publicKey}", header);
        var jwt = header["vapid t=".Length..header.IndexOf(',')].Split('.');
        Assert.Contains("\"aud\":\"https://fcm.googleapis.com\"", Base64UrlEncoder.Decode(jwt[1]));
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = p.Q });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), Base64UrlEncoder.DecodeBytes(jwt[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Mismatched_vapid_keys_turn_push_off()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = key.ExportParameters(false).Q;
        var signer = new VapidSigner(Options.Create(new NotificationOptions { WebPush = new WebPushOptions
        {
            Enabled = true, PublicKey = Base64UrlEncoder.Encode([0x04, .. q.X!, .. q.Y!]), PrivateKey = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
        } }), NullLogger<VapidSigner>.Instance);
        Assert.False(signer.IsConfigured);
    }
}
