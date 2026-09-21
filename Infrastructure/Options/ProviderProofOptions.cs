using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class ProviderProofOptions
{
    public const string Section = "ProviderProof";

    public string? Endpoint { get; set; }
    public string? SharedSecret { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool AllowPrivateNetworkHttp { get; set; }
}

public sealed class ProviderProofOptionsValidator : IValidateOptions<ProviderProofOptions>
{
    private readonly IOptions<DirectoryIntegrationOptions> _directoryOptions;

    public ProviderProofOptionsValidator(IOptions<DirectoryIntegrationOptions> directoryOptions)
    {
        _directoryOptions = directoryOptions;
    }

    public ValidateOptionsResult Validate(string? name, ProviderProofOptions options)
    {
        var failures = new List<string>();

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(30))
        {
            failures.Add("Provider proof timeout must be between zero and thirty seconds.");
        }

        if (_directoryOptions.Value.Enabled)
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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
