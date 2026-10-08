using Core.Application.DTOs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Options;

public sealed class ProviderProfileOptions
{
    public const string Section = "ProviderProfile";
    public bool Enabled { get; set; }
    public TimeSpan MaximumAge { get; set; } = TimeSpan.FromMinutes(5);
    public Dictionary<string, ProviderProfileSourceOptions> Sources { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ProviderProfileSourceOptions
{
    public string ProviderNamespace { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string SharedSecret { get; set; } = string.Empty;
    public bool AllowPrivateNetworkHttp { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    // Only String and Boolean are accepted; names and types are deployment policy.
    public Dictionary<string, string> AllowedProperties { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ProviderProfileOptionsValidator : IValidateOptions<ProviderProfileOptions>
{
    public ValidateOptionsResult Validate(string? name, ProviderProfileOptions options)
    {
        if (options.MaximumAge < TimeSpan.Zero || options.MaximumAge > TimeSpan.FromDays(1) ||
            options.Sources.Count > 8)
            return ValidateOptionsResult.Fail("Profile maximum age must be zero to one day; at most eight sources are allowed.");

        foreach (var (key, source) in options.Sources)
        {
            if (!ProviderProfileContract.IsValidKey(key) || string.IsNullOrWhiteSpace(source.ProviderNamespace) ||
                source.ProviderNamespace.Length > 200 || source.Timeout <= TimeSpan.Zero ||
                source.Timeout > TimeSpan.FromSeconds(30) || source.AllowedProperties.Count > 32 ||
                source.AllowedProperties.Any(p => !ProviderProfileContract.IsValidKey(p.Key) ||
                    p.Value is not ("String" or "Boolean")))
                return ValidateOptionsResult.Fail("Invalid profile source, timeout or approved property schema.");

            if (options.Enabled && (!Uri.TryCreate(source.Endpoint, UriKind.Absolute, out var endpoint) ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment) ||
                (endpoint.Scheme != Uri.UriSchemeHttps &&
                 !(source.AllowPrivateNetworkHttp && endpoint.Scheme == Uri.UriSchemeHttp)) ||
                string.IsNullOrWhiteSpace(source.SharedSecret)))
                return ValidateOptionsResult.Fail("Enabled profile sources require a protected endpoint and service secret.");
        }
        return ValidateOptionsResult.Success;
    }
}
