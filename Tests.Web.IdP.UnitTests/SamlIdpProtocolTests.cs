using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Core.Application.Interfaces;
using Core.Application.Options;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens.Saml2;
using Web.IdP.Services.Saml;
using Web.IdP.Services.Saml.Idp;

namespace Tests.Web.IdP.UnitTests;

public sealed class SamlIdpProtocolTests
{
    [Fact]
    public void ReadRequest_ShouldAcceptOnlyTrustedSignedRedirect()
    {
        using var fixture = new SamlIdpFixture();
        var request = fixture.Protocol.ReadRequest(fixture.Request());
        Assert.Equal(SamlIdpFixture.Partner, request.Partner);
        Assert.Equal("relay", request.RelayState);
        Assert.False(request.ForceAuthentication);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("acs")]
    [InlineData("destination")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("unsigned")]
    [InlineData("wrong-key")]
    [InlineData("weak-algorithm")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    [InlineData("name-format")]
    public void ReadRequest_ShouldRejectTrustAndProfileViolations(string condition)
    {
        using var fixture = new SamlIdpFixture();
        Assert.ThrowsAny<Exception>(() => fixture.Protocol.ReadRequest(fixture.Request(condition)));
    }

    [Fact]
    public void CreateResponse_ShouldSignBothEnvelopesAndDiscloseOnlyOpaquePersistentSubject()
    {
        using var fixture = new SamlIdpFixture();
        var request = fixture.Protocol.ReadRequest(fixture.Request());
        var userId = Guid.NewGuid();
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(fixture.Protocol.CreateResponse(request, userId, DateTimeOffset.UtcNow.AddMinutes(-2))));
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.LoadXml(xml);
        var signatures = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().ToArray();
        Assert.Equal(2, signatures.Length);
        foreach (var signature in signatures)
        {
            var verifier = new SignedXml((XmlElement)signature.ParentNode!);
            verifier.LoadXml(signature);
            Assert.Equal(SignedXml.XmlDsigRSASHA256Url, verifier.SignedInfo!.SignatureMethod);
            Assert.All(verifier.SignedInfo.References.Cast<Reference>(), reference => Assert.Equal(SignedXml.XmlDsigSHA256Url, reference.DigestMethod));
            Assert.True(verifier.CheckSignature(fixture.Certificate, true));
        }
        XNamespace assertion = "urn:oasis:names:tc:SAML:2.0:assertion";
        var parsed = XDocument.Parse(xml);
        var name = parsed.Descendants(assertion + "NameID").Single();
        Assert.Equal(userId.ToString("N"), name.Value);
        Assert.Equal(SamlProfile.PersistentNameIdFormat, (string?)name.Attribute("Format"));
        Assert.Equal(SamlIdpFixture.Issuer, (string?)name.Attribute("NameQualifier"));
        Assert.Equal(SamlIdpFixture.Partner, (string?)name.Attribute("SPNameQualifier"));
        Assert.Equal(request.RequestId, (string?)parsed.Root!.Attribute("InResponseTo"));
        Assert.Equal(SamlIdpFixture.Acs, (string?)parsed.Root.Attribute("Destination"));
        Assert.Equal(SamlIdpFixture.Partner, parsed.Descendants(assertion + "Audience").Single().Value);
        var attribute = Assert.Single(parsed.Descendants(assertion + "Attribute"));
        Assert.Equal("urn:hybrididp:claims:subject", (string?)attribute.Attribute("Name"));
        Assert.Equal(userId.ToString("N"), attribute.Element(assertion + "AttributeValue")!.Value);
        var conditions = parsed.Descendants(assertion + "Conditions").Single();
        Assert.True(XmlConvert.ToDateTimeOffset(conditions.Attribute("NotOnOrAfter")!.Value) -
            XmlConvert.ToDateTimeOffset(conditions.Attribute("NotBefore")!.Value) <= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Metadata_ShouldAdvertiseOnlySigningPersistentNameIdAndRedirectSso()
    {
        using var fixture = new SamlIdpFixture();
        var metadata = fixture.Protocol.Metadata();
        Assert.Contains("WantAuthnRequestsSigned=\"true\"", metadata);
        Assert.Contains(SamlProfile.PersistentNameIdFormat, metadata);
        Assert.DoesNotContain("SingleLogout", metadata);
        Assert.DoesNotContain("encryption", metadata);
    }
}

internal sealed class SamlIdpFixture : IDisposable
{
    public const string Issuer = "https://hybrid.example/idp";
    public const string Sso = "https://hybrid.example/saml/idp/sso";
    public const string Partner = "https://partner.example/sp";
    public const string Acs = "https://partner.example/acs";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "saml-idp-" + Guid.NewGuid().ToString("N"));
    public X509Certificate2 Certificate { get; }
    public SamlFederationRuntime Runtime { get; }
    public SamlIdpProtocol Protocol { get; }

    public SamlIdpFixture()
    {
        Directory.CreateDirectory(_directory);
        using var key = RSA.Create(2048);
        Certificate = new CertificateRequest("CN=SAML-IdP-Fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = Path.Combine(_directory, "key.pfx");
        var cert = Path.Combine(_directory, "public.cer");
        File.WriteAllBytes(pfx, Certificate.Export(X509ContentType.Pfx));
        File.WriteAllBytes(cert, Certificate.Export(X509ContentType.Cert));
        Runtime = new(new SamlFederationOptions
        {
            Idp = new()
            {
                Enabled = true, EntityId = Issuer, SingleSignOnUrl = Sso, SigningCertificate = new() { PfxPath = pfx },
                ServiceProviders = [new() { EntityId = Partner, AssertionConsumerServiceUrl = Acs, SigningCertificatePaths = [cert] }]
            }
        });
        Protocol = new(Runtime, TimeProvider.System);
    }

    public HttpRequest Request(string? condition = null)
    {
        using var wrongKey = RSA.Create(2048);
        using var wrongCert = new CertificateRequest("CN=Wrong", wrongKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var config = new Saml2Configuration
        {
            Issuer = condition == "issuer" ? "https://untrusted.example" : Partner,
            SigningCertificate = condition == "wrong-key" ? wrongCert : Certificate,
            SignAuthnRequest = condition != "unsigned", SignatureAlgorithm = Saml2SecurityAlgorithms.RsaSha256Signature
        };
        var request = new Saml2AuthnRequest(config)
        {
            Destination = new Uri(condition == "destination" ? "https://wrong.example/sso" : Sso),
            AssertionConsumerServiceUrl = new Uri(condition == "acs" ? "https://wrong.example/acs" : Acs),
            ProtocolBinding = ProtocolBindings.HttpPost,
            NameIdPolicy = new NameIdPolicy { Format = condition == "name-format" ? NameIdentifierFormats.Email.OriginalString : SamlProfile.PersistentNameIdFormat },
            IssueInstant = condition == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-7) :
                condition == "future" ? DateTimeOffset.UtcNow.AddMinutes(2) : DateTimeOffset.UtcNow
        };
        var binding = new Saml2RedirectBinding { RelayState = "relay" };
        binding.Bind(request);
        var query = binding.RedirectLocation.Query;
        if (condition == "weak-algorithm") query = query.Replace(Uri.EscapeDataString(Saml2SecurityAlgorithms.RsaSha256Signature), Uri.EscapeDataString(SignedXml.XmlDsigRSASHA1Url));
        if (condition == "duplicate") query += "&SAMLRequest=duplicate";
        if (condition == "oversized")
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                deflate.Write(Encoding.UTF8.GetBytes(new string('x', SamlIdpProtocol.MaximumXmlBytes + 1)));
            query = "?SAMLRequest=" + Uri.EscapeDataString(Convert.ToBase64String(output.ToArray())) +
                "&SigAlg=" + Uri.EscapeDataString(Saml2SecurityAlgorithms.RsaSha256Signature) + "&Signature=AAAA";
        }
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("hybrid.example");
        http.Request.Path = SamlProfile.IdpSingleSignOnPath;
        http.Request.Method = "GET";
        http.Request.QueryString = new QueryString(query);
        return http.Request;
    }

    public void Dispose()
    {
        Runtime.Dispose();
        Certificate.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
