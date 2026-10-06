using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Infrastructure.Configuration;

public static class ForwardedHeadersHelper
{
    public static bool IsTrustedReadinessAddress(IPAddress? address, Infrastructure.Options.ProxyOptions proxy)
    {
        if (address == null) return false;
        address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (IPAddress.IsLoopback(address)) return true;
        if (!proxy.Enabled || string.IsNullOrWhiteSpace(proxy.KnownProxies)) return false;
        var options = new ForwardedHeadersOptions();
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        ConfigureKnownNetworks(options, proxy.KnownProxies);
        return options.KnownProxies.Contains(address) || options.KnownIPNetworks.Any(network => network.Contains(address));
    }

    public static void ConfigureKnownNetworks(ForwardedHeadersOptions options, string knownProxiesConfig)
    {
        if (string.IsNullOrEmpty(knownProxiesConfig))
            return;

        foreach (var ipOrCidr in knownProxiesConfig.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Check for CIDR notation (e.g., 10.0.0.0/8)
            if (ipOrCidr.Contains('/'))
            {
                var parts = ipOrCidr.Split('/');
                if (parts.Length == 2 && 
                    IPAddress.TryParse(parts[0], out var networkIp) && 
                    int.TryParse(parts[1], out var prefixLength))
                {
                    options.KnownIPNetworks.Add(new System.Net.IPNetwork(networkIp, prefixLength));
                }
            }
            // Check for simple IP
            else if (IPAddress.TryParse(ipOrCidr, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }
    }
}
