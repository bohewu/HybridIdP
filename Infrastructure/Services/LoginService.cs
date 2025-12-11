using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service for authenticating users via local or legacy systems.
/// Phase 18: Added Person lifecycle validation to block login for inactive persons.
/// </summary>
public partial class LoginService : ILoginService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ILegacyAuthService _legacyAuthService;
    private readonly IJitProvisioningService _jitProvisioningService;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        UserManager<ApplicationUser> userManager,
        ISecurityPolicyService securityPolicyService,
        ILegacyAuthService legacyAuthService,
        IJitProvisioningService jitProvisioningService,
        IApplicationDbContext dbContext,
        ILogger<LoginService> logger)
    {
        _userManager = userManager;
        _securityPolicyService = securityPolicyService;
        _legacyAuthService = legacyAuthService;
        _jitProvisioningService = jitProvisioningService;
        _dbContext = dbContext;
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
        // Phase 18: Check Person status before authentication
        var personCheckResult = await ValidatePersonStatusAsync(user);
        if (personCheckResult != null)
        {
            return personCheckResult;
        }

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

        // Phase 18: Check Person status after JIT provisioning
        var personCheckResult = await ValidatePersonStatusAsync(provisionedUser);
        if (personCheckResult != null)
        {
            return personCheckResult;
        }

        LogLegacyUserAuthenticated(login);
        return LoginResult.LegacySuccess(provisionedUser);
    }

    /// <summary>
    /// Validates if the user's linked Person is allowed to authenticate.
    /// Phase 18: Personnel Lifecycle Management
    /// </summary>
    private async Task<LoginResult?> ValidatePersonStatusAsync(ApplicationUser user)
    {
        // If user is not linked to a Person, allow login (no Person-level restrictions)
        if (user.PersonId == null)
        {
            return null;
        }

        // Load the Person from the database
        var person = await _dbContext.Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == user.PersonId);

        if (person == null)
        {
            // Person was deleted - block login
            LogPersonNotFound(user.UserName, user.PersonId.Value);
            return LoginResult.PersonInactive("Associated person record not found");
        }

        // Use the Person.CanAuthenticate() helper method
        if (!person.CanAuthenticate())
        {
            var reason = GetPersonInactiveReason(person);
            LogPersonInactive(user.UserName, person.Id, reason);
            return LoginResult.PersonInactive(reason);
        }

        return null; // Person is valid, continue with login
    }

    /// <summary>
    /// Gets a user-friendly reason why the Person cannot authenticate.
    /// </summary>
    private static string GetPersonInactiveReason(Core.Domain.Entities.Person person)
    {
        if (person.IsDeleted)
            return "Person record has been deleted";
        
        if (person.Status != PersonStatus.Active)
            return $"Person status is {person.Status}";
        
        var now = DateTime.UtcNow.Date;
        if (person.StartDate.HasValue && person.StartDate.Value.Date > now)
            return "Person employment has not started yet";
        
        if (person.EndDate.HasValue && person.EndDate.Value.Date < now)
            return "Person employment has ended";
        
        return "Person is not active";
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for user '{UserName}': Person {PersonId} not found.")]
    partial void LogPersonNotFound(string? userName, Guid personId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login blocked for user '{UserName}': Person {PersonId} is inactive - {Reason}.")]
    partial void LogPersonInactive(string? userName, Guid personId, string reason);
}

