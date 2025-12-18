using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Infrastructure.Services;

/// <summary>
/// Multi-Factor Authentication service implementation using ASP.NET Core Identity.
/// </summary>
public class MfaService : IMfaService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBrandingService _brandingService;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _emailTemplateService;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly IDistributedCache _cache;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<MfaService> _logger;
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    public MfaService(
        UserManager<ApplicationUser> userManager, 
        IBrandingService brandingService,
        IEmailService emailService,
        IEmailTemplateService emailTemplateService,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IDistributedCache cache,
        ISecurityPolicyService securityPolicyService,
        ApplicationDbContext dbContext,
        ILogger<MfaService> logger)
    {
        _userManager = userManager;
        _brandingService = brandingService;
        _emailService = emailService;
        _emailTemplateService = emailTemplateService;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _securityPolicyService = securityPolicyService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<MfaSetupInfo> GetTotpSetupInfoAsync(ApplicationUser user, CancellationToken ct = default)
    {
        // Get or create authenticator key
        var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        // Format key for display (groups of 4)
        var sharedKey = FormatKey(unformattedKey!);
        
        // Get issuer from branding settings
        var issuer = await _brandingService.GetAppNameAsync() ?? "HybridIdP";
        var email = user.Email ?? user.UserName ?? "user";
        
        var authenticatorUri = string.Format(
            AuthenticatorUriFormat,
            Uri.EscapeDataString(issuer),
            Uri.EscapeDataString(email),
            unformattedKey);

        // Generate QR code
        var qrCodeDataUri = GenerateQrCodeDataUri(authenticatorUri);

        return new MfaSetupInfo(sharedKey, authenticatorUri, qrCodeDataUri);
    }

    public async Task<bool> VerifyAndEnableTotpAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        // Verify the TOTP code
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);

        if (!isValid)
        {
            return false;
        }

        // Enable 2FA
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        return true;
    }

    public async Task<bool> ValidateTotpCodeAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        // Calculate current TOTP time window (30-second intervals since Unix epoch)
        var currentWindow = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        
        // Replay Attack Prevention: Check if this window was already used
        if (user.LastTotpValidatedWindow.HasValue && user.LastTotpValidatedWindow.Value >= currentWindow)
        {
            // Same code already validated in this time window - reject to prevent replay
            return false;
        }
        
        // Verify the TOTP code with Identity
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);
        
        if (!isValid)
        {
            return false;
        }
        
        // Update last validated window to prevent replay
        user.LastTotpValidatedWindow = currentWindow;
        await _userManager.UpdateAsync(user);
        
        return true;
    }

    public async Task DisableMfaAsync(ApplicationUser user, CancellationToken ct = default)
    {
        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        
        // Cascading revocation: if policy requires MFA for passkeys and this was the last MFA method
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (policy.RequireMfaForPasskey && !user.EmailMfaEnabled)
        {
            await RevokeAllPasskeysAsync(user.Id, ct);
        }
    }

    public async Task<IEnumerable<string>> GenerateRecoveryCodesAsync(ApplicationUser user, int count = 10, CancellationToken ct = default)
    {
        var codes = new List<string>();
        var hashedCodes = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var code = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
            codes.Add(code);
            hashedCodes.Add(_passwordHasher.HashPassword(user, code));
        }

        user.RecoveryCodes = System.Text.Json.JsonSerializer.Serialize(hashedCodes);
        await _userManager.UpdateAsync(user);

        return codes;
    }

    public async Task<bool> ValidateRecoveryCodeAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.RecoveryCodes))
        {
            return false;
        }

        var hashedCodes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.RecoveryCodes);
        if (hashedCodes == null || hashedCodes.Count == 0)
        {
            return false;
        }

        string? matchedCode = null;
        foreach (var hashedCode in hashedCodes)
        {
            var result = _passwordHasher.VerifyHashedPassword(user, hashedCode, code);
            if (result == PasswordVerificationResult.Success)
            {
                matchedCode = hashedCode;
                break;
            }
        }

        if (matchedCode != null)
        {
            hashedCodes.Remove(matchedCode);
            user.RecoveryCodes = System.Text.Json.JsonSerializer.Serialize(hashedCodes);
            await _userManager.UpdateAsync(user);
            return true;
        }

        return false;
    }

    public Task<int> CountRecoveryCodesAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.RecoveryCodes))
        {
            return Task.FromResult(0);
        }

        try
        {
            var hashedCodes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.RecoveryCodes);
            return Task.FromResult(hashedCodes?.Count ?? 0);
        }
        catch
        {
            return Task.FromResult(0);
        }
    }

    #region Email MFA (Phase 20.3)

    public async Task<(bool Success, int RemainingSeconds)> SendEmailMfaCodeAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.Email))
        {
            throw new InvalidOperationException("User does not have an email address.");
        }

        // Rate Limiting Check
        var cacheKey = $"EmailMfa_Cooldown_{user.Id}";
        var cachedValue = await _cache.GetStringAsync(cacheKey, ct);
        
        if (!string.IsNullOrEmpty(cachedValue) && long.TryParse(cachedValue, out long expireTicks))
        {
             var validAfter = new DateTimeOffset(expireTicks, TimeSpan.Zero);
             var remaining = (int)(validAfter - DateTimeOffset.UtcNow).TotalSeconds;
             if (remaining > 0)
             {
                 return (false, remaining);
             }
        }

        // Generate 6-digit numeric code
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        // Hash the code before storing
        user.EmailMfaCode = _passwordHasher.HashPassword(user, code);
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(10); // 10-minute expiry

        await _userManager.UpdateAsync(user);

        // Send email via template service
        var (subject, body) = await _emailTemplateService.RenderMfaCodeEmailAsync(code, 10, user.Locale);
        await _emailService.SendEmailAsync(user.Email, subject, body, isHtml: true, ct);

        // Set Cooldown
        var cooldownExpiry = DateTimeOffset.UtcNow.AddSeconds(60);
        await _cache.SetStringAsync(
            cacheKey, 
            cooldownExpiry.Ticks.ToString(), 
            new DistributedCacheEntryOptions { AbsoluteExpiration = cooldownExpiry }, 
            ct);
        
        return (true, 60);
    }

    public async Task<bool> VerifyEmailMfaCodeAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.EmailMfaCode))
        {
            return false; // No code pending
        }

        if (user.EmailMfaCodeExpiry.HasValue && user.EmailMfaCodeExpiry.Value < DateTime.UtcNow)
        {
            // Code expired, clear it
            user.EmailMfaCode = null;
            user.EmailMfaCodeExpiry = null;
            await _userManager.UpdateAsync(user);
            return false;
        }

        // Verify hashed code
        var result = _passwordHasher.VerifyHashedPassword(user, user.EmailMfaCode, code);
        if (result == PasswordVerificationResult.Failed)
        {
            return false;
        }

        // Clear the code after successful verification
        user.EmailMfaCode = null;
        user.EmailMfaCodeExpiry = null;
        await _userManager.UpdateAsync(user);

        return true;
    }

    public async Task EnableEmailMfaAsync(ApplicationUser user, CancellationToken ct = default)
    {
        user.EmailMfaEnabled = true;
        await _userManager.UpdateAsync(user);
    }

    public async Task DisableEmailMfaAsync(ApplicationUser user, CancellationToken ct = default)
    {
        user.EmailMfaEnabled = false;
        user.EmailMfaCode = null;
        user.EmailMfaCodeExpiry = null;
        await _userManager.UpdateAsync(user);
        
        // Cascading revocation: if policy requires MFA for passkeys and this was the last MFA method
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        if (policy.RequireMfaForPasskey && !user.TwoFactorEnabled)
        {
            await RevokeAllPasskeysAsync(user.Id, ct);
        }
    }
    
    /// <summary>
    /// Revokes all passkeys for the user when MFA is disabled with RequireMfaForPasskey policy.
    /// </summary>
    private async Task RevokeAllPasskeysAsync(Guid userId, CancellationToken ct)
    {
        var passkeys = await _dbContext.UserCredentials
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);
        
        if (passkeys.Any())
        {
            _dbContext.UserCredentials.RemoveRange(passkeys);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogWarning("Revoked {Count} passkeys for user {UserId} due to MFA disable with RequireMfaForPasskey policy", passkeys.Count, userId);
        }
    }

    #endregion

    #region Private Helpers

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    private static string GenerateQrCodeDataUri(string text)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = qrCode.GetGraphic(20);
        return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
    }

    #endregion
}
