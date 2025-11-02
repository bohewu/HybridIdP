using Core.Application.DTOs;
using Core.Domain;

namespace Core.Application;

public interface IJitProvisioningService
{
    Task<ApplicationUser> ProvisionUserAsync(LegacyUserDto dto, CancellationToken cancellationToken = default);
}
