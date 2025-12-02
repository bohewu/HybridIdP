using Core.Application.DTOs;
using Core.Domain;

namespace Core.Application;

public interface IJitProvisioningService
{
    /// <summary>
    /// Provision or update Person and ApplicationUser for external authentication
    /// </summary>
    /// <param name="externalAuth">External authentication result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Provisioned or updated ApplicationUser</returns>
    Task<ApplicationUser> ProvisionExternalUserAsync(
        ExternalAuthResult externalAuth,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Legacy system JIT Provisioning (for backward compatibility)
    /// </summary>
    [Obsolete("Use ProvisionExternalUserAsync instead")]
    Task<ApplicationUser> ProvisionUserAsync(
        LegacyUserDto dto,
        CancellationToken cancellationToken = default);
}
