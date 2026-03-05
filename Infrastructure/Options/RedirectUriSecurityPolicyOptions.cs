namespace Infrastructure.Options;

public class RedirectUriSecurityPolicyOptions
{
    public const string Section = "RedirectUriSecurityPolicy";

    public bool EnforceHttps { get; set; } = true;

    public bool AllowLocalhostHttp { get; set; } = true;

    public string[] AllowedHosts { get; set; } = [];
}
