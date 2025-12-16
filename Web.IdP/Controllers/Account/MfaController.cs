using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Web.IdP.Attributes;

namespace Web.IdP.Controllers.Account;

/// <summary>
/// MFA (Multi-Factor Authentication) API endpoints for TOTP setup and management.
/// </summary>
[ApiController]
[Route("api/account/mfa")]
[ApiAuthorize]
public class MfaController : ControllerBase
{
    private readonly IMfaService _mfaService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;
    private readonly ILogger<MfaController> _logger;

    public MfaController(
        IMfaService mfaService,
        UserManager<ApplicationUser> userManager,
        IAuditService auditService,
        ILogger<MfaController> logger)
    {
        _mfaService = mfaService;
        _userManager = userManager;
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// Get current MFA status for the authenticated user.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<MfaStatusResponse>> GetStatus(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var recoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user);
        var hasPassword = await _userManager.HasPasswordAsync(user);

        return Ok(new MfaStatusResponse
        {
            TwoFactorEnabled = user.TwoFactorEnabled,
            HasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) != null,
            RecoveryCodesLeft = recoveryCodesLeft,
            HasPassword = hasPassword,
            EmailMfaEnabled = user.EmailMfaEnabled
        });
    }
    
    /// <summary>
    /// Gets the current user from either Identity cookie or Bearer token claims.
    /// </summary>
    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        // Try UserManager first (works with Identity cookie auth)
        var user = await _userManager.GetUserAsync(User);
        if (user != null)
            return user;
        
        // Fallback: Get user ID from Bearer token claims (OpenIddict uses "sub" claim)
        var userId = User.GetClaim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject);
        
        if (string.IsNullOrEmpty(userId))
            return null;
        
        return await _userManager.FindByIdAsync(userId);
    }

    /// <summary>
    /// Get TOTP setup information including QR code for authenticator apps.
    /// </summary>
    [HttpGet("setup")]
    public async Task<ActionResult<MfaSetupResponse>> GetSetup(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var setupInfo = await _mfaService.GetTotpSetupInfoAsync(user, ct);

        return Ok(new MfaSetupResponse
        {
            SharedKey = setupInfo.SharedKey,
            AuthenticatorUri = setupInfo.AuthenticatorUri,
            QrCodeDataUri = setupInfo.QrCodeDataUri
        });
    }

    /// <summary>
    /// Verify TOTP code and enable MFA.
    /// </summary>
    [HttpPost("verify")]
    public async Task<ActionResult<MfaVerifyResponse>> Verify([FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var isValid = await _mfaService.VerifyAndEnableTotpAsync(user, request.Code, ct);

        if (isValid)
        {
            _logger.LogInformation("User {UserId} enabled MFA", user.Id);
            await _auditService.LogEventAsync("MfaEnabled", user.Id.ToString(), null, null, null);

            // Generate recovery codes
            var recoveryCodes = await _mfaService.GenerateRecoveryCodesAsync(user, 10, ct);

            return Ok(new MfaVerifyResponse
            {
                Success = true,
                RecoveryCodes = recoveryCodes.ToList()
            });
        }

        return Ok(new MfaVerifyResponse
        {
            Success = false,
            Error = "invalidCode"
        });
    }

    /// <summary>
    /// Disable MFA for the authenticated user.
    /// Requires password verification OR TOTP code for passwordless users.
    /// </summary>
    [HttpPost("disable")]
    public async Task<ActionResult> Disable([FromBody] MfaDisableRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        // Check if user has a local password
        var hasPassword = await _userManager.HasPasswordAsync(user);
        
        if (hasPassword)
        {
            // User has password - require password verification
            if (string.IsNullOrEmpty(request.Password))
            {
                return BadRequest(new { error = "passwordRequired" });
            }
            
            var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!isPasswordValid)
            {
                return BadRequest(new { error = "invalidPassword" });
            }
        }
        else
        {
            // User has no password (legacy/SSO) - require TOTP verification
            if (string.IsNullOrEmpty(request.TotpCode))
            {
                return BadRequest(new { error = "totpRequired", requiresTotp = true });
            }
            
            var isTotpValid = await _mfaService.ValidateTotpCodeAsync(user, request.TotpCode, ct);
            if (!isTotpValid)
            {
                return BadRequest(new { error = "invalidCode" });
            }
        }

        await _mfaService.DisableMfaAsync(user, ct);

        _logger.LogInformation("User {UserId} disabled MFA", user.Id);
        await _auditService.LogEventAsync("MfaDisabled", user.Id.ToString(), null, null, null);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Generate new recovery codes.
    /// </summary>
    [HttpPost("recovery-codes")]
    public async Task<ActionResult<RecoveryCodesResponse>> GenerateRecoveryCodes(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        if (!user.TwoFactorEnabled)
        {
            return BadRequest(new { error = "MFA is not enabled" });
        }

        var codes = await _mfaService.GenerateRecoveryCodesAsync(user, 10, ct);

        _logger.LogInformation("User {UserId} regenerated recovery codes", user.Id);
        await _auditService.LogEventAsync("MfaRecoveryCodesRegenerated", user.Id.ToString(), null, null, null);

        return Ok(new RecoveryCodesResponse
        {
            RecoveryCodes = codes.ToList()
        });
    }

    #region Email MFA (Phase 20.3)

    /// <summary>
    /// Send a 6-digit verification code to the user's email.
    /// </summary>
    [HttpPost("email/send")]
    public async Task<ActionResult> SendEmailMfaCode(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(user.Email))
        {
            return BadRequest(new { error = "noEmail", message = "User does not have an email address." });
        }

        await _mfaService.SendEmailMfaCodeAsync(user, ct);

        _logger.LogInformation("Email MFA code sent to user {UserId}", user.Id);

        return Ok(new { success = true, message = "Code sent to email." });
    }

    /// <summary>
    /// Verify the email MFA code.
    /// </summary>
    [HttpPost("email/verify")]
    public async Task<ActionResult> VerifyEmailMfaCode([FromBody] EmailMfaVerifyRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var isValid = await _mfaService.VerifyEmailMfaCodeAsync(user, request.Code, ct);

        if (isValid)
        {
            _logger.LogInformation("Email MFA code verified for user {UserId}", user.Id);
            return Ok(new { success = true });
        }

        return Ok(new { success = false, error = "invalidOrExpiredCode" });
    }

    /// <summary>
    /// Enable Email MFA for the user.
    /// </summary>
    [HttpPost("email/enable")]
    public async Task<ActionResult> EnableEmailMfa(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(user.Email))
        {
            return BadRequest(new { error = "noEmail", message = "User does not have an email address." });
        }

        await _mfaService.EnableEmailMfaAsync(user, ct);

        _logger.LogInformation("User {UserId} enabled Email MFA", user.Id);
        await _auditService.LogEventAsync("EmailMfaEnabled", user.Id.ToString(), null, null, null);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Disable Email MFA for the user.
    /// </summary>
    [HttpPost("email/disable")]
    public async Task<ActionResult> DisableEmailMfa(CancellationToken ct)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        await _mfaService.DisableEmailMfaAsync(user, ct);

        _logger.LogInformation("User {UserId} disabled Email MFA", user.Id);
        await _auditService.LogEventAsync("EmailMfaDisabled", user.Id.ToString(), null, null, null);

        return Ok(new { success = true });
    }

    #endregion
}

#region DTOs

public record MfaStatusResponse
{
    public bool TwoFactorEnabled { get; init; }
    public bool HasAuthenticator { get; init; }
    public int RecoveryCodesLeft { get; init; }
    public bool HasPassword { get; init; }
    public bool EmailMfaEnabled { get; init; }
}

public record MfaSetupResponse
{
    public required string SharedKey { get; init; }
    public required string AuthenticatorUri { get; init; }
    public required string QrCodeDataUri { get; init; }
}

public record MfaVerifyRequest
{
    public required string Code { get; init; }
}

public record MfaVerifyResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string>? RecoveryCodes { get; init; }
}

public record MfaDisableRequest
{
    /// <summary>
    /// Password for users with local credentials.
    /// </summary>
    public string? Password { get; init; }
    
    /// <summary>
    /// TOTP code for users without local passwords (legacy/SSO).
    /// </summary>
    public string? TotpCode { get; init; }
}

public record RecoveryCodesResponse
{
    public required List<string> RecoveryCodes { get; init; }
}

public record EmailMfaVerifyRequest
{
    public required string Code { get; init; }
}

#endregion
