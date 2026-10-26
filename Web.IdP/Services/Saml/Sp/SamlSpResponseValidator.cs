using System.Collections.Specialized;
using System.Globalization;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Core.Application.Interfaces;
using Core.Application.Options;
using ITfoxtec.Identity.Saml2;

namespace Web.IdP.Services.Saml.Sp;

/// <summary>The deliberately bounded POST/persistent-NameID profile, in addition to library signature validation.</summary>
public sealed class SamlSpResponseValidator(SamlFederationRuntime runtime, TimeProvider timeProvider)
{
    public const int MaximumEncodedResponseLength = 180000;
    private static readonly XNamespace Assertion = "urn:oasis:names:tc:SAML:2.0:assertion";
    private static readonly XNamespace Protocol = "urn:oasis:names:tc:SAML:2.0:protocol";
    private static readonly XNamespace Signature = SignedXml.XmlDsigNamespaceUrl;

    public SamlSpValidatedResponse Validate(string encodedResponse)
    {
        var configuration = runtime.CreateSpConfiguration();
        var options = runtime.Options.Sp;
        Require(encodedResponse.Length is > 0 and <= MaximumEncodedResponseLength);
        var bytes = Convert.FromBase64String(encodedResponse);
        Require(bytes.Length <= 128 * 1024);
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 128 * 1024, MaxCharactersFromEntities = 0
        });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var response = document.Root ?? throw Denied();
        Require(response.Name == Protocol + "Response");
        var assertion = One(response, Assertion + "Assertion");
        // Reject nested/wrapped/duplicate assertions and identifiers before any signature processing.
        Require(document.Descendants().Count(e => e.Name.LocalName == "Assertion") == 1);
        Require(!document.Descendants().Any(e => e.Name.LocalName == "EncryptedAssertion"));
        var ids = document.Descendants().Attributes().Where(a => a.Name.LocalName is "ID" or "Id" or "id")
            .Select(a => a.Value).ToArray();
        Require(ids.Length == ids.Distinct(StringComparer.Ordinal).Count());
        var responseId = Identifier(response, "ID");
        var assertionId = Identifier(assertion, "ID");
        var requestId = Identifier(response, "InResponseTo");
        Require(Value(response, "Version") == "2.0" && Value(assertion, "Version") == "2.0");
        Require(Value(response, "Destination") == options.AssertionConsumerServiceUrl);
        Require(One(response, Assertion + "Issuer").Value == options.IdentityProvider.EntityId);
        Require(One(assertion, Assertion + "Issuer").Value == options.IdentityProvider.EntityId);
        Require(Value(One(One(response, Protocol + "Status"), Protocol + "StatusCode"), "Value") ==
            "urn:oasis:names:tc:SAML:2.0:status:Success");
        var now = timeProvider.GetUtcNow();
        var responseInstant = Instant(response, "IssueInstant");
        var assertionInstant = Instant(assertion, "IssueInstant");
        ValidateIssueInstant(responseInstant, now);
        ValidateIssueInstant(assertionInstant, now);

        Require(document.Descendants(Signature + "Signature").Count() == 2);
        ValidateSignatureShape(response, responseId);
        ValidateSignatureShape(assertion, assertionId);

        var conditions = One(assertion, Assertion + "Conditions");
        ValidateWindow(conditions, assertionInstant, now, requireNotBefore: true);
        var audience = One(conditions, Assertion + "AudienceRestriction");
        Require(One(audience, Assertion + "Audience").Value == options.EntityId);
        // Other conditions need their own semantics; do not silently ignore them.
        Require(conditions.Elements().All(e => e.Name == Assertion + "AudienceRestriction"));
        var subject = One(assertion, Assertion + "Subject");
        var nameId = One(subject, Assertion + "NameID");
        Require(Value(nameId, "Format") == SamlProfile.PersistentNameIdFormat);
        Require(nameId.Attribute("NameQualifier") is null || Value(nameId, "NameQualifier") == options.IdentityProvider.EntityId);
        Require(nameId.Attribute("SPNameQualifier") is null || Value(nameId, "SPNameQualifier") == options.EntityId);
        Require(nameId.Attribute("SPProvidedID") is null);
        Require(!nameId.HasElements && !string.IsNullOrWhiteSpace(nameId.Value) && nameId.Value.Length <= 1024);
        var confirmation = One(subject, Assertion + "SubjectConfirmation");
        Require(Value(confirmation, "Method") == "urn:oasis:names:tc:SAML:2.0:cm:bearer");
        var data = One(confirmation, Assertion + "SubjectConfirmationData");
        Require(Value(data, "Recipient") == options.AssertionConsumerServiceUrl);
        Require(Value(data, "InResponseTo") == requestId);
        Require(data.Attribute("Address") is null && !data.HasElements);
        ValidateWindow(data, assertionInstant, now, requireNotBefore: false);
        var authentication = One(assertion, Assertion + "AuthnStatement");
        Require(Instant(authentication, "AuthnInstant") <= now + SamlProfile.ClockSkew);
        if (authentication.Attribute("SessionNotOnOrAfter") is not null)
            Require(Instant(authentication, "SessionNotOnOrAfter") > now);

        // The library verifies BOTH present signatures against pinned keys and validates the token.
        // Never create a session from the library's unrestricted claims identity.
        var binding = new Saml2PostBinding();
        binding.Unbind(new ITfoxtec.Identity.Saml2.Http.HttpRequest
        {
            Method = "POST", Form = new NameValueCollection { ["SAMLResponse"] = encodedResponse }
        }, new Saml2AuthnResponse(configuration));

        var identity = new ClaimsIdentity(SamlProfile.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, ProviderKey(options, nameId.Value)));
        // Only presentation fields are allowed. Email has NO binding assurance. Roles/AMR are never imported.
        foreach (var type in new[] { ClaimTypes.Name, ClaimTypes.Email, ClaimTypes.GivenName, ClaimTypes.Surname })
        {
            var attributes = assertion.Elements(Assertion + "AttributeStatement").Elements(Assertion + "Attribute")
                .Where(e => Value(e, "Name") == type).ToArray();
            if (attributes.Length == 0) continue;
            Require(attributes.Length == 1);
            var value = One(attributes[0], Assertion + "AttributeValue");
            Require(!value.HasElements && value.Value.Length <= 256);
            if (!string.IsNullOrWhiteSpace(value.Value)) identity.AddClaim(new Claim(type, value.Value));
        }
        return new SamlSpValidatedResponse(responseId, assertionId, requestId,
            options.IdentityProvider.EntityId, new ClaimsPrincipal(identity));
    }

    private static string ProviderKey(SamlSpOptions options, string nameId)
    {
        // Fixed qualifiers plus length-prefixed issuer and opaque NameID avoid delimiter ambiguity
        // and preserve the existing bounded Identity ProviderKey column. Never use email as the key.
        var tuple = string.Concat(new[] { options.IdentityProvider.EntityId, SamlProfile.PersistentNameIdFormat,
                options.IdentityProvider.EntityId, options.EntityId, nameId }
            .Select(value => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value));
        return "saml-v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tuple)));
    }

    private static void ValidateSignatureShape(XElement signed, string id)
    {
        var signature = One(signed, Signature + "Signature");
        var info = One(signature, Signature + "SignedInfo");
        Require(Value(One(info, Signature + "SignatureMethod"), "Algorithm") == SignedXml.XmlDsigRSASHA256Url);
        Require(Value(One(info, Signature + "CanonicalizationMethod"), "Algorithm") == SignedXml.XmlDsigExcC14NTransformUrl);
        var reference = One(info, Signature + "Reference");
        Require(Value(reference, "URI") == "#" + id);
        Require(Value(One(reference, Signature + "DigestMethod"), "Algorithm") == SignedXml.XmlDsigSHA256Url);
        var transforms = One(reference, Signature + "Transforms").Elements(Signature + "Transform").ToArray();
        Require(transforms.Length == 2 && Value(transforms[0], "Algorithm") == SignedXml.XmlDsigEnvelopedSignatureTransformUrl &&
            Value(transforms[1], "Algorithm") == SignedXml.XmlDsigExcC14NTransformUrl);
        Require(!transforms.Any(e => e.HasElements));
    }

    private static void ValidateIssueInstant(DateTimeOffset instant, DateTimeOffset now) =>
        Require(instant <= now + SamlProfile.ClockSkew && instant > now - SamlProfile.MessageLifetime - SamlProfile.ClockSkew);

    private static void ValidateWindow(XElement element, DateTimeOffset issued, DateTimeOffset now, bool requireNotBefore)
    {
        var until = Instant(element, "NotOnOrAfter");
        Require(until > now && until <= issued + SamlProfile.MessageLifetime + SamlProfile.ClockSkew);
        if (requireNotBefore || element.Attribute("NotBefore") is not null)
        {
            var from = Instant(element, "NotBefore");
            Require(from <= now + SamlProfile.ClockSkew && from < until && until - from <= SamlProfile.MessageLifetime + SamlProfile.ClockSkew);
        }
    }

    private static DateTimeOffset Instant(XElement element, string attribute)
    {
        var text = Value(element, attribute);
        Require(text.EndsWith('Z') && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out _));
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private static string Identifier(XElement element, string attribute)
    {
        var value = Value(element, attribute);
        Require(value.Length is > 0 and <= 256);
        XmlConvert.VerifyNCName(value);
        return value;
    }

    private static XElement One(XElement parent, XName name)
    {
        var values = parent.Elements(name).ToArray();
        Require(values.Length == 1);
        return values[0];
    }

    private static string Value(XElement element, string attribute) => (string?)element.Attribute(attribute) ?? string.Empty;
    private static void Require(bool valid) { if (!valid) throw Denied(); }
    private static AuthenticationException Denied() => new("SAML response rejected.");
}

public sealed record SamlSpValidatedResponse(string ResponseId, string AssertionId, string RequestId,
    string Issuer, ClaimsPrincipal Principal);
