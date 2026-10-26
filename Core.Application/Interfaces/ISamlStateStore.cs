namespace Core.Application.Interfaces;

public enum SamlStatePurpose
{
    IdpRequestReplay,
    IdpContinuation,
    SpCorrelation,
    SpResponseReplay,
    SpAssertionReplay
}

/// <summary>
/// Durable cross-instance one-use state. Database failures throw (deny the flow).
/// Replay markers use an empty payload and must never be consumed by callers.
/// Keys must bind the partner and message ID; continuation payloads must bind the browser.
/// </summary>
public interface ISamlStateStore
{
    Task<bool> TryAddAsync(SamlStatePurpose purpose, string key, string payload,
        DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Returns the protected record's plaintext exactly once, or null on absence/expiry.</summary>
    Task<string?> TryTakeAsync(SamlStatePurpose purpose, string key,
        CancellationToken cancellationToken = default);
}

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
