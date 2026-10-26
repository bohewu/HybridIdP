using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Core.Application.Interfaces;
using Core.Application.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Web.IdP.Extensions;
using Web.IdP.Services.Saml;

namespace Tests.Web.IdP.UnitTests;

public sealed class SamlFoundationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AddSamlFoundation_ShouldEnableOnlyExplicitRolesAndPreserveAuthenticationDefaults(bool idp, bool sp)
    {
        using var certificates = new CertificateFixture();
        var configuration = certificates.Configuration(idp, sp);
        var services = new ServiceCollection();
        services.AddAuthentication("existing-oidc-cookie");
        services.AddSamlFoundation(configuration);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<SamlFederationRuntime>();
        Assert.Equal(idp, runtime.Options.Idp.Enabled);
        Assert.Equal(sp, runtime.Options.Sp.Enabled);
        Assert.Equal("existing-oidc-cookie", provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
        Assert.Equal(idp || sp, services.Any(service => service.ServiceType == typeof(ISamlStateStore)));
        if (idp)
        {
            var config = runtime.CreateIdpConfiguration("https://sample-sp.example/entity");
            Assert.True(config.SignAuthnRequest);
            Assert.Single(config.SignatureValidationCertificates);
            Assert.NotSame(config, runtime.CreateIdpConfiguration("https://sample-sp.example/entity"));
            Assert.Throws<InvalidOperationException>(() => runtime.CreateIdpConfiguration("https://untrusted.example/entity"));
        }
        else Assert.Throws<InvalidOperationException>(() => runtime.CreateIdpConfiguration("https://sample-sp.example/entity"));
        if (sp)
        {
            var config = runtime.CreateSpConfiguration();
            Assert.Equal("https://sample-idp.example/entity", config.AllowedIssuer);
            Assert.Equal("https://hybrid.example/sp", Assert.Single(config.AllowedAudienceUris));
            Assert.True(config.SignAuthnRequest);
            Assert.Empty(config.DecryptionCertificates);
        }
        else Assert.Throws<InvalidOperationException>(() => runtime.CreateSpConfiguration());
    }

    [Fact]
    public void AddSamlFoundation_ShouldIgnoreDisabledMaterialsAndDefaultBothRolesOff()
    {
        var services = new ServiceCollection();
        services.AddSamlFoundation(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Saml:Idp:SigningCertificate:PfxPath"] = "missing-file",
            ["Saml:Sp:SigningCertificate:PfxPath"] = "missing-file"
        }).Build());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SamlFederationRuntime>().Options;
        Assert.False(options.Idp.Enabled);
        Assert.False(options.Sp.Enabled);
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(ISamlStateStore));
    }

    [Theory]
    [InlineData("Saml:Sp:IdentityProvider:SingleSignOnUrl", "http://sample-idp.example/sso")]
    [InlineData("Saml:Sp:AssertionConsumerServiceUrl", "https://hybrid.example/wrong-route")]
    [InlineData("Saml:Sp:IdentityProvider:EntityId", "relative")]
    [InlineData("Saml:Sp:SigningCertificate:PfxPath", "relative.pfx")]
    public void AddSamlFoundation_ShouldFailEnabledInvalidConfiguration(string key, string value)
    {
        using var certificates = new CertificateFixture();
        var values = certificates.Values(false, true);
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSamlFoundation(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public void Runtime_ShouldRejectExpiredSigningCertificate()
    {
        using var certificates = new CertificateFixture(expired: true);
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSamlFoundation(certificates.Configuration(true, false)));
    }

    private sealed class CertificateFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"saml-foundation-{Guid.NewGuid():N}");

        public CertificateFixture(bool expired = false)
        {
            Directory.CreateDirectory(_directory);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=SAML-Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2),
                DateTimeOffset.UtcNow.AddDays(expired ? -1 : 2));
            File.WriteAllBytes(Path.Combine(_directory, "signing.pfx"), certificate.Export(X509ContentType.Pfx));
            File.WriteAllBytes(Path.Combine(_directory, "public.cer"), certificate.Export(X509ContentType.Cert));
        }

        public IConfiguration Configuration(bool idp, bool sp) => new ConfigurationBuilder().AddInMemoryCollection(Values(idp, sp)).Build();

        public Dictionary<string, string?> Values(bool idp, bool sp) => new()
        {
            ["Saml:Idp:Enabled"] = idp.ToString(),
            ["Saml:Idp:EntityId"] = "https://hybrid.example/idp",
            ["Saml:Idp:SingleSignOnUrl"] = "https://hybrid.example/saml/idp/sso",
            ["Saml:Idp:SigningCertificate:PfxPath"] = Path.Combine(_directory, "signing.pfx"),
            ["Saml:Idp:ServiceProviders:0:EntityId"] = "https://sample-sp.example/entity",
            ["Saml:Idp:ServiceProviders:0:AssertionConsumerServiceUrl"] = "https://sample-sp.example/acs",
            ["Saml:Idp:ServiceProviders:0:SigningCertificatePaths:0"] = Path.Combine(_directory, "public.cer"),
            ["Saml:Sp:Enabled"] = sp.ToString(),
            ["Saml:Sp:EntityId"] = "https://hybrid.example/sp",
            ["Saml:Sp:AssertionConsumerServiceUrl"] = "https://hybrid.example/saml/sp/acs",
            ["Saml:Sp:SigningCertificate:PfxPath"] = Path.Combine(_directory, "signing.pfx"),
            ["Saml:Sp:IdentityProvider:EntityId"] = "https://sample-idp.example/entity",
            ["Saml:Sp:IdentityProvider:SingleSignOnUrl"] = "https://sample-idp.example/sso",
            ["Saml:Sp:IdentityProvider:SigningCertificatePaths:0"] = Path.Combine(_directory, "public.cer")
        };

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
