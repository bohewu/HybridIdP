using Core.Application.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class ProviderProofOptions
{
    public const string Section = "ProviderProof";

    public bool Enabled { get; set; }
    public string? TrustedProviderNamespace { get; set; }
    // Email confirmation is separate from selecting a credential authority.
    // Empty by default; namespace matching is exact and case-sensitive.
    public string[] TrustedEmailProviderNamespaces { get; set; } = [];

    public string? Endpoint { get; set; }
    public string? SharedSecret { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool AllowPrivateNetworkHttp { get; set; }
}

public sealed class ProviderProofOptionsValidator : IValidateOptions<ProviderProofOptions>
{
    private readonly IOptions<DirectoryIntegrationOptions> _directoryOptions;
    private readonly IOptions<DirectoryNativeProfileOptions>? _directoryNativeOptions;
    private readonly IOptions<NativeFieldPolicyOptions>? _nativeFieldOptions;
    private readonly IOptions<CredentialMigrationOptions>? _migrationOptions;

    public ProviderProofOptionsValidator(IOptions<DirectoryIntegrationOptions> directoryOptions,
        IOptions<DirectoryNativeProfileOptions>? directoryNativeOptions = null,
        IOptions<NativeFieldPolicyOptions>? nativeFieldOptions = null,
        IOptions<CredentialMigrationOptions>? migrationOptions = null)
    {
        _directoryOptions = directoryOptions;
        _directoryNativeOptions = directoryNativeOptions;
        _nativeFieldOptions = nativeFieldOptions;
        _migrationOptions = migrationOptions;
    }

    public ValidateOptionsResult Validate(string? name, ProviderProofOptions options)
    {
        var failures = new List<string>();

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(30))
        {
            failures.Add("Provider proof timeout must be between zero and thirty seconds.");
        }

        var directory = _directoryOptions.Value;
        // Only the explicit data-only deployment may omit Proof transport entirely. A configured
        // or partial transport, or any existing staged credential/native flow, retains its requirements.
        var dataOnlyDirectory = _directoryNativeOptions?.Value is { Enabled: true, Trusted: true } &&
            !options.Enabled && string.IsNullOrWhiteSpace(options.Endpoint) && string.IsNullOrWhiteSpace(options.SharedSecret) &&
            !directory.AuthenticationEnabled && !directory.TemporaryCredentialCapabilityEnabled &&
            _nativeFieldOptions?.Value.Stage1Enabled != true && _migrationOptions?.Value.Enabled != true;
        if (options.Enabled || directory.Enabled && !dataOnlyDirectory)
        {
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttps &&
                 !(options.AllowPrivateNetworkHttp && endpoint.Scheme == Uri.UriSchemeHttp)))
            {
                failures.Add("Enabled directory integration requires a protected provider endpoint.");
            }

            if (string.IsNullOrWhiteSpace(options.SharedSecret))
            {
                failures.Add("Enabled directory integration requires a provider shared secret.");
            }
        }

        if (options.Enabled && (string.IsNullOrWhiteSpace(options.TrustedProviderNamespace) ||
            options.TrustedProviderNamespace.Length > 200 || options.TrustedProviderNamespace == "Legacy"))
            failures.Add("Independent Proof login requires an explicit, non-Legacy provider namespace.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
