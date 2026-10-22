using Core.Application.DTOs;

namespace Infrastructure.Options;

public sealed class ProviderNativeProfileOptions
{
    public Dictionary<string, NeutralQuerySourceOptions> QuerySources { get; set; } = new(StringComparer.Ordinal);

    public const string Section = "ProviderNativeProfile";
    public bool Enabled { get; set; }
    public string? TrustedProviderNamespace { get; set; }
    public string? Endpoint { get; set; }
    public string? SharedSecret { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool AllowPrivateNetworkHttp { get; set; }
    public string[] TrustedEmailProviderNamespaces { get; set; } = [];

    public bool TryGetEndpoint(out Uri endpoint) =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out endpoint!) &&
        (endpoint.Scheme == Uri.UriSchemeHttps || AllowPrivateNetworkHttp && endpoint.Scheme == Uri.UriSchemeHttp) &&
        string.IsNullOrEmpty(endpoint.UserInfo) && string.IsNullOrEmpty(endpoint.Query) && string.IsNullOrEmpty(endpoint.Fragment) &&
        ProviderNativeProfileContract.IsValidIdentity(TrustedProviderNamespace, "configuration") &&
        !string.IsNullOrWhiteSpace(SharedSecret) && !SharedSecret.Any(char.IsControl) &&
        Timeout > TimeSpan.Zero && Timeout <= TimeSpan.FromSeconds(30) && TrustedEmailProviderNamespaces is not null;
}
