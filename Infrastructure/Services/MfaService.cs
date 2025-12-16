using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
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
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    public MfaService(
        UserManager<ApplicationUser> userManager, 
        IBrandingService brandingService,
        IEmailService emailService,
        IPasswordHasher<ApplicationUser> passwordHasher)
    {
        _userManager = userManager;
        _brandingService = brandingService;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
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
    }

    public async Task<IEnumerable<string>> GenerateRecoveryCodesAsync(ApplicationUser user, int count = 10, CancellationToken ct = default)
    {
        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, count);
        return codes ?? Array.Empty<string>();
    }

    public async Task<bool> ValidateRecoveryCodeAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        var result = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
        return result.Succeeded;
    }

    #region Email MFA (Phase 20.3)

    public async Task SendEmailMfaCodeAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.Email))
        {
            throw new InvalidOperationException("User does not have an email address.");
        }

        // Generate 6-digit numeric code
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        // Hash the code before storing
        user.EmailMfaCode = _passwordHasher.HashPassword(user, code);
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(10); // 10-minute expiry

        await _userManager.UpdateAsync(user);

        // Send email via queue
        var subject = "Your verification code";
        var body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <p>Your verification code is:</p>
    <h1 style='font-size: 32px; letter-spacing: 5px; color: #1a73e8;'>{code}</h1>
    <p>This code will expire in 10 minutes.</p>
    <p>If you didn't request this code, please ignore this email.</p>
</body>
</html>";

        await _emailService.SendEmailAsync(user.Email, subject, body, isHtml: true, ct);
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
