using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class ProviderMetadataRefreshOptions
{
    public const string Section = "ProviderMetadataRefresh";

    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? SharedSecret { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool AllowPrivateNetworkHttp { get; set; }
}

public sealed class ProviderMetadataRefreshOptionsValidator : IValidateOptions<ProviderMetadataRefreshOptions>
{
    public ValidateOptionsResult Validate(string? name, ProviderMetadataRefreshOptions options)
    {
        var failures = new List<string>();

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(30))
        {
            failures.Add("Provider metadata timeout must be between zero and thirty seconds.");
        }

        if (options.Enabled)
        {
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttps &&
                 !(options.AllowPrivateNetworkHttp && endpoint.Scheme == Uri.UriSchemeHttp)))
            {
                failures.Add("Enabled provider metadata refresh requires a protected endpoint.");
            }

            if (string.IsNullOrWhiteSpace(options.SharedSecret))
            {
                failures.Add("Enabled provider metadata refresh requires a shared secret.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
