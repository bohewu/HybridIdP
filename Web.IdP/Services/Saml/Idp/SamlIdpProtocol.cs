using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Core.Application.Interfaces;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.MvcCore;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace Web.IdP.Services.Saml.Idp;

public sealed record SamlIdpRequest(string Partner, string RequestId, string? RelayState,
    DateTimeOffset ExpiresAt, bool ForceAuthentication);

/// <summary>The deliberately small signed Redirect / signed POST federation profile.</summary>
public sealed class SamlIdpProtocol(SamlFederationRuntime runtime, TimeProvider clock)
{
    private static readonly XNamespace Protocol = "urn:oasis:names:tc:SAML:2.0:protocol";
    private static readonly XNamespace Assertion = "urn:oasis:names:tc:SAML:2.0:assertion";
    public const int MaximumXmlBytes = 32768;

    public SamlIdpRequest ReadRequest(HttpRequest http)
    {
        if (!runtime.Options.Idp.Enabled || !http.IsHttps || !HttpMethods.IsGet(http.Method) ||
            http.QueryString.Value?.Length > 16384) throw InvalidRequest();
        var allowedKeys = new[] { "SAMLRequest", "RelayState", "SigAlg", "Signature" };
        if (http.Query.Any(pair => !allowedKeys.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Count != 1) ||
            http.Query["SigAlg"] != Saml2SecurityAlgorithms.RsaSha256Signature ||
            string.IsNullOrEmpty(http.Query["Signature"]) || string.IsNullOrEmpty(http.Query["SAMLRequest"]))
            throw InvalidRequest();
        var relay = http.Query.TryGetValue("RelayState", out var value) ? value.ToString() : null;
        if (relay != null && (Encoding.UTF8.GetByteCount(relay) > 80 || relay.Any(char.IsControl))) throw InvalidRequest();

        // Bound decompression before invoking the protocol library. DTDs and resolvers are prohibited.
        using var compressed = new MemoryStream(Convert.FromBase64String(http.Query["SAMLRequest"].ToString()));
        using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
        using var bounded = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = inflater.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (bounded.Length + read > MaximumXmlBytes) throw InvalidRequest();
            bounded.Write(buffer, 0, read);
        }
        bounded.Position = 0;
        using var reader = XmlReader.Create(bounded, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXmlBytes
        });
        var root = XDocument.Load(reader).Root ?? throw InvalidRequest();
        var issuers = root.Elements(Assertion + "Issuer").ToArray();
        if (root.Name != Protocol + "AuthnRequest" || (string?)root.Attribute("Version") != "2.0" ||
            issuers.Length != 1 || issuers[0].HasElements) throw InvalidRequest();
        var issuer = issuers[0].Value;
        var partner = runtime.Options.Idp.ServiceProviders.SingleOrDefault(p => p.EntityId == issuer)
            ?? throw InvalidRequest();
        var config = runtime.CreateIdpConfiguration(partner.EntityId);
        // Query signature is validated exclusively with deployment-pinned certificates.
        var request = new Saml2AuthnRequest(config);
        new Saml2RedirectBinding().Unbind(http.ToGenericHttpRequest(), request);

        var id = (string?)root.Attribute("ID");
        if (string.IsNullOrEmpty(id) || id.Length > 256 || issuer.Length + id.Length + 12 > 2048 ||
            XmlConvert.VerifyNCName(id) != id || request.Id.Value != id || request.Issuer != issuer ||
            (string?)root.Attribute("Destination") != runtime.Options.Idp.SingleSignOnUrl ||
            (string?)root.Attribute("AssertionConsumerServiceURL") != partner.AssertionConsumerServiceUrl ||
            (string?)root.Attribute("ProtocolBinding") != ProtocolBindings.HttpPost.OriginalString ||
            root.Attribute("AssertionConsumerServiceIndex") != null || root.Attribute("AttributeConsumingServiceIndex") != null ||
            root.Elements().Any(e => e.Name != Assertion + "Issuer" && e.Name != Protocol + "NameIDPolicy"))
            throw InvalidRequest();
        var policies = root.Elements(Protocol + "NameIDPolicy").ToArray();
        if (policies.Length > 1 || policies.Any(p => p.HasElements ||
            (string?)p.Attribute("Format") != SamlProfile.PersistentNameIdFormat ||
            p.Attribute("SPNameQualifier") is { } qualifier && qualifier.Value != issuer)) throw InvalidRequest();
        var passive = root.Attribute("IsPassive");
        if (passive != null && XmlConvert.ToBoolean(passive.Value)) throw InvalidRequest();
        var force = root.Attribute("ForceAuthn");
        var instant = XmlConvert.ToDateTimeOffset((string?)root.Attribute("IssueInstant") ?? throw InvalidRequest());
        var now = clock.GetUtcNow();
        if (instant > now + SamlProfile.ClockSkew || instant + SamlProfile.MessageLifetime + SamlProfile.ClockSkew <= now)
            throw InvalidRequest();
        return new(issuer, id, relay, instant + SamlProfile.MessageLifetime + SamlProfile.ClockSkew,
            force != null && XmlConvert.ToBoolean(force.Value));
    }

    public string CreateResponse(SamlIdpRequest request, Guid userId, DateTimeOffset authenticatedAt,
        IReadOnlyList<SamlAttributeValueDto>? attributes = null)
    {
        if (request.ExpiresAt <= clock.GetUtcNow() || userId == Guid.Empty) throw InvalidRequest();
        var config = runtime.CreateIdpConfiguration(request.Partner);
        var partner = runtime.Options.Idp.ServiceProviders.Single(p => p.EntityId == request.Partner);
        var subject = userId.ToString("N");
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(SamlAttributePolicy.SubjectName, subject) };
        if (attributes is not null)
        {
            if (attributes.Count > 17) throw InvalidRequest();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attribute in attributes)
            {
                if (!names.Add(attribute.Name)) throw InvalidRequest();
                if (attribute.Name == SamlAttributePolicy.SubjectName)
                {
                    if (attribute.Value != subject) throw InvalidRequest();
                    continue;
                }
                SamlAttributePolicy.ValidateName(attribute.Name);
                SamlAttributePolicy.ValidateValue(attribute.Value);
                claims.Add(new(attribute.Name, attribute.Value));
            }
        }
        var response = new Saml2AuthnResponse(config)
        {
            InResponseTo = new Saml2Id(request.RequestId), Status = Saml2StatusCodes.Success,
            Destination = new Uri(partner.AssertionConsumerServiceUrl),
            NameId = new Saml2NameIdentifier(subject, new Uri(SamlProfile.PersistentNameIdFormat))
            {
                NameQualifier = runtime.Options.Idp.EntityId, SPNameQualifier = request.Partner
            },
            SessionIndex = Guid.NewGuid().ToString("N"),
            // Only the shared local-source resolver may add explicitly selected profile attributes.
            ClaimsIdentity = new ClaimsIdentity(claims)
        };
        var token = response.CreateSecurityToken(request.Partner,
            authnContext: new Uri("urn:oasis:names:tc:SAML:2.0:ac:classes:unspecified"),
            subjectConfirmationLifetime: 5, issuedTokenLifetime: 5);
        foreach (var statement in token.Assertion.Statements.OfType<Saml2AuthenticationStatement>())
            statement.AuthenticationInstant = authenticatedAt.UtcDateTime;
        var binding = new Saml2PostBinding();
        binding.Bind(response);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(binding.XmlDocument.OuterXml));
    }

    public string Metadata()
    {
        if (!runtime.Options.Idp.Enabled) throw InvalidRequest();
        XNamespace md = "urn:oasis:names:tc:SAML:2.0:metadata";
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
        return new XElement(md + "EntityDescriptor", new XAttribute("entityID", runtime.Options.Idp.EntityId),
            new XElement(md + "IDPSSODescriptor", new XAttribute("protocolSupportEnumeration", Protocol.NamespaceName),
                new XAttribute("WantAuthnRequestsSigned", "true"),
                new XElement(md + "KeyDescriptor", new XAttribute("use", "signing"),
                    new XElement(ds + "KeyInfo", new XElement(ds + "X509Data", new XElement(ds + "X509Certificate",
                        Convert.ToBase64String(runtime.GetIdpSigningCertificate().Export(X509ContentType.Cert)))))),
                new XElement(md + "NameIDFormat", SamlProfile.PersistentNameIdFormat),
                new XElement(md + "SingleSignOnService", new XAttribute("Binding", ProtocolBindings.HttpRedirect.OriginalString),
                    new XAttribute("Location", runtime.Options.Idp.SingleSignOnUrl)))).ToString(SaveOptions.DisableFormatting);
    }

    private static InvalidOperationException InvalidRequest() => new("Invalid SAML request.");
}
