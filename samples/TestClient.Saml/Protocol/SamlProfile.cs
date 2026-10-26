namespace TestClient.Saml.Protocol;

public static class SamlProfile
{
    public const string AuthenticationScheme = "Saml";
    public const string PersistentNameIdFormat = "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent";
    public const string IdpMetadataPath = "/saml/idp/metadata";
    public const string IdpSingleSignOnPath = "/saml/idp/sso";
    public const string IdpContinuePath = "/saml/idp/continue";
    public const string SpMetadataPath = "/saml/sp/metadata";
    public const string SpAssertionConsumerServicePath = "/saml/sp/acs";
    public static readonly TimeSpan MaximumStateLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MessageLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);
}

