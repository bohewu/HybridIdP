using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;


using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;

namespace TestClient.Saml.Protocol;

/// <summary>Local, explicitly pinned partner trust. No network metadata or global SAML cookie defaults.</summary>
public sealed class SamlFederationRuntime : IDisposable
{
    private readonly List<X509Certificate2> _certificates = [];
    private readonly Dictionary<string, X509Certificate2[]> _serviceProviderCertificates = new(StringComparer.Ordinal);
    private X509Certificate2? _idpSigningCertificate;
    private X509Certificate2? _spSigningCertificate;
    private X509Certificate2[] _identityProviderCertificates = [];

    public SamlFederationOptions Options { get; }

    public SamlFederationRuntime(SamlFederationOptions options)
    {
        Options = options;
        try
        {
            if (options.Idp.Enabled)
            {
                ValidateEntityId(options.Idp.EntityId);
                ValidateEndpoint(options.Idp.SingleSignOnUrl, SamlProfile.IdpSingleSignOnPath);
                _idpSigningCertificate = LoadSigningCertificate(options.Idp.SigningCertificate);
                if (options.Idp.ServiceProviders.Count == 0)
                    throw new InvalidOperationException("Enabled SAML IdP requires at least one trusted service provider.");
                foreach (var partner in options.Idp.ServiceProviders)
                {
                    ValidateEntityId(partner.EntityId);
                    ValidateEndpoint(partner.AssertionConsumerServiceUrl);
                    if (!_serviceProviderCertificates.TryAdd(partner.EntityId, LoadTrustCertificates(partner.SigningCertificatePaths)))
                        throw new InvalidOperationException("SAML service provider entity IDs must be unique.");
                }
            }
            if (options.Sp.Enabled)
            {
                ValidateEntityId(options.Sp.EntityId);
                ValidateEndpoint(options.Sp.AssertionConsumerServiceUrl, SamlProfile.SpAssertionConsumerServicePath);
                ValidateEntityId(options.Sp.IdentityProvider.EntityId);
                ValidateEndpoint(options.Sp.IdentityProvider.SingleSignOnUrl);
                if (string.IsNullOrWhiteSpace(options.Sp.DisplayName) || options.Sp.DisplayName.Length > 100)
                    throw new InvalidOperationException("SAML provider display name is required and bounded.");
                _spSigningCertificate = LoadSigningCertificate(options.Sp.SigningCertificate);
                _identityProviderCertificates = LoadTrustCertificates(options.Sp.IdentityProvider.SigningCertificatePaths);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // Configurations are per-operation: ITfoxtec objects are mutable and must not be shared between requests.
    public Saml2Configuration CreateIdpConfiguration(string serviceProviderEntityId)
    {
        if (!Options.Idp.Enabled || !_serviceProviderCertificates.TryGetValue(serviceProviderEntityId, out var certificates))
            throw new InvalidOperationException("The SAML service provider is not trusted or IdP is disabled.");
        return CreateConfiguration(Options.Idp.EntityId, serviceProviderEntityId, _idpSigningCertificate!, certificates);
    }

    public Saml2Configuration CreateSpConfiguration()
    {
        if (!Options.Sp.Enabled) throw new InvalidOperationException("SAML SP is disabled.");
        var configuration = CreateConfiguration(Options.Sp.EntityId, Options.Sp.IdentityProvider.EntityId,
            _spSigningCertificate!, _identityProviderCertificates);
        configuration.SingleSignOnDestination = new Uri(Options.Sp.IdentityProvider.SingleSignOnUrl);
        return configuration;
    }

    /// <summary>Metadata publishes only the enabled role's public certificate.</summary>
    public X509Certificate2 GetIdpSigningCertificate() => Options.Idp.Enabled
        ? _idpSigningCertificate! : throw new InvalidOperationException("SAML IdP is disabled.");

    public X509Certificate2 GetSpSigningCertificate() => Options.Sp.Enabled
        ? _spSigningCertificate! : throw new InvalidOperationException("SAML SP is disabled.");

    private static Saml2Configuration CreateConfiguration(string issuer, string allowedIssuer,
        X509Certificate2 signingCertificate, IEnumerable<X509Certificate2> validationCertificates)
    {
        ValidateCertificate(signingCertificate, requirePrivateKey: true);
        var configuration = new Saml2Configuration
        {
            Issuer = issuer,
            AllowedIssuer = allowedIssuer,
            SigningCertificate = signingCertificate,
            SignAuthnRequest = true,
            SignatureAlgorithm = Saml2SecurityAlgorithms.RsaSha256Signature,
            AuthnResponseSignType = Saml2AuthnResponseSignTypes.SignAssertionAndResponse,
            // SAML trust is exact deployment-pinned certificates, not the OS public CA store.
            CertificateValidationMode = X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck,
            AudienceRestricted = true
        };
        configuration.AllowedAudienceUris.Add(issuer);
        configuration.SignatureValidationAlgorithms.Add(Saml2SecurityAlgorithms.RsaSha256Signature);
        foreach (var certificate in validationCertificates)
        {
            ValidateCertificate(certificate, requirePrivateKey: false);
            configuration.SignatureValidationCertificates.Add(certificate);
        }
        return configuration;
    }

    private X509Certificate2 LoadSigningCertificate(SamlSigningCertificateOptions options)
    {
        ValidateCertificatePath(options.PfxPath);
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(options.PfxPath, options.Password,
            X509KeyStorageFlags.EphemeralKeySet);
        _certificates.Add(certificate);
        ValidateCertificate(certificate, requirePrivateKey: true);
        return certificate;
    }

    private X509Certificate2[] LoadTrustCertificates(List<string> paths)
    {
        if (paths.Count is < 1 or > 5)
            throw new InvalidOperationException("SAML partner requires one to five pinned public certificates.");
        return paths.Select(path =>
        {
            ValidateCertificatePath(path);
            var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
            _certificates.Add(certificate);
            ValidateCertificate(certificate, requirePrivateKey: false);
            return certificate;
        }).ToArray();
    }

    private static void ValidateCertificatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("SAML certificate paths must be absolute deployment-owned paths.");
    }

    private static void ValidateCertificate(X509Certificate2 certificate, bool requirePrivateKey)
    {
        var now = DateTime.UtcNow;
        using var key = certificate.GetRSAPublicKey();
        if (now < certificate.NotBefore.ToUniversalTime() || now >= certificate.NotAfter.ToUniversalTime() ||
            key is null || key.KeySize < 2048 || (requirePrivateKey && !certificate.HasPrivateKey))
            throw new InvalidOperationException("SAML requires a currently valid RSA certificate of at least 2048 bits and the local signing key.");
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (usage is not null && !usage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
            throw new InvalidOperationException("SAML certificate must permit digital signatures.");
    }

    private static void ValidateEntityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || !Uri.TryCreate(value, UriKind.Absolute, out _))
            throw new InvalidOperationException("SAML entity IDs must be bounded absolute URIs.");
    }

    private static void ValidateEndpoint(string value, string? requiredPath = null)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (requiredPath is not null && uri.AbsolutePath != requiredPath))
            throw new InvalidOperationException("SAML endpoints require HTTPS, no query/fragment/userinfo, and the declared local route.");
    }

    public void Dispose()
    {
        foreach (var certificate in _certificates) certificate.Dispose();
        _certificates.Clear();
    }
}

