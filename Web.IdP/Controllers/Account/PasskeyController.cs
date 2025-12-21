using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Infrastructure;

using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Account;

public record LoginOptionsRequest(string? Username);

[Route("api/passkey")]
[ApiController]
public partial class PasskeyController : ControllerBase
{
    private readonly IPasskeyService _passkeyService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ILogger<PasskeyController> _logger;

    public PasskeyController(
        IPasskeyService passkeyService,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ISecurityPolicyService securityPolicyService,
        ApplicationDbContext dbContext,
        IAuditService auditService,
        ILogger<PasskeyController> logger)
    {
        _passkeyService = passkeyService;
        _signInManager = signInManager;
        _userManager = userManager;
        _securityPolicyService = securityPolicyService;
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpPost("register-options")]
    [ApiAuthorize]
    [EnableRateLimiting("default")]
    public async Task<IActionResult> MakeCredentialOptions(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        // 1. Get security policy
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        
        // 2. Check if passkey is enabled
        if (!policy.EnablePasskey)
        {
            LogPasskeyFeatureDisabled();
            return StatusCode(403, new { error = "Passkey authentication is disabled" });
        }
        
        // 3. Check RequireMfaForPasskey policy - API validation
        if (policy.RequireMfaForPasskey)
        {
            var hasTotpMfa = user.TwoFactorEnabled;
            var hasEmailMfa = user.EmailMfaEnabled;
            
            if (!hasTotpMfa && !hasEmailMfa)
            {
                LogPasskeyBlockedNoMfa(user.Id);
                return StatusCode(403, new { 
                    error = "MfaRequiredForPasskey",
                    message = "You must enable TOTP or Email MFA before registering a passkey"
                });
            }
        }
        
        // 4. Count existing passkeys
        var existingCount = await _dbContext.UserCredentials
            .CountAsync(c => c.UserId == user.Id, ct);
        
        if (existingCount >= policy.MaxPasskeysPerUser)
        {
            LogPasskeyLimitReached(user.Id, existingCount, policy.MaxPasskeysPerUser);
            return BadRequest(new { 
                error = $"Maximum passkey limit reached ({policy.MaxPasskeysPerUser})" 
            });
        }

        var options = await _passkeyService.GetRegistrationOptionsAsync(user, ct);

        // Store options in session for verification
        HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());

        LogRegistrationOptionsGenerated(user.UserName);

        return Ok(options);
    }

    [HttpPost("register")]
    [ApiAuthorize]
    public async Task<IActionResult> MakeCredential([FromBody] System.Text.Json.JsonElement attestationResponse, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (!policy.EnablePasskey)
        {
            LogPasskeyRegistrationBlocked(user.Id);
            return StatusCode(403, new { error = "Passkey authentication is disabled" });
        }

        var jsonOptions = HttpContext.Session.GetString("fido2.attestationOptions");
        if (string.IsNullOrEmpty(jsonOptions))
        {
            return BadRequest(new { success = false, error = "Session expired" });
        }

        var result = await _passkeyService.RegisterCredentialsAsync(user, attestationResponse.ToString(), jsonOptions, ct);

        if (result.Success)
        {
            LogPasskeyRegistered(user.UserName);
            await _auditService.LogEventAsync(
                Core.Domain.Constants.AuditEventTypes.PasskeyRegistered,
                user.Id.ToString(),
                null, // details
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers["User-Agent"].FirstOrDefault());
            return Ok(new { success = true });
        }

        return BadRequest(new { success = false, error = result.Error });
    }

    [HttpGet("list")]
    [ApiAuthorize]
    public async Task<IActionResult> ListPasskeys(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null) return Unauthorized();
        
        var passkeys = await _passkeyService.GetUserPasskeysAsync(user.Id, ct);
        return Ok(passkeys);
    }

    [HttpDelete("{id}")]
    [ApiAuthorize]
    public async Task<IActionResult> DeletePasskey(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null) return Unauthorized();
        
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (policy.EnforceMandatoryMfaEnrollment)
        {
            // Check if this (passkeys) is the last MFA factor
            var existingCount = await _dbContext.UserCredentials.CountAsync(c => c.UserId == user.Id, ct);
            if (existingCount == 1)
            {
                // Last passkey - check other factors
                var otherFactors = 0;
                if (user.TwoFactorEnabled) otherFactors++;
                if (user.EmailMfaEnabled) otherFactors++;

                if (otherFactors == 0)
                {
                    return BadRequest(new { error = "mandatoryMfaEnforced" });
                }
            }
        }

        var result = await _passkeyService.DeletePasskeyAsync(user.Id, id, ct);
        if (!result)
        {
            return NotFound(new { error = "Passkey not found" });
        }

        LogPasskeyDeleted(user.Id, id);
        await _auditService.LogEventAsync(
            Core.Domain.Constants.AuditEventTypes.PasskeyDeleted,
            user.Id.ToString(),
            $"{{\"passkeyId\":{id}}}",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers["User-Agent"].FirstOrDefault());
        return Ok(new { success = true });
    }

    private async Task<ApplicationUser?> GetAuthenticatedUserAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user != null) return user;

        // Fallback: OpenIddict uses 'sub' claim, which simple GetUserAsync might not map to NameIdentifier by default
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        // Keep logging for verification if needed, but reducing verbosity
        if (!string.IsNullOrEmpty(userId))
        {
            return await _userManager.FindByIdAsync(userId);
        }
        
        return null;
    }

    [HttpPost("login-options")]
    public async Task<IActionResult> AssertionOptionsPost([FromBody] LoginOptionsRequest request, CancellationToken ct)
    {
        var options = await _passkeyService.GetAssertionOptionsAsync(request.Username, ct);
        
        HttpContext.Session.SetString("fido2.assertionOptions", options.ToJson());
        
        LogAssertionOptionsGenerated(request.Username);
        
        return Ok(options);
    }

    [HttpPost("login")]
    public async Task<IActionResult> MakeAssertion([FromBody] System.Text.Json.JsonElement clientResponse, CancellationToken ct)
    {
        var jsonOptions = HttpContext.Session.GetString("fido2.assertionOptions");
        if (string.IsNullOrEmpty(jsonOptions))
        {
            return BadRequest(new { success = false, error = "Session expired" });
        }

        var result = await _passkeyService.VerifyAssertionAsync(clientResponse.ToString(), jsonOptions, ct);

        if (result.Success && result.User != null)
        {
            // 1. Check Person.Status (CRITICAL SECURITY FIX)
            if (result.User.Person != null && result.User.Person.Status != PersonStatus.Active)
            {
                LogPasskeyLoginBlockedByStatus(result.User.Person.Id, result.User.Person.Status);
                return BadRequest(new { success = false, error = "Account not active" });
            }
            
            // 2. Check User.IsActive
            if (!result.User.IsActive)
            {
                LogPasskeyLoginBlockedByDeactivation(result.User.Id);
                return BadRequest(new { success = false, error = "User account deactivated" });
            }
            
            // 3. All checks passed - Sign in
            // Add AMR to session
            AddAmrToSession(Core.Domain.Constants.AuthConstants.Amr.HardwareKey);
            AddAmrToSession(Core.Domain.Constants.AuthConstants.Amr.UserPresence);
            AddAmrToSession(Core.Domain.Constants.AuthConstants.Amr.Mfa);

            await _signInManager.SignInAsync(result.User, isPersistent: false);
            LogPasskeyLogin(result.User.UserName);
            return Ok(new { success = true, username = result.User.UserName });
        }

        return BadRequest(new { success = false, error = result.Error });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated FIDO2 registration options for user '{UserName}'.")]
    partial void LogRegistrationOptionsGenerated(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' registered a new passkey.")]
    partial void LogPasskeyRegistered(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated FIDO2 assertion options for user '{UserName}'.")]
    partial void LogAssertionOptionsGenerated(string? userName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User '{UserName}' logged in with passkey.")]
    partial void LogPasskeyLogin(string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Passkey registration blocked: feature disabled")]
    partial void LogPasskeyFeatureDisabled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Passkey registration blocked for user {UserId}: limit reached ({Count}/{Max})")]
    partial void LogPasskeyLimitReached(Guid userId, int count, int max);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Passkey registration blocked for user {UserId}: feature disabled")]
    partial void LogPasskeyRegistrationBlocked(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Passkey registration blocked for user {UserId}: MFA required but not enabled")]
    partial void LogPasskeyBlockedNoMfa(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} deleted passkey {CredentialId}")]
    partial void LogPasskeyDeleted(Guid userId, int credentialId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Passkey login blocked for person {PersonId} with status {Status}")]
    partial void LogPasskeyLoginBlockedByStatus(Guid personId, PersonStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Passkey login blocked for deactivated user {UserId}")]
    partial void LogPasskeyLoginBlockedByDeactivation(Guid userId);

    private void AddAmrToSession(string amr)
    {
        var currentAmrJson = HttpContext.Session.GetString("AuthenticationMethods");
        List<string> amrList = string.IsNullOrEmpty(currentAmrJson) 
            ? new List<string>() 
            : JsonSerializer.Deserialize<List<string>>(currentAmrJson) ?? new List<string>();
        
        if (!amrList.Contains(amr))
        {
            amrList.Add(amr);
            HttpContext.Session.SetString("AuthenticationMethods", JsonSerializer.Serialize(amrList));
        }
    }
}
