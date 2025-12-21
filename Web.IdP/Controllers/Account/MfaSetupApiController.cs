using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Core.Application.Interfaces;
using Core.Domain.Constants;
using Web.IdP.Attributes;
using System.Security.Claims;

namespace Web.IdP.Controllers.Account;

/// <summary>
/// MFA Setup API endpoints specifically for the mandatory MFA enrollment flow.
/// These endpoints accept TwoFactorUserIdScheme (partial authentication) in addition to full authentication.
/// This allows users to set up MFA before completing full login.
/// </summary>
[ApiController]
[Route("api/account/mfa-setup")]
[MfaSetupAuthorize]
public partial class MfaSetupApiController : ControllerBase
{
    private readonly IMfaService _mfaService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditService _auditService;
    private readonly IPasskeyService _passkeyService;
    private readonly ILogger<MfaSetupApiController> _logger;

    public MfaSetupApiController(
        IMfaService mfaService,
        ISecurityPolicyService securityPolicyService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuditService auditService,
        IPasskeyService passkeyService,
        ILogger<MfaSetupApiController> logger)
    {
        _mfaService = mfaService;
        _securityPolicyService = securityPolicyService;
        _userManager = userManager;
        _signInManager = signInManager;
        _auditService = auditService;
        _passkeyService = passkeyService;
        _logger = logger;
    }

    /// <summary>
    /// Get current MFA status for the user (supports partial authentication).
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<MfaSetupStatusResponse>> GetStatus(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var recoveryCodesLeft = await _mfaService.CountRecoveryCodesAsync(user, ct);
        var hasPassword = await _userManager.HasPasswordAsync(user);
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();

        return Ok(new MfaSetupStatusResponse
        {
            TwoFactorEnabled = user.TwoFactorEnabled,
            HasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) != null,
            RecoveryCodesLeft = recoveryCodesLeft,
            HasPassword = hasPassword,
            EmailMfaEnabled = user.EmailMfaEnabled,
            EnableTotpMfa = policy.EnableTotpMfa,
            EnableEmailMfa = policy.EnableEmailMfa,
            EnablePasskey = policy.EnablePasskey
        });
    }

    /// <summary>
    /// Get security policy for MFA setup (supports partial authentication).
    /// </summary>
    [HttpGet("policy")]
    public async Task<ActionResult<MfaSetupPolicyResponse>> GetPolicy()
    {
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        return Ok(new MfaSetupPolicyResponse
        {
            RequireMfaForPasskey = policy.RequireMfaForPasskey,
            EnableTotpMfa = policy.EnableTotpMfa,
            EnableEmailMfa = policy.EnableEmailMfa,
            EnablePasskey = policy.EnablePasskey
        });
    }

    /// <summary>
    /// Get TOTP setup information (supports partial authentication).
    /// </summary>
    [HttpGet("totp/setup")]
    public async Task<ActionResult<MfaSetupTotpResponse>> GetTotpSetup(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (!policy.EnableTotpMfa)
        {
            return StatusCode(403, new { error = "mfaDisabled" });
        }

        var setupInfo = await _mfaService.GetTotpSetupInfoAsync(user, ct);

        return Ok(new MfaSetupTotpResponse
        {
            SharedKey = setupInfo.SharedKey,
            AuthenticatorUri = setupInfo.AuthenticatorUri,
            QrCodeDataUri = setupInfo.QrCodeDataUri
        });
    }

    /// <summary>
    /// Verify TOTP code and enable MFA (supports partial authentication).
    /// </summary>
    [HttpPost("totp/verify")]
    public async Task<ActionResult<MfaSetupVerifyResponse>> VerifyTotp([FromBody] MfaSetupVerifyRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (!policy.EnableTotpMfa)
        {
            return StatusCode(403, new { error = "mfaDisabled" });
        }

        var isValid = await _mfaService.VerifyAndEnableTotpAsync(user, request.Code, ct);

        if (isValid)
        {
            LogMfaEnabled(user.Id);
            await _auditService.LogEventAsync("MfaEnabled", user.Id.ToString(), null, null, null);

            // UX Improvement: Sign in user fully so they can access the app immediately
            // This prevents redirection back to Login page and ensures AMR claims are correct
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, new[] 
            { 
                new Claim("amr", AuthConstants.Amr.Password),
                new Claim("amr", AuthConstants.Amr.Mfa),
                new Claim("amr", AuthConstants.Amr.Otp)
            });

            // Generate recovery codes
            var recoveryCodes = await _mfaService.GenerateRecoveryCodesAsync(user, 10, ct);

            return Ok(new MfaSetupVerifyResponse
            {
                Success = true,
                RecoveryCodes = recoveryCodes.ToList()
            });
        }

        return Ok(new MfaSetupVerifyResponse
        {
            Success = false,
            Error = "invalidCode"
        });
    }

    /// <summary>
    /// Enable Email MFA (supports partial authentication).
    /// </summary>
    [HttpPost("email/enable")]
    public async Task<ActionResult> EnableEmailMfa(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (!policy.EnableEmailMfa)
        {
            return StatusCode(403, new { error = "mfaDisabled" });
        }

        if (string.IsNullOrEmpty(user.Email))
        {
            return BadRequest(new { error = "noEmail", message = "User does not have an email address." });
        }

        await _mfaService.EnableEmailMfaAsync(user, ct);

        LogEmailMfaEnabled(user.Id);
        await _auditService.LogEventAsync("EmailMfaEnabled", user.Id.ToString(), null, null, null);

        // UX Improvement: Sign in user fully
        await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, new[] 
        { 
            new Claim("amr", AuthConstants.Amr.Password),
            new Claim("amr", AuthConstants.Amr.Mfa),
            new Claim("amr", AuthConstants.Amr.Otp)
        });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Get passkey list (supports partial authentication).
    /// </summary>
    [HttpGet("passkeys")]
    public async Task<ActionResult> GetPasskeys(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var passkeys = await _passkeyService.GetUserPasskeysAsync(user.Id, ct);
        return Ok(passkeys.Select(p => new
        {
            id = p.Id,
            deviceName = p.DeviceName,
            createdAt = p.CreatedAt,
            lastUsedAt = p.LastUsedAt
        }));
    }

    /// <summary>
    /// Gets the current user from either full authentication or TwoFactorUserIdScheme.
    /// </summary>
    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        // Try full authentication first
        var user = await _userManager.GetUserAsync(User);
        if (user != null)
            return user;

        // Try TwoFactorUserIdScheme (partial authentication during MFA setup)
        var twoFactorResult = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (twoFactorResult.Succeeded && twoFactorResult.Principal != null)
        {
            var userId = twoFactorResult.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
            {
                return await _userManager.FindByIdAsync(userGuid.ToString());
            }
        }

        return null;
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} enabled MFA via setup flow")]
    partial void LogMfaEnabled(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} enabled Email MFA via setup flow")]
    partial void LogEmailMfaEnabled(Guid userId);

    #endregion
}

#region DTOs

public record MfaSetupStatusResponse
{
    public bool TwoFactorEnabled { get; init; }
    public bool HasAuthenticator { get; init; }
    public int RecoveryCodesLeft { get; init; }
    public bool HasPassword { get; init; }
    public bool EmailMfaEnabled { get; init; }
    public bool EnableTotpMfa { get; init; }
    public bool EnableEmailMfa { get; init; }
    public bool EnablePasskey { get; init; }
}

public record MfaSetupPolicyResponse
{
    public bool RequireMfaForPasskey { get; init; }
    public bool EnableTotpMfa { get; init; }
    public bool EnableEmailMfa { get; init; }
    public bool EnablePasskey { get; init; }
}

public record MfaSetupTotpResponse
{
    public required string SharedKey { get; init; }
    public required string AuthenticatorUri { get; init; }
    public required string QrCodeDataUri { get; init; }
}

public record MfaSetupVerifyRequest
{
    public required string Code { get; init; }
}

public record MfaSetupVerifyResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string>? RecoveryCodes { get; init; }
}

#endregion
