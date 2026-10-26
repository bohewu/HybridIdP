namespace Core.Application.Options;

/// <summary>Deployment-owned trust; neither role is enabled implicitly.</summary>
public sealed class SamlFederationOptions
{
    public const string Section = "Saml";
    public SamlIdpOptions Idp { get; set; } = new();
    public SamlSpOptions Sp { get; set; } = new();
}

public sealed class SamlSigningCertificateOptions
{
    public string PfxPath { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public sealed class SamlIdpOptions
{
    public bool Enabled { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string SingleSignOnUrl { get; set; } = string.Empty;
    public SamlSigningCertificateOptions SigningCertificate { get; set; } = new();
    public List<SamlServiceProviderOptions> ServiceProviders { get; set; } = [];
}

public sealed class SamlServiceProviderOptions
{
    public string EntityId { get; set; } = string.Empty;
    public string AssertionConsumerServiceUrl { get; set; } = string.Empty;
    public List<string> SigningCertificatePaths { get; set; } = [];
}

public sealed class SamlSpOptions
{
    public bool Enabled { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string AssertionConsumerServiceUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "SAML";
    public SamlSigningCertificateOptions SigningCertificate { get; set; } = new();
    public SamlIdentityProviderOptions IdentityProvider { get; set; } = new();
}

public sealed class SamlIdentityProviderOptions
{
    public string EntityId { get; set; } = string.Empty;
    public string SingleSignOnUrl { get; set; } = string.Empty;
    public List<string> SigningCertificatePaths { get; set; } = [];
}
