using System.Threading;
using System.Threading.Tasks;
using Core.Domain;

namespace Core.Application;

/// <summary>
/// Multi-Factor Authentication service for TOTP and recovery codes.
/// </summary>
public interface IMfaService
{
    /// <summary>
    /// Gets TOTP setup info including the secret key and QR code URI.
    /// </summary>
    Task<MfaSetupInfo> GetTotpSetupInfoAsync(ApplicationUser user, CancellationToken ct = default);
    
    /// <summary>
    /// Verifies the TOTP code and enables MFA for the user if valid.
    /// </summary>
    Task<bool> VerifyAndEnableTotpAsync(ApplicationUser user, string code, CancellationToken ct = default);
    
    /// <summary>
    /// Validates a TOTP code for an existing MFA-enabled user.
    /// </summary>
    Task<bool> ValidateTotpCodeAsync(ApplicationUser user, string code, CancellationToken ct = default);
    
    /// <summary>
    /// Disables MFA for the user.
    /// </summary>
    Task DisableMfaAsync(ApplicationUser user, CancellationToken ct = default);
    
    /// <summary>
    /// Generates new recovery codes for the user.
    /// </summary>
    Task<IEnumerable<string>> GenerateRecoveryCodesAsync(ApplicationUser user, int count = 10, CancellationToken ct = default);
    
    /// <summary>
    /// Validates a recovery code (consumes it if valid).
    /// </summary>
    Task<bool> ValidateRecoveryCodeAsync(ApplicationUser user, string code, CancellationToken ct = default);
}

/// <summary>
/// TOTP setup information returned to the client.
/// </summary>
public record MfaSetupInfo(
    string SharedKey,
    string AuthenticatorUri,
    string QrCodeDataUri
);
