using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public partial class LoginService : ILoginService
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
            LogUserLockedOut(user.UserName);
            return LoginResult.LockedOut();
        }

        if (await _userManager.CheckPasswordAsync(user, password))
        {
            await _userManager.ResetAccessFailedCountAsync(user);
            LogUserAuthenticated(user.UserName);
            return LoginResult.Success(user);
        }

        // Password is incorrect, handle lockout logic
        LogInvalidPasswordAttempt(user.UserName);
        
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        if (policy.MaxFailedAccessAttempts > 0)
        {
            await _userManager.AccessFailedAsync(user);
            var accessFailedCount = await _userManager.GetAccessFailedCountAsync(user);

            if (accessFailedCount >= policy.MaxFailedAccessAttempts)
            {
                LogUserLockedOutCheck(user.UserName);
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

        LogLegacyUserAuthenticated(login);
        return LoginResult.LegacySuccess(provisionedUser);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account '{UserName}' is locked out.")]
    partial void LogUserLockedOut(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' authenticated successfully.")]
    partial void LogUserAuthenticated(string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid password attempt for user '{UserName}'.")]
    partial void LogInvalidPasswordAttempt(string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User account '{UserName}' locked out due to too many failed login attempts.")]
    partial void LogUserLockedOutCheck(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{Login}' authenticated via legacy auth and JIT provisioned.")]
    partial void LogLegacyUserAuthenticated(string login);
}
