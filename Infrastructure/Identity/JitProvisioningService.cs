using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

public class JitProvisioningService : IJitProvisioningService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public JitProvisioningService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public Task<ApplicationUser> ProvisionUserAsync(LegacyUserDto dto, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
