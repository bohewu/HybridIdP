using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Domain.Constants;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens.Saml2;
using Moq;
using Web.IdP.Controllers.Saml;
using Web.IdP.Extensions;
using Web.IdP.Services.Saml;
using Web.IdP.Services.Saml.Sp;

namespace Tests.Web.IdP.UnitTests;

public sealed class SamlSpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registration_ShouldOnlyExposeEnabledSchemeWithoutChangingDefaults(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("existing-default");
        services.AddSamlSp(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Saml:Sp:Enabled"] = enabled.ToString()
        }).Build());
        using var provider = services.BuildServiceProvider();
        Assert.Equal("existing-default", provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
        Assert.Equal(enabled, (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync("Saml")) is not null);
    }

    [Fact]
    public async Task DisabledEndpoints_ShouldReturn404WithoutStateStoreOrCertificates()
    {
        using var runtime = new SamlFederationRuntime(new SamlFederationOptions());
        var controller = new SamlSpController(runtime, NullLogger<SamlSpController>.Instance);
        Assert.IsType<NotFoundResult>(controller.Metadata());
        Assert.IsType<NotFoundResult>(await controller.AssertionConsumerService(default));
    }

    [Fact]
    public async Task Challenge_ShouldSignRequestAndBindSecureCrossSiteBrowserCookie()
    {
        using var fixture = new Fixture();
        var challenge = await fixture.ChallengeAsync();
        var query = QueryHelpers.ParseQuery(new Uri(challenge.Location).Query);
        Assert.Equal(43, challenge.Relay.Length);
        var signedText = new Uri(challenge.Location).Query[1..].Split("&Signature=")[0];
        using var rsa = fixture.Certificate.GetRSAPublicKey();
        Assert.True(rsa!.VerifyData(Encoding.UTF8.GetBytes(signedText), Convert.FromBase64String(query["Signature"]!),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var cookie = challenge.Cookie;
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/saml/sp/acs", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain(challenge.RequestId, cookie);
        Assert.Contains("WantAssertionsSigned=\"true\"", fixture.Service.Metadata());
        Assert.DoesNotContain("SingleLogoutService", fixture.Service.Metadata());
    }

    [Fact]
    public async Task Complete_ShouldIssueOnlyExternalTicketWithProtectedCallbackPropertiesAndNoPrivilegeClaims()
    {
        using var fixture = new Fixture();
        var challenge = await fixture.ChallengeAsync("/Account/LinkExternalLogin/Callback", "local-link-operation");
        var context = fixture.CallbackContext(challenge.Cookie);
        var response = fixture.Response(challenge.RequestId);
        var callback = await fixture.Service.CompleteAsync(context, response, challenge.Relay);
        Assert.Equal("/Account/LinkExternalLogin/Callback", callback);
        Assert.Equal(IdentityConstants.ExternalScheme, fixture.SignedScheme);
        Assert.Equal("local-link-operation", fixture.SignedProperties!.Items["test-operation"]);
        Assert.Equal("local-user", fixture.SignedProperties.Items["XsrfId"]);
        Assert.Equal("Saml", fixture.SignedProperties.Items["LoginProvider"]);
        Assert.False(fixture.SignedProperties.IsPersistent);
        var principal = fixture.SignedPrincipal!;
        Assert.StartsWith("saml-v1:", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("user@example.test", principal.FindFirstValue(ClaimTypes.Email));
        Assert.Null(principal.FindFirst("amr"));
        Assert.Null(principal.FindFirst(ClaimTypes.Role));
        Assert.Null(principal.FindFirst(AuthConstants.Claims.ExternalEmailVerified));
        Assert.Equal(1, fixture.SignInCount);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.CompleteAsync(context, response, challenge.Relay));
        Assert.Equal(1, fixture.SignInCount);
    }

    [Fact]
    public async Task Complete_ShouldRejectDifferentBrowserWithoutConsumingValidCorrelation()
    {
        using var fixture = new Fixture();
        var challenge = await fixture.ChallengeAsync();
        var response = fixture.Response(challenge.RequestId);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.CompleteAsync(fixture.CallbackContext(null), response, challenge.Relay));
        await fixture.Service.CompleteAsync(fixture.CallbackContext(challenge.Cookie), response, challenge.Relay);
        Assert.Equal(1, fixture.SignInCount);
    }

    [Fact]
    public async Task Complete_ShouldRejectSwappedRelayAndRequest()
    {
        using var fixture = new Fixture();
        var first = await fixture.ChallengeAsync();
        var second = await fixture.ChallengeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.CompleteAsync(fixture.CallbackContext(second.Cookie),
            fixture.Response(first.RequestId), second.Relay));
        Assert.Equal(0, fixture.SignInCount);
    }

    [Theory]
    [InlineData(SamlStatePurpose.SpResponseReplay)]
    [InlineData(SamlStatePurpose.SpAssertionReplay)]
    public async Task Complete_ShouldRejectPreviouslyReservedResponseOrAssertion(SamlStatePurpose purpose)
    {
        using var fixture = new Fixture();
        var challenge = await fixture.ChallengeAsync();
        var response = fixture.Response(challenge.RequestId);
        var validated = fixture.Validator.Validate(response);
        var id = purpose == SamlStatePurpose.SpResponseReplay ? validated.ResponseId : validated.AssertionId;
        await fixture.Store.Object.TryAddAsync(purpose, Fixture.Issuer.Length + ":" + Fixture.Issuer + id, "", DateTimeOffset.UtcNow.AddMinutes(10));
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.CompleteAsync(fixture.CallbackContext(challenge.Cookie), response, challenge.Relay));
        Assert.Equal(0, fixture.SignInCount);
    }

    [Fact]
    public async Task Complete_ShouldFailClosedWhenStateStoreFails()
    {
        using var fixture = new Fixture();
        var challenge = await fixture.ChallengeAsync();
        fixture.Store.Setup(s => s.TryTakeAsync(SamlStatePurpose.SpCorrelation, challenge.Relay, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("state unavailable"));
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.CompleteAsync(fixture.CallbackContext(challenge.Cookie),
            fixture.Response(challenge.RequestId), challenge.Relay));
        Assert.Equal(0, fixture.SignInCount);
    }

    [Theory]
    [InlineData("unsigned-response")]
    [InlineData("unsigned-assertion")]
    [InlineData("tampered-claim")]
    [InlineData("issuer")]
    [InlineData("destination")]
    [InlineData("audience")]
    [InlineData("recipient")]
    [InlineData("subject-request")]
    [InlineData("unsolicited")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("long-lived")]
    [InlineData("transient")]
    [InlineData("name-qualifier")]
    [InlineData("sp-qualifier")]
    [InlineData("duplicate-assertion")]
    [InlineData("duplicate-id")]
    [InlineData("wrong-reference")]
    public void Validate_ShouldRejectInvalidProfileOrSignature(string mutation)
    {
        using var fixture = new Fixture();
        XNamespace a = "urn:oasis:names:tc:SAML:2.0:assertion";
        XNamespace s = "http://www.w3.org/2000/09/xmldsig#";
        var document = XDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(fixture.Response("_request"))));
        var root = document.Root!;
        var assertion = root.Element(a + "Assertion")!;
        var data = assertion.Descendants(a + "SubjectConfirmationData").Single();
        var name = assertion.Descendants(a + "NameID").Single();
        switch (mutation)
        {
            case "unsigned-response": root.Element(s + "Signature")!.Remove(); break;
            case "unsigned-assertion": assertion.Element(s + "Signature")!.Remove(); break;
            case "tampered-claim": assertion.Descendants(a + "AttributeValue").First().Value = "tampered"; break;
            case "issuer": root.Element(a + "Issuer")!.Value = "https://other.example/idp"; break;
            case "destination": root.SetAttributeValue("Destination", "https://other.example/acs"); break;
            case "audience": assertion.Descendants(a + "Audience").Single().Value = "https://other.example/sp"; break;
            case "recipient": data.SetAttributeValue("Recipient", "https://other.example/acs"); break;
            case "subject-request": data.SetAttributeValue("InResponseTo", "_other"); break;
            case "unsolicited": root.Attribute("InResponseTo")!.Remove(); break;
            case "expired": data.SetAttributeValue("NotOnOrAfter", DateTime.UtcNow.AddMinutes(-2).ToString("O")); break;
            case "future": root.SetAttributeValue("IssueInstant", DateTime.UtcNow.AddMinutes(2).ToString("O")); break;
            case "long-lived": data.SetAttributeValue("NotOnOrAfter", DateTime.UtcNow.AddHours(1).ToString("O")); break;
            case "transient": name.SetAttributeValue("Format", "urn:oasis:names:tc:SAML:2.0:nameid-format:transient"); break;
            case "name-qualifier": name.SetAttributeValue("NameQualifier", "https://other.example/idp"); break;
            case "sp-qualifier": name.SetAttributeValue("SPNameQualifier", "https://other.example/sp"); break;
            case "duplicate-assertion": root.Add(new XElement(assertion)); break;
            case "duplicate-id": assertion.SetAttributeValue("ID", root.Attribute("ID")!.Value); break;
            case "wrong-reference": root.Element(s + "Signature")!.Descendants(s + "Reference").Single().SetAttributeValue("URI", "#other"); break;
        }
        Assert.ThrowsAny<Exception>(() => fixture.Validator.Validate(Convert.ToBase64String(Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)))));
    }

    [Fact]
    public void Validate_ShouldRejectUntrustedCertificateAndDtd()
    {
        using var trusted = new Fixture();
        using var untrusted = new Fixture();
        Assert.ThrowsAny<Exception>(() => trusted.Validator.Validate(untrusted.Response("_request")));
        Assert.ThrowsAny<Exception>(() => trusted.Validator.Validate(Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "<!DOCTYPE Response [<!ENTITY sample 'bad'>]><Response>&sample;</Response>"))));
    }

    [Fact]
    public void Validate_ShouldNormalizeOptionalQualifiersAndKeepOpaqueNameIdSeparateFromEmail()
    {
        using var fixture = new Fixture();
        var explicitKey = fixture.Validator.Validate(fixture.Response("_request")).Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var implicitKey = fixture.Validator.Validate(fixture.Response("_request", qualifiers: false)).Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var otherKey = fixture.Validator.Validate(fixture.Response("_request", subject: "different-subject")).Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        Assert.Equal(explicitKey, implicitKey);
        Assert.NotEqual(explicitKey, otherKey);
        Assert.NotEqual("user@example.test", explicitKey);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Issuer = "https://upstream.example/idp";
        private const string Entity = "https://hybrid.example/sp";
        private const string Acs = "https://hybrid.example/saml/sp/acs";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "saml-sp-" + Guid.NewGuid().ToString("N"));
        private readonly SamlFederationRuntime _runtime;
        private readonly ServiceProvider _services;
        public X509Certificate2 Certificate { get; }
        public SamlSpService Service { get; }
        public SamlSpResponseValidator Validator { get; }
        public Mock<ISamlStateStore> Store { get; } = new();
        public string? SignedScheme { get; private set; }
        public ClaimsPrincipal? SignedPrincipal { get; private set; }
        public AuthenticationProperties? SignedProperties { get; private set; }
        public int SignInCount { get; private set; }

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            using var rsa = RSA.Create(2048);
            Certificate = new CertificateRequest("CN=SAML-SP-Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var pfx = Path.Combine(_directory, "signing.pfx");
            var cer = Path.Combine(_directory, "trust.cer");
            File.WriteAllBytes(pfx, Certificate.Export(X509ContentType.Pfx));
            File.WriteAllBytes(cer, Certificate.Export(X509ContentType.Cert));
            _runtime = new SamlFederationRuntime(new SamlFederationOptions
            {
                Sp = new SamlSpOptions
                {
                    Enabled = true, EntityId = Entity, AssertionConsumerServiceUrl = Acs,
                    SigningCertificate = new SamlSigningCertificateOptions { PfxPath = pfx },
                    IdentityProvider = new SamlIdentityProviderOptions
                    {
                        EntityId = Issuer, SingleSignOnUrl = "https://upstream.example/sso", SigningCertificatePaths = [cer]
                    }
                }
            });
            var state = new ConcurrentDictionary<(SamlStatePurpose, string), string>();
            Store.Setup(s => s.TryAddAsync(It.IsAny<SamlStatePurpose>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SamlStatePurpose purpose, string key, string payload, DateTimeOffset expires, CancellationToken _) => state.TryAdd((purpose, key), payload));
            Store.Setup(s => s.TryTakeAsync(It.IsAny<SamlStatePurpose>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SamlStatePurpose purpose, string key, CancellationToken _) => state.TryRemove((purpose, key), out var payload) ? payload : null);
            Validator = new SamlSpResponseValidator(_runtime, TimeProvider.System);
            Service = new SamlSpService(_runtime, Store.Object, new EphemeralDataProtectionProvider(), TimeProvider.System, Validator);
            var auth = new Mock<IAuthenticationService>();
            auth.Setup(a => a.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
                .Callback((HttpContext _, string scheme, ClaimsPrincipal principal, AuthenticationProperties properties) =>
                {
                    SignedScheme = scheme; SignedPrincipal = principal; SignedProperties = properties; SignInCount++;
                }).Returns(Task.CompletedTask);
            _services = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
        }

        public DefaultHttpContext CallbackContext(string? cookie)
        {
            var context = new DefaultHttpContext { RequestServices = _services };
            context.Request.Scheme = "https";
            if (cookie is not null) context.Request.Headers.Cookie = cookie.Split(';')[0];
            return context;
        }

        public async Task<(string Location, string Relay, string RequestId, string Cookie)> ChallengeAsync(
            string callback = "/Account/ExternalLoginCallback?returnUrl=%2F", string operation = "operation")
        {
            var context = CallbackContext(null);
            var properties = new AuthenticationProperties { RedirectUri = callback };
            properties.Items["LoginProvider"] = "Saml";
            properties.Items["XsrfId"] = "local-user";
            properties.Items["test-operation"] = operation;
            var location = await Service.ChallengeAsync(context, properties);
            var query = QueryHelpers.ParseQuery(new Uri(location).Query);
            using var compressed = new MemoryStream(Convert.FromBase64String(query["SAMLRequest"]!));
            using var deflate = new System.IO.Compression.DeflateStream(compressed, System.IO.Compression.CompressionMode.Decompress);
            var xml = XDocument.Load(deflate);
            Assert.Equal(SamlProfile.PersistentNameIdFormat, xml.Root!.Elements().Single(e => e.Name.LocalName == "NameIDPolicy").Attribute("Format")!.Value);
            return (location, query["RelayState"]!, xml.Root.Attribute("ID")!.Value, context.Response.Headers.SetCookie.ToString());
        }

        public string Response(string requestId, bool qualifiers = true, string subject = "opaque-subject")
        {
            var config = new Saml2Configuration
            {
                Issuer = Issuer, SigningCertificate = Certificate,
                SignatureAlgorithm = Saml2SecurityAlgorithms.RsaSha256Signature,
                AuthnResponseSignType = Saml2AuthnResponseSignTypes.SignAssertionAndResponse
            };
            var response = new Saml2AuthnResponse(config)
            {
                InResponseTo = new Saml2Id(requestId), Destination = new Uri(Acs), Status = Saml2StatusCodes.Success,
                NameId = new Saml2NameIdentifier(subject)
                {
                    Format = new Uri(SamlProfile.PersistentNameIdFormat), NameQualifier = qualifiers ? Issuer : null,
                    SPNameQualifier = qualifiers ? Entity : null
                },
                ClaimsIdentity = new ClaimsIdentity([
                    new Claim(ClaimTypes.Name, "Example User"), new Claim(ClaimTypes.Email, "user@example.test"),
                    new Claim("amr", "mfa"), new Claim(ClaimTypes.Role, "Admin"),
                    new Claim(AuthConstants.Claims.ExternalEmailVerified, "true")])
            };
            response.CreateSecurityToken(Entity, subjectConfirmationLifetime: 5, issuedTokenLifetime: 5);
            var binding = new Saml2PostBinding();
            binding.Bind(response);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(binding.XmlDocument.OuterXml));
        }

        public void Dispose()
        {
            _services.Dispose();
            _runtime.Dispose();
            Certificate.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
