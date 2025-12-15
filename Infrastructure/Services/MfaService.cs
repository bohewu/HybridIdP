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
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    public MfaService(UserManager<ApplicationUser> userManager, IBrandingService brandingService)
    {
        _userManager = userManager;
        _brandingService = brandingService;
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
        return await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);
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
