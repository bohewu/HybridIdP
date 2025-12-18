using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // Added
using System.Net;
using Core.Application.Options; // Added

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization handler for IP whitelist-based access control
/// Supports IPv4, IPv6, and CIDR notation
/// Reads allowed IPs from configuration dynamically on each request
/// </summary>
public partial class IpWhitelistAuthorizationHandler : AuthorizationHandler<IpWhitelistRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<ObservabilityOptions> _options; // Changed to IOptionsMonitor for dynamic updates
    private readonly ILogger<IpWhitelistAuthorizationHandler> _logger;

    public IpWhitelistAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<ObservabilityOptions> options, // Changed
        ILogger<IpWhitelistAuthorizationHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options; // Changed
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IpWhitelistRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            LogHttpContextNull(_logger);
            return Task.CompletedTask;
        }

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            LogRemoteIpNull(_logger);
            return Task.CompletedTask;
        }

        // Read allowed IPs from configuration dynamically
        var allowedIPs = _options.CurrentValue.AllowedIPs ?? Array.Empty<string>(); // Added check

        LogCheckingWhitelist(_logger, remoteIp, string.Join(", ", allowedIPs));

        if (IsIpAllowed(remoteIp, allowedIPs))
        {
            LogIpAllowed(_logger, remoteIp);
            context.Succeed(requirement);
        }
        else
        {
            LogIpNotWhitelisted(_logger, remoteIp, string.Join(", ", allowedIPs));
        }

        return Task.CompletedTask;
    }

    private bool IsIpAllowed(IPAddress remoteIp, IReadOnlyCollection<string> allowedIPs)
    {
        // Map IPv4-mapped IPv6 addresses to IPv4 for comparison
        var ipToCheck = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;

        foreach (var allowedIp in allowedIPs)
        {
            if (allowedIp.Contains('/'))
            {
                // CIDR notation (e.g., "10.0.0.0/8")
                if (IsInCidrRange(ipToCheck, allowedIp))
                {
                    return true;
                }
            }
            else if (IPAddress.TryParse(allowedIp, out var allowedAddress))
            {
                // Exact IP match
                if (ipToCheck.Equals(allowedAddress))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsInCidrRange(IPAddress address, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2)
                return false;

            if (!IPAddress.TryParse(parts[0], out var networkAddress))
                return false;

            if (!int.TryParse(parts[1], out var prefixLength))
                return false;

            // Convert to IPv4 if comparing IPv4-mapped IPv6 with IPv4 network
            if (address.IsIPv4MappedToIPv6 && networkAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                address = address.MapToIPv4();
            }

            // Must be same address family
            if (address.AddressFamily != networkAddress.AddressFamily)
                return false;

            var addressBytes = address.GetAddressBytes();
            var networkBytes = networkAddress.GetAddressBytes();

            // Validate prefix length
            int maxPrefixLength = addressBytes.Length * 8;
            if (prefixLength < 0 || prefixLength > maxPrefixLength)
                return false;

            var bytesToCheck = prefixLength / 8;
            var bitsToCheck = prefixLength % 8;

            // Check full bytes
            for (int i = 0; i < bytesToCheck; i++)
            {
                if (addressBytes[i] != networkBytes[i])
                    return false;
            }

            // Check remaining bits
            if (bitsToCheck > 0 && bytesToCheck < addressBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - bitsToCheck));
                if ((addressBytes[bytesToCheck] & mask) != (networkBytes[bytesToCheck] & mask))
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogCidrParseError(_logger, ex, cidr);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "HttpContext is null, denying access")]
    static partial void LogHttpContextNull(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote IP address is null, denying access")]
    static partial void LogRemoteIpNull(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking IP whitelist: RemoteIP={RemoteIp}, AllowedIPs={AllowedIPs}")]
    static partial void LogCheckingWhitelist(ILogger logger, IPAddress remoteIp, string allowedIPs);

    [LoggerMessage(Level = LogLevel.Information, Message = "IP {RemoteIp} is allowed, granting access")]
    static partial void LogIpAllowed(ILogger logger, IPAddress remoteIp);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IP {RemoteIp} is not in whitelist (configured: {AllowedIPs}), denying access")]
    static partial void LogIpNotWhitelisted(ILogger logger, IPAddress remoteIp, string allowedIPs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error parsing CIDR notation: {Cidr}")]
    static partial void LogCidrParseError(ILogger logger, Exception ex, string cidr);
}
