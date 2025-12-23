using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public partial class SecurityPolicyService : ISecurityPolicyService
{
    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SecurityPolicyService> _logger;
    private const string CurrentPolicyCacheKey = "SecurityPolicy:Current";

    public SecurityPolicyService(IApplicationDbContext db, IMemoryCache cache, ILogger<SecurityPolicyService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SecurityPolicy> GetCurrentPolicyAsync()
    {
        if (_cache.TryGetValue(CurrentPolicyCacheKey, out SecurityPolicy? policy))
        {
            return policy!;
        }

        policy = await _db.SecurityPolicies.FirstOrDefaultAsync();

        if (policy == null)
        {
            LogNoSecurityPolicyFound();
            policy = new SecurityPolicy();
            await _db.SecurityPolicies.AddAsync(policy);
            await _db.SaveChangesAsync(default);
        }
        
        _cache.Set(CurrentPolicyCacheKey, policy, TimeSpan.FromHours(1));
        return policy;
    }

    public async Task UpdatePolicyAsync(SecurityPolicyDto policyDto, string updatedBy)
    {
        // Business rule validation: Cannot enable mandatory MFA enrollment without at least one MFA method enabled
        // Passkeys count as MFA since Login.cshtml.cs checks for them as an alternative to TOTP/Email MFA
        if (policyDto.EnforceMandatoryMfaEnrollment && !policyDto.EnableTotpMfa && !policyDto.EnableEmailMfa && !policyDto.EnablePasskey)
        {
            throw new InvalidOperationException(
                "Cannot enable mandatory MFA enrollment without at least one MFA method (TOTP, Email, or Passkey) enabled.");
        }

        var policy = await _db.SecurityPolicies.FirstOrDefaultAsync();
        if (policy == null)
        {
            // This should not happen in practice after the first Get call, but as a safeguard:
            policy = new SecurityPolicy();
            await _db.SecurityPolicies.AddAsync(policy);
        }

        // Update properties from DTO
        policy.MinPasswordLength = policyDto.MinPasswordLength;
        policy.RequireUppercase = policyDto.RequireUppercase;
        policy.RequireLowercase = policyDto.RequireLowercase;
        policy.RequireDigit = policyDto.RequireDigit;
        policy.RequireNonAlphanumeric = policyDto.RequireNonAlphanumeric;
        policy.MinCharacterTypes = policyDto.MinCharacterTypes;
        policy.PasswordHistoryCount = policyDto.PasswordHistoryCount;
        policy.PasswordExpirationDays = policyDto.PasswordExpirationDays;
        policy.MinPasswordAgeDays = policyDto.MinPasswordAgeDays;
        policy.MaxFailedAccessAttempts = policyDto.MaxFailedAccessAttempts;
        policy.LockoutDurationMinutes = policyDto.LockoutDurationMinutes;
        policy.AbnormalLoginHistoryCount = policyDto.AbnormalLoginHistoryCount;
        policy.BlockAbnormalLogin = policyDto.BlockAbnormalLogin;
        policy.AllowSelfPasswordChange = policyDto.AllowSelfPasswordChange;
        
        // MFA & Passkey Toggles
        policy.EnablePasskey = policyDto.EnablePasskey;
        policy.EnableTotpMfa = policyDto.EnableTotpMfa;
        policy.EnableEmailMfa = policyDto.EnableEmailMfa;
        policy.MaxPasskeysPerUser = policyDto.MaxPasskeysPerUser;
        policy.RequireMfaForPasskey = policyDto.RequireMfaForPasskey;
        policy.EnforceMandatoryMfaEnrollment = policyDto.EnforceMandatoryMfaEnrollment;
        policy.MfaEnforcementGracePeriodDays = policyDto.MfaEnforcementGracePeriodDays;
        policy.CustomForgotPasswordUrl = policyDto.CustomForgotPasswordUrl;
        
        // Update metadata
        policy.UpdatedUtc = DateTime.UtcNow;
        policy.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(default);
        
        // Invalidate cache
        _cache.Remove(CurrentPolicyCacheKey);
        LogSecurityPolicyUpdated(updatedBy);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No security policy found in database, creating a default one.")]
    partial void LogNoSecurityPolicyFound();

    [LoggerMessage(Level = LogLevel.Information, Message = "Security policy updated by {UpdatedBy}")]
    partial void LogSecurityPolicyUpdated(string updatedBy);
}
