using Core.Application.DTOs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class LegacyPasswordSyncOptions
{
    public const string Section = "LegacyPasswordSync";

    public bool Enabled { get; set; }
    public bool CompletedDirectoryRecoveryEnabled { get; set; }
    public bool CompletedDirectoryRequiredChangeEnabled { get; set; }
    public bool Stage2MigrationEnabled { get; set; }
    public string? Endpoint { get; set; }
    public string? SharedSecret { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool AllowPrivateNetworkHttp { get; set; }
    public string? TrustedProviderNamespace { get; set; }
    public string? ActiveMappingVersion { get; set; }
    public List<LegacyPasswordSyncMappingOptions> Mappings { get; set; } = [];

    public bool IsCohortEnabled(LegacyPasswordSyncCohort cohort) => cohort switch
    {
        LegacyPasswordSyncCohort.CompletedDirectoryRecovery => CompletedDirectoryRecoveryEnabled,
        LegacyPasswordSyncCohort.CompletedDirectoryRequiredChange =>
            CompletedDirectoryRequiredChangeEnabled,
        LegacyPasswordSyncCohort.Stage2Migration => Stage2MigrationEnabled,
        _ => false
    };
}

public sealed class LegacyPasswordSyncMappingOptions
{
    public bool Enabled { get; set; }
    public string? ProviderNamespace { get; set; }
    public string? StableSubject { get; set; }
    public Guid AccountIdentity { get; set; }
    public string? MappingVersion { get; set; }
}

public sealed class LegacyPasswordSyncOptionsValidator : IValidateOptions<LegacyPasswordSyncOptions>
{
    public ValidateOptionsResult Validate(string? name, LegacyPasswordSyncOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        var anyCohortEnabled = options.CompletedDirectoryRecoveryEnabled ||
            options.CompletedDirectoryRequiredChangeEnabled || options.Stage2MigrationEnabled;
        var enabledMappings = options.Mappings?.Where(mapping => mapping.Enabled).ToArray() ?? [];

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(30))
        {
            failures.Add("Legacy password sync timeout must be between zero and thirty seconds.");
        }

        if (!anyCohortEnabled)
        {
            failures.Add("Enabled Legacy password sync requires an eligible cohort.");
        }

        if (!IsProtectedEndpoint(options.Endpoint, options.AllowPrivateNetworkHttp))
        {
            failures.Add("Enabled Legacy password sync requires a protected endpoint.");
        }

        if (!IsExactSecret(options.SharedSecret))
        {
            failures.Add("Enabled Legacy password sync requires a protected caller secret.");
        }

        if (!IsExactValue(options.TrustedProviderNamespace))
        {
            failures.Add("Enabled Legacy password sync requires one exact trusted provider namespace.");
        }

        if (!IsExactValue(options.ActiveMappingVersion))
        {
            failures.Add("Enabled Legacy password sync requires an active mapping version.");
        }

        if (enabledMappings.Length == 0)
        {
            failures.Add("Enabled Legacy password sync requires an enabled exact mapping.");
        }

        foreach (var mapping in enabledMappings)
        {
            if (!IsExactValue(mapping.ProviderNamespace) || !IsExactValue(mapping.StableSubject) ||
                !IsExactValue(mapping.MappingVersion) || mapping.AccountIdentity == Guid.Empty)
            {
                failures.Add("Enabled Legacy password sync mappings must contain exact non-empty values.");
                break;
            }

            if (!string.Equals(mapping.ProviderNamespace, options.TrustedProviderNamespace, StringComparison.Ordinal))
            {
                failures.Add("Enabled Legacy password sync mappings must use the trusted provider namespace.");
                break;
            }

            if (!string.Equals(mapping.MappingVersion, options.ActiveMappingVersion, StringComparison.Ordinal))
            {
                failures.Add("Enabled Legacy password sync mappings must use the active mapping version.");
                break;
            }
        }

        if (enabledMappings
            .GroupBy(mapping => (mapping.ProviderNamespace, mapping.StableSubject))
            .Any(group => group.Count() != 1) ||
            enabledMappings.GroupBy(mapping => mapping.AccountIdentity).Any(group => group.Count() != 1))
        {
            failures.Add("Enabled Legacy password sync mappings must use unique source identities and account identities.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsProtectedEndpoint(string? value, bool allowPrivateNetworkHttp) =>
        Uri.TryCreate(value, UriKind.Absolute, out var endpoint) &&
        string.IsNullOrEmpty(endpoint.UserInfo) &&
        string.IsNullOrEmpty(endpoint.Fragment) &&
        !string.IsNullOrWhiteSpace(endpoint.Host) &&
        (endpoint.Scheme == Uri.UriSchemeHttps ||
         (allowPrivateNetworkHttp && endpoint.Scheme == Uri.UriSchemeHttp));

    internal static bool IsExactValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    internal static bool IsExactSecret(string? value) => IsExactValue(value);
}
