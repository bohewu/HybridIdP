using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Authorization;

/// <summary>
/// Requirement for IP whitelist-based authorization
/// </summary>
public class IpWhitelistRequirement : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> AllowedIPs { get; }

    public IpWhitelistRequirement(IEnumerable<string> allowedIPs)
    {
        AllowedIPs = allowedIPs.ToList().AsReadOnly();
    }
}
