using System.Net;

namespace Web.IdP.Middleware;

/// <summary>
/// Middleware to restrict access to Prometheus metrics endpoint based on IP whitelist
/// </summary>
public class PrometheusIpWhitelistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PrometheusIpWhitelistMiddleware> _logger;
    private readonly HashSet<IPAddress> _allowedIPs;
    private readonly HashSet<string> _allowedNetworks; // CIDR notation

    public PrometheusIpWhitelistMiddleware(
        RequestDelegate next,
        ILogger<PrometheusIpWhitelistMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _allowedIPs = new HashSet<IPAddress>();
        _allowedNetworks = new HashSet<string>();

        var allowedIPs = configuration.GetSection("Observability:AllowedIPs").Get<string[]>() ?? Array.Empty<string>();
        
        foreach (var ip in allowedIPs)
        {
            if (ip.Contains('/'))
            {
                // CIDR notation (e.g., "10.0.0.0/8")
                _allowedNetworks.Add(ip);
            }
            else if (IPAddress.TryParse(ip, out var address))
            {
                _allowedIPs.Add(address);
            }
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only check metrics endpoints
        if (context.Request.Path.StartsWithSegments("/metrics"))
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            
            if (remoteIp == null || !IsIpAllowed(remoteIp))
            {
                _logger.LogWarning(
                    "Blocked access to metrics endpoint from IP: {RemoteIp}", 
                    remoteIp?.ToString() ?? "unknown");
                
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Access denied");
                return;
            }

            _logger.LogDebug("Allowed access to metrics endpoint from IP: {RemoteIp}", remoteIp);
        }

        await _next(context);
    }

    private bool IsIpAllowed(IPAddress remoteIp)
    {
        // Check exact IP match
        if (_allowedIPs.Contains(remoteIp))
        {
            return true;
        }

        // Map IPv4-mapped IPv6 addresses to IPv4
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
            if (_allowedIPs.Contains(remoteIp))
            {
                return true;
            }
        }

        // Check CIDR ranges
        foreach (var network in _allowedNetworks)
        {
            if (IsInNetwork(remoteIp, network))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInNetwork(IPAddress address, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2)
                return false;

            var networkAddress = IPAddress.Parse(parts[0]);
            var prefixLength = int.Parse(parts[1]);

            // Convert to IPv4 if needed
            if (address.IsIPv4MappedToIPv6 && networkAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                address = address.MapToIPv4();
            }

            // Must be same address family
            if (address.AddressFamily != networkAddress.AddressFamily)
                return false;

            var addressBytes = address.GetAddressBytes();
            var networkBytes = networkAddress.GetAddressBytes();

            var bytesToCheck = prefixLength / 8;
            var bitsToCheck = prefixLength % 8;

            // Check full bytes
            for (int i = 0; i < bytesToCheck; i++)
            {
                if (addressBytes[i] != networkBytes[i])
                    return false;
            }

            // Check remaining bits
            if (bitsToCheck > 0)
            {
                var mask = (byte)(0xFF << (8 - bitsToCheck));
                if ((addressBytes[bytesToCheck] & mask) != (networkBytes[bytesToCheck] & mask))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
