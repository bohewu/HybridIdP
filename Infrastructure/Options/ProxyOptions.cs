namespace Infrastructure.Options;

public class ProxyOptions
{
    public const string Section = "Proxy";

    public bool Enabled { get; set; }
    
    /// <summary>
    /// Semicolon separated list of IP addresses of known proxies.
    /// </summary>
    public string KnownProxies { get; set; } = string.Empty;

    /// <summary>
    /// Semicolon separated list of CIDR networks of known proxies (e.g., "10.0.0.0/8;172.16.0.0/12").
    /// </summary>
    public string KnownNetworks { get; set; } = string.Empty;
    
    public int ForwardLimit { get; set; } = 1;

    public bool RequireHeaderSymmetry { get; set; }
}
