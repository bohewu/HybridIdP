using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Authorization;

/// <summary>
/// Authorization requirement for IP whitelist validation
/// </summary>
public class IpWhitelistRequirement : IAuthorizationRequirement
{
    // Empty marker requirement - actual IPs are read from configuration in the handler
}
