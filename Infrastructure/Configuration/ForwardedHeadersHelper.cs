using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Infrastructure.Configuration;

public static class ForwardedHeadersHelper
{
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
