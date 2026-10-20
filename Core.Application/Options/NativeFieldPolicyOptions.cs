namespace Core.Application.Options;

/// <summary>Deployment ceiling only. Enabling a field source does not enable authentication or establish identity.</summary>
public sealed class NativeFieldPolicyOptions
{
    public const string Section = "NativeFieldPolicy";
    // Preserve existing explicitly enabled Proof mappings; ProviderProof.Enabled still defaults to false.
    public bool ProofEnabled { get; set; } = true;
    public bool Stage1Enabled { get; set; }
    public string[] TrustedStage1ProviderNamespaces { get; set; } = [];
}
