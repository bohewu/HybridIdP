namespace Infrastructure.Options;

/// <summary>Deployment-only ceilings. Administration can select these sources, never enable or trust them.</summary>
public sealed class NeutralQuerySourceOptions
{
    public bool Enabled { get; set; }
    public bool Trusted { get; set; }
    public bool Paused { get; set; }
    public string ProviderNamespace { get; set; } = string.Empty;
    public string PolicyRevision { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string SharedSecret { get; set; } = string.Empty;
    public bool AllowPrivateNetworkHttp { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public string? QueryRule { get; set; }
    public string[][] AcceptedAttributeSets { get; set; } = [];
    public Dictionary<string, string[]> AllowedValueSources { get; set; } = new(StringComparer.Ordinal);
    // Explicit equivalence approval; never assume one authority's identifiers mean the same elsewhere.
    public string[] TrustedValueProviderNamespaces { get; set; } = [];
}
