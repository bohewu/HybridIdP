using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Core.Application.Interfaces;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Web.IdP.Services.Saml.Sp;

public sealed class SamlSpService(SamlFederationRuntime runtime, ISamlStateStore stateStore,
    IDataProtectionProvider protectionProvider, TimeProvider timeProvider, SamlSpResponseValidator validator)
{
    private readonly IDataProtector _browserProtector = protectionProvider.CreateProtector("Saml.Sp.Browser.v1");
    private const string CookiePrefix = "__Secure-HybridSamlSp.";

    public async Task<string> ChallengeAsync(HttpContext context, AuthenticationProperties properties,
        CancellationToken cancellationToken = default)
    {
        var configuration = runtime.CreateSpConfiguration();
        if (!context.Request.IsHttps || !IsCallback(properties.RedirectUri) ||
            !properties.Items.TryGetValue("LoginProvider", out var provider) || provider != SamlProfile.AuthenticationScheme)
            throw new AuthenticationException("SAML challenge rejected.");
        var request = new Saml2AuthnRequest(configuration)
        {
            AssertionConsumerServiceUrl = new Uri(runtime.Options.Sp.AssertionConsumerServiceUrl),
            ProtocolBinding = new Uri("urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"),
            NameIdPolicy = new NameIdPolicy { Format = SamlProfile.PersistentNameIdFormat, AllowCreate = true }
        };
        var relay = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var browser = new BrowserCorrelation(relay, request.IdAsString,
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)));
        var expires = timeProvider.GetUtcNow() + SamlProfile.MessageLifetime;
        var correlation = new Correlation(browser, runtime.Options.Sp.IdentityProvider.EntityId,
            Convert.ToBase64String(PropertiesSerializer.Default.Serialize(properties)));
        if (!await stateStore.TryAddAsync(SamlStatePurpose.SpCorrelation, relay,
                JsonSerializer.Serialize(correlation), expires, cancellationToken))
            throw new AuthenticationException("SAML challenge rejected.");
        context.Response.Cookies.Append(CookiePrefix + relay, _browserProtector.Protect(JsonSerializer.Serialize(browser)),
            CookieOptions(expires));
        var binding = new Saml2RedirectBinding { RelayState = relay };
        binding.Bind(request);
        return binding.RedirectLocation.OriginalString;
    }

    public async Task<string> CompleteAsync(HttpContext context, string encodedResponse, string relay,
        CancellationToken cancellationToken = default)
    {
        if (!context.Request.IsHttps || relay.Length != 43 || relay.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new AuthenticationException("SAML response rejected.");
        if (!context.Request.Cookies.TryGetValue(CookiePrefix + relay, out var protectedBrowser))
            throw new AuthenticationException("SAML response rejected.");
        var browser = JsonSerializer.Deserialize<BrowserCorrelation>(_browserProtector.Unprotect(protectedBrowser));
        if (browser is null || browser.Relay != relay) throw new AuthenticationException("SAML response rejected.");
        var proof = validator.Validate(encodedResponse);
        if (proof.RequestId != browser.RequestId) throw new AuthenticationException("SAML response rejected.");

        // Consume only after cryptographic trust and browser correlation, and before creating any ticket.
        var payload = await stateStore.TryTakeAsync(SamlStatePurpose.SpCorrelation, relay, cancellationToken);
        var correlation = payload is null ? null : JsonSerializer.Deserialize<Correlation>(payload);
        if (correlation is null || correlation.Browser != browser || correlation.Issuer != proof.Issuer)
            throw new AuthenticationException("SAML response rejected.");
        context.Response.Cookies.Delete(CookiePrefix + relay, CookieOptions(null));
        var properties = PropertiesSerializer.Default.Deserialize(Convert.FromBase64String(correlation.Properties));
        if (properties is null || !IsCallback(properties.RedirectUri) ||
            !properties.Items.TryGetValue("LoginProvider", out var provider) || provider != SamlProfile.AuthenticationScheme)
            throw new AuthenticationException("SAML response rejected.");

        var expires = timeProvider.GetUtcNow() + SamlProfile.MaximumStateLifetime;
        if (!await stateStore.TryAddAsync(SamlStatePurpose.SpResponseReplay, ReplayKey(proof.Issuer, proof.ResponseId), "", expires, cancellationToken) ||
            !await stateStore.TryAddAsync(SamlStatePurpose.SpAssertionReplay, ReplayKey(proof.Issuer, proof.AssertionId), "", expires, cancellationToken))
            throw new AuthenticationException("SAML response rejected.");

        properties.IsPersistent = false;
        properties.AllowRefresh = false;
        properties.IssuedUtc = timeProvider.GetUtcNow();
        properties.ExpiresUtc = properties.IssuedUtc + SamlProfile.MessageLifetime;
        // The existing callback/coordinator alone owns local identity linking, MFA and application sign-in.
        await context.SignInAsync(IdentityConstants.ExternalScheme, proof.Principal, properties);
        return properties.RedirectUri!;
    }

    public string Metadata()
    {
        _ = runtime.CreateSpConfiguration(); // Revalidate certificate lifetime before publishing.
        XNamespace metadata = "urn:oasis:names:tc:SAML:2.0:metadata";
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        return new XElement(metadata + "EntityDescriptor", new XAttribute("entityID", runtime.Options.Sp.EntityId),
            new XElement(metadata + "SPSSODescriptor", new XAttribute("protocolSupportEnumeration", "urn:oasis:names:tc:SAML:2.0:protocol"),
                new XAttribute("AuthnRequestsSigned", "true"), new XAttribute("WantAssertionsSigned", "true"),
                new XElement(metadata + "KeyDescriptor", new XAttribute("use", "signing"),
                    new XElement(signature + "KeyInfo", new XElement(signature + "X509Data",
                        new XElement(signature + "X509Certificate", Convert.ToBase64String(runtime.GetSpSigningCertificate().RawData))))),
                new XElement(metadata + "NameIDFormat", SamlProfile.PersistentNameIdFormat),
                new XElement(metadata + "AssertionConsumerService", new XAttribute("Binding", "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"),
                    new XAttribute("Location", runtime.Options.Sp.AssertionConsumerServiceUrl), new XAttribute("index", "0"),
                    new XAttribute("isDefault", "true")))).ToString(SaveOptions.DisableFormatting);
    }

    private static string ReplayKey(string issuer, string id) => issuer.Length.ToString(CultureInfo.InvariantCulture) + ":" + issuer + id;

    private static bool IsCallback(string? uri) => uri is not null && uri.Split('?')[0] is
        "/Account/ExternalLoginCallback" or "/Account/LinkExternalLogin/Callback";

    private static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.None, IsEssential = true,
        Path = SamlProfile.SpAssertionConsumerServicePath, Expires = expires
    };

    private sealed record BrowserCorrelation(string Relay, string RequestId, string Nonce);
    private sealed record Correlation(BrowserCorrelation Browser, string Issuer, string Properties);
}
