using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TestClient.Saml.Controllers;
using TestClient.Saml.Protocol;
using Xunit;

namespace TestClient.Saml.Tests;

public sealed class SampleTests
{
    [Fact]
    public void RoundTrip_ShouldValidateSignedRequestAndBothResponseSignatures()
    {
        using var fixture = new Fixture();
        var proof = fixture.Consumer.Validate(fixture.Response());
        Assert.Equal(fixture.Request.RequestId, proof.RequestId);
        Assert.Equal(Fixture.Idp, proof.Issuer);
        Assert.Equal(Fixture.Subject.ToString("N"), proof.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Single(proof.Principal.Claims);
    }

    [Fact]
    public void Response_ShouldRejectTamperingAndUnpinnedSigningKey()
    {
        using var fixture = new Fixture();
        using var stranger = new Fixture();
        Assert.ThrowsAny<Exception>(() => fixture.Consumer.Validate(stranger.Response()));
        var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fixture.Response()));
        text = text.Replace(Fixture.Subject.ToString("N"), Guid.NewGuid().ToString("N"), StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => fixture.Consumer.Validate(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void Correlation_ShouldRejectCopiedUrlWithoutBrowserCookie()
    {
        var store = new BrowserStateStore(new EphemeralDataProtectionProvider());
        var context = new DefaultHttpContext();
        var token = BrowserStateStore.RandomToken();
        store.BindBrowser(context, token, "nonce", "/saml/sp/acs", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.False(store.MatchesBrowser(new DefaultHttpContext(), token, "nonce"));
        var browser = new DefaultHttpContext();
        browser.Request.Headers.Cookie = context.Response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.True(store.MatchesBrowser(browser, token, "nonce"));
        Assert.False(store.MatchesBrowser(browser, token, "other-nonce"));
    }

    [Fact]
    public async Task Correlation_ShouldHaveExactlyOneConcurrentConsumer()
    {
        var store = new BrowserStateStore(new EphemeralDataProtectionProvider());
        Assert.True(store.Add("correlation", "payload", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.False(store.Add("correlation", "replacement", DateTimeOffset.UtcNow.AddMinutes(5)));
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.Find<string>("correlation", true))));
        Assert.Equal(1, results.Count(value => value == "payload"));
        Assert.Null(store.Find<string>("correlation"));
        Assert.False(store.Add("expired", "payload", DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.False(store.Add("too-long", "payload", DateTimeOffset.UtcNow.AddMinutes(11)));
    }

    [Fact]
    public void DisabledRoles_ShouldNeedNoCertificatesAndRejectProtocolOperations()
    {
        using var runtime = new SamlFederationRuntime(new());
        Assert.Throws<InvalidOperationException>(() => runtime.CreateSpConfiguration());
        Assert.Throws<InvalidOperationException>(() => runtime.CreateIdpConfiguration("unknown"));
    }

    private sealed class Fixture : IDisposable
    {
        public const string Idp = "https://producer.example/idp";
        public const string Sp = "https://consumer.example/sp";
        public static readonly Guid Subject = new("fd294c09-5105-4c36-8833-5f9c288523cd");
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "saml-sample-test-" + Guid.NewGuid().ToString("N"));
        private readonly SamlFederationRuntime _runtime;
        public SamlIdpProtocol Producer { get; }
        public SamlSpResponseValidator Consumer { get; }
        public SamlIdpRequest Request { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            using var rsa = RSA.Create(2048);
            using var certificate = new CertificateRequest("CN=SAML Sample Fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var pfx = Path.Combine(_directory, "signing.pfx");
            var cer = Path.Combine(_directory, "signing.cer");
            File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pfx));
            File.WriteAllBytes(cer, certificate.Export(X509ContentType.Cert));
            _runtime = new(new SamlFederationOptions
            {
                Idp = new()
                {
                    Enabled = true, EntityId = Idp, SingleSignOnUrl = "https://producer.example/saml/idp/sso",
                    SigningCertificate = new() { PfxPath = pfx },
                    ServiceProviders = [new() { EntityId = Sp, AssertionConsumerServiceUrl = "https://consumer.example/saml/sp/acs", SigningCertificatePaths = [cer] }]
                },
                Sp = new()
                {
                    Enabled = true, EntityId = Sp, AssertionConsumerServiceUrl = "https://consumer.example/saml/sp/acs",
                    SigningCertificate = new() { PfxPath = pfx },
                    IdentityProvider = new() { EntityId = Idp, SingleSignOnUrl = "https://producer.example/saml/idp/sso", SigningCertificatePaths = [cer] }
                }
            });
            Producer = new(_runtime, TimeProvider.System);
            Consumer = new(_runtime, TimeProvider.System);
            var controller = new DemoController(_runtime, new(new EphemeralDataProtectionProvider()), Producer, Consumer, new())
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
            controller.Request.Scheme = "https";
            var location = Assert.IsType<RedirectResult>(controller.Login()).Url;
            var http = new DefaultHttpContext();
            http.Request.Scheme = "https";
            http.Request.Method = "GET";
            http.Request.QueryString = new QueryString(new Uri(location).Query);
            Request = Producer.ReadRequest(http.Request);
        }

        public string Response() => Producer.CreateResponse(Request, Subject, DateTimeOffset.UtcNow);

        public void Dispose()
        {
            _runtime.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
