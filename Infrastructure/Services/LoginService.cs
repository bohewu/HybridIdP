using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class LoginService : ILoginService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ILegacyAuthService _legacyAuthService;
    private readonly IJitProvisioningService _jitProvisioningService;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        UserManager<ApplicationUser> userManager,
        ISecurityPolicyService securityPolicyService,
        ILegacyAuthService legacyAuthService,
        IJitProvisioningService jitProvisioningService,
        ILogger<LoginService> logger)
    {
        _userManager = userManager;
        _securityPolicyService = securityPolicyService;
        _legacyAuthService = legacyAuthService;
        _jitProvisioningService = jitProvisioningService;
        _logger = logger;
    }

    public async Task<LoginResult> AuthenticateAsync(string login, string password)
    {
        var user = await _userManager.FindByEmailAsync(login) 
                   ?? await _userManager.FindByNameAsync(login);

        if (user != null)
        {
            return await AuthenticateLocalUserAsync(user, password);
        }

        return await AuthenticateLegacyUserAsync(login, password);
    }

    private async Task<LoginResult> AuthenticateLocalUserAsync(ApplicationUser user, string password)
    {
        if (await _userManager.IsLockedOutAsync(user))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("User account '{username}' is locked out.", user.UserName);
            }
            return LoginResult.LockedOut();
        }

        if (await _userManager.CheckPasswordAsync(user, password))
        {
            await _userManager.ResetAccessFailedCountAsync(user);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("User '{username}' authenticated successfully.", user.UserName);
            }
            return LoginResult.Success(user);
        }

        // Password is incorrect, handle lockout logic
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("Invalid password attempt for user '{username}'.", user.UserName);
        }
        
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        if (policy.MaxFailedAccessAttempts > 0)
        {
            await _userManager.AccessFailedAsync(user);
            var accessFailedCount = await _userManager.GetAccessFailedCountAsync(user);

            if (accessFailedCount >= policy.MaxFailedAccessAttempts)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("User account '{username}' locked out due to too many failed login attempts.", user.UserName);
                }
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(policy.LockoutDurationMinutes));
                return LoginResult.LockedOut();
            }
        }
        
        return LoginResult.InvalidCredentials();
    }

    private async Task<LoginResult> AuthenticateLegacyUserAsync(string login, string password)
    {
        var legacyResult = await _legacyAuthService.ValidateAsync(login, password);
        if (!legacyResult.IsAuthenticated)
        {
            return LoginResult.InvalidCredentials();
        }

        // Map LegacyUserDto to ExternalAuthResult
        var externalAuth = new Core.Application.DTOs.ExternalAuthResult
        {
             Provider = "Legacy",
             ProviderKey = legacyResult.ExternalId ?? login,
             Email = legacyResult.Email,
             DisplayName = legacyResult.FullName,
             Department = legacyResult.Department,
             JobTitle = legacyResult.JobTitle,
             PhoneNumber = legacyResult.Phone,
             NationalId = legacyResult.NationalId,
             PassportNumber = legacyResult.PassportNumber,
             ResidentCertificateNumber = legacyResult.ResidentCertificateNumber
        };

        var provisionedUser = await _jitProvisioningService.ProvisionExternalUserAsync(externalAuth);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("User '{login}' authenticated via legacy auth and JIT provisioned.", login);
        }
        return LoginResult.LegacySuccess(provisionedUser);
    }
}
