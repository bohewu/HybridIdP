using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    private readonly IEmailMfaAttemptStore _emailMfaAttemptStore;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<MfaService> _logger;
    private readonly TimeProvider _timeProvider;
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";
    private const int EmailMfaCodeLifetimeMinutes = 10;
    private const int EmailMfaCooldownSeconds = 60;
    private const int MaxEmailMfaVerificationAttempts = 5;

    public MfaService(
        UserManager<ApplicationUser> userManager, 
        IBrandingService brandingService,
        IEmailService emailService,
        IEmailTemplateService emailTemplateService,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IDistributedCache cache,
        ISecurityPolicyService securityPolicyService,
        IEmailMfaAttemptStore emailMfaAttemptStore,
        ApplicationDbContext dbContext,
        ILogger<MfaService> logger,
        TimeProvider? timeProvider = null)
    {
        _userManager = userManager;
        _brandingService = brandingService;
        _emailService = emailService;
        _emailTemplateService = emailTemplateService;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _securityPolicyService = securityPolicyService;
        _emailMfaAttemptStore = emailMfaAttemptStore;
        _dbContext = dbContext;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MfaSetupInfo> GetTotpSetupInfoAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user.TwoFactorEnabled) throw new InvalidOperationException("An enabled authenticator cannot be disclosed.");
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
        if (user.TwoFactorEnabled) return false;
        return await ConsumeTotpAsync(user, code, enable: true, ct);
    }

    public Task<bool> ValidateTotpCodeAsync(ApplicationUser user, string code, CancellationToken ct = default) =>
        user.TwoFactorEnabled ? ConsumeTotpAsync(user, code, enable: false, ct) : Task.FromResult(false);

    private async Task<bool> ConsumeTotpAsync(ApplicationUser user, string code, bool enable, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);
        if (!isValid) return false;

        // Identity 10 accepts current-2 .. current+2, not just the clock's current step.
        var matchedWindow = MatchTotpWindow(await _userManager.GetAuthenticatorKeyAsync(user), code);
        if (matchedWindow is null || user.LastTotpValidatedWindow >= matchedWindow) return false;

        var previous = _dbContext.Entry(user).CurrentValues.Clone();
        user.LastTotpValidatedWindow = matchedWindow;
        if (enable) user.TwoFactorEnabled = true;
        return await PersistProofAsync(user, previous, () => _userManager.UpdateAsync(user), ct);
    }

    public Task<MfaRemovalResult> DisableMfaAsync(ApplicationUser user, CancellationToken ct = default) =>
        DisableFactorAsync(user, totp: true, ct);

    public async Task<IEnumerable<string>> GenerateRecoveryCodesAsync(ApplicationUser user, int count = 10, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var codes = new List<string>();
        var hashedCodes = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var code = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
            codes.Add(code);
            hashedCodes.Add(_passwordHasher.HashPassword(user, code));
        }

        var previousRecoveryCodes = user.RecoveryCodes;
        user.RecoveryCodes = System.Text.Json.JsonSerializer.Serialize(hashedCodes);
        try
        {
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException("Recovery codes could not be persisted.");
            }
        }
        catch
        {
            user.RecoveryCodes = previousRecoveryCodes;
            throw;
        }

        return codes;
    }

    public async Task<bool> ValidateRecoveryCodeAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        if ((!user.TwoFactorEnabled && !user.EmailMfaEnabled) || string.IsNullOrEmpty(user.RecoveryCodes))
        {
            return false;
        }

        List<string>? hashedCodes;
        try { hashedCodes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.RecoveryCodes); }
        catch (System.Text.Json.JsonException) { return false; }
        if (hashedCodes == null || hashedCodes.Count == 0)
        {
            return false;
        }

        string? matchedCode = null;
        foreach (var hashedCode in hashedCodes)
        {
            var result = _passwordHasher.VerifyHashedPassword(user, hashedCode, code);
            if (result != PasswordVerificationResult.Failed)
            {
                matchedCode = hashedCode;
                break;
            }
        }

        if (matchedCode != null)
        {
            var previous = _dbContext.Entry(user).CurrentValues.Clone();
            hashedCodes.Remove(matchedCode);
            user.RecoveryCodes = System.Text.Json.JsonSerializer.Serialize(hashedCodes);
            return await PersistProofAsync(user, previous, () => _userManager.UpdateAsync(user), ct);
        }

        return false;
    }

    public Task<bool> ValidateNativeRecoveryCodeAsync(ApplicationUser user, string code, CancellationToken ct = default)
    {
        if (!user.TwoFactorEnabled && !user.EmailMfaEnabled) return Task.FromResult(false);
        // Identity's generated codes contain a hyphen; the existing login form accepts either spelling.
        var normalized = code.Replace(" ", "").Replace("-", "");
        if (normalized.Length == 10) normalized = normalized.Insert(5, "-");
        var previous = _dbContext.Entry(user).CurrentValues.Clone();
        return PersistProofAsync(user, previous,
            () => _userManager.RedeemTwoFactorRecoveryCodeAsync(user, normalized), ct);
    }

    private async Task<bool> PersistProofAsync(ApplicationUser user, PropertyValues previous,
        Func<Task<IdentityResult>> update, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if ((await update()).Succeeded) return true;
        }
        catch (DbUpdateException)
        {
            // Includes optimistic concurrency; neither a cookie nor a success result is allowed.
        }
        catch
        {
            await DiscardFailedProofAsync(user, previous);
            throw;
        }
        await DiscardFailedProofAsync(user, previous);
        return false;
    }

    private async Task DiscardFailedProofAsync(ApplicationUser user, PropertyValues previous)
    {
        // Native redemption mutates an Identity token before UpdateAsync. Never let a later
        // AccessFailedAsync or unrelated SaveChanges flush that unsuccessful consumption.
        foreach (var token in _dbContext.ChangeTracker.Entries<IdentityUserToken<Guid>>()
                     .Where(entry => entry.Entity.UserId == user.Id).ToArray())
            token.State = EntityState.Detached;
        var entry = _dbContext.Entry(user);
        entry.CurrentValues.SetValues(previous);
        if (entry.State == EntityState.Detached) return;
        try { await entry.ReloadAsync(CancellationToken.None); }
        catch { entry.State = EntityState.Detached; throw; }
    }

    private long? MatchTotpWindow(string? key, string token)
    {
        if (string.IsNullOrEmpty(key) || !int.TryParse(token, out var submitted)) return null;
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in key.TrimEnd('=').ToUpperInvariant())
        {
            var digit = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(character);
            if (digit < 0) return null;
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
        }
        if (bytes.Count == 0) return null;
        var keyBytes = bytes.ToArray();
        var current = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        for (var offset = -2; offset <= 2; offset++)
        {
            var step = current + offset;
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
            var hash = HMACSHA1.HashData(keyBytes, counter);
            var index = hash[^1] & 15;
            var value = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(index, 4)) & int.MaxValue;
            if (value % 1_000_000 == submitted) return step;
        }
        return null;
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
        var cacheKey = GetEmailMfaCooldownKey(user.Id);
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

        // Generate a uniformly distributed 6-digit numeric code, including leading zeroes.
        var code = RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6", CultureInfo.InvariantCulture);

        // Hash the code before storing
        user.EmailMfaCode = _passwordHasher.HashPassword(user, code);
        user.EmailMfaCodeExpiry = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(EmailMfaCodeLifetimeMinutes);
        user.EmailMfaVerificationAttempts = 0;

        await _userManager.UpdateAsync(user);

        // Send email via template service
        var (subject, body) = await _emailTemplateService.RenderMfaCodeEmailAsync(
            code,
            EmailMfaCodeLifetimeMinutes,
            user.Locale);
        await _emailService.SendEmailAsync(user.Email, subject, body, isHtml: true, ct);

        // Set Cooldown
        var cooldownExpiry = DateTimeOffset.UtcNow.AddSeconds(EmailMfaCooldownSeconds);
        await _cache.SetStringAsync(
            cacheKey, 
            cooldownExpiry.Ticks.ToString(), 
            new DistributedCacheEntryOptions { AbsoluteExpiration = cooldownExpiry }, 
            ct);
        
        return (true, EmailMfaCooldownSeconds);
    }

    public Task<bool> VerifyEmailMfaCodeAsync(ApplicationUser user, string code, CancellationToken ct = default) =>
        VerifyEmailMfaCodeCoreAsync(user, code, enableEmailMfa: false, ct);

    public Task<bool> VerifyAndEnableEmailMfaAsync(ApplicationUser user, string code, CancellationToken ct = default) =>
        VerifyEmailMfaCodeCoreAsync(user, code, enableEmailMfa: true, ct);

    private async Task<bool> VerifyEmailMfaCodeCoreAsync(
        ApplicationUser user,
        string code,
        bool enableEmailMfa,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(user.EmailMfaCode))
        {
            return false; // No code pending
        }

        if (!user.EmailMfaCodeExpiry.HasValue ||
            user.EmailMfaCodeExpiry.Value <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            // Code expired, clear it
            user.EmailMfaCode = null;
            user.EmailMfaCodeExpiry = null;
            user.EmailMfaVerificationAttempts = 0;
            await _userManager.UpdateAsync(user);
            return false;
        }

        var pendingCodeHash = user.EmailMfaCode;
        var reservation = await _emailMfaAttemptStore.TryReserveAttemptAsync(
            user.Id,
            pendingCodeHash,
            _timeProvider.GetUtcNow().UtcDateTime,
            MaxEmailMfaVerificationAttempts,
            ct);
        if (reservation == EmailMfaAttemptReservation.Rejected || user.EmailMfaCode != pendingCodeHash ||
            !user.EmailMfaCodeExpiry.HasValue || user.EmailMfaCodeExpiry.Value <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            return false;
        }

        // Verify hashed code
        var result = _passwordHasher.VerifyHashedPassword(user, pendingCodeHash, code);
        if (result == PasswordVerificationResult.Failed)
        {
            if (reservation == EmailMfaAttemptReservation.FinalAttempt)
            {
                await _emailMfaAttemptStore.InvalidatePendingCodeAsync(
                    user.Id,
                    pendingCodeHash,
                    ct);
            }

            return false;
        }

        var previousCode = user.EmailMfaCode;
        var previousExpiry = user.EmailMfaCodeExpiry;
        var previousAttempts = user.EmailMfaVerificationAttempts;
        var wasEmailMfaEnabled = user.EmailMfaEnabled;

        // Consume the proof and, for enrollment, persist enablement in the same update.
        user.EmailMfaCode = null;
        user.EmailMfaCodeExpiry = null;
        user.EmailMfaVerificationAttempts = 0;
        if (enableEmailMfa)
        {
            user.EmailMfaEnabled = true;
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (updateResult.Succeeded)
        {
            await ClearEmailMfaCooldownAsync(user.Id, ct);
            return true;
        }

        user.EmailMfaCode = previousCode;
        user.EmailMfaCodeExpiry = previousExpiry;
        user.EmailMfaVerificationAttempts = previousAttempts;
        user.EmailMfaEnabled = wasEmailMfaEnabled;
        return false;
    }

    private async Task ClearEmailMfaCooldownAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync(GetEmailMfaCooldownKey(userId), ct);
        }
        catch (Exception exception)
        {
            // The proof has already been consumed. A cache outage must not turn a
            // successful MFA verification into a failed authentication attempt.
            _logger.LogWarning(
                exception,
                "Could not clear the email MFA send cooldown for user {UserId}",
                userId);
        }
    }

    public Task<MfaRemovalResult> DisableEmailMfaAsync(ApplicationUser user, CancellationToken ct = default) =>
        DisableFactorAsync(user, totp: false, ct);

    private async Task<MfaRemovalResult> DisableFactorAsync(ApplicationUser user, bool totp, CancellationToken ct)
    {
        var policy = await _securityPolicyService.GetCurrentPolicyAsync();
        var passkeys = await _dbContext.UserCredentials
            .Where(c => c.UserId == user.Id && c.DisabledAtUtc == null)
            .ToListAsync(ct);
        var remainingTotp = !totp && user.TwoFactorEnabled;
        var remainingEmail = totp && user.EmailMfaEnabled;
        var retirePasskeys = policy.RequireMfaForPasskey && !remainingTotp && !remainingEmail;
        if (policy.EnforceMandatoryMfaEnrollment &&
            !(policy.EnableTotpMfa && remainingTotp) && !(policy.EnableEmailMfa && remainingEmail) &&
            !(policy.EnablePasskey && !retirePasskeys && passkeys.Count > 0))
            return MfaRemovalResult.MandatoryFactorRequired;

        var previous = _dbContext.Entry(user).CurrentValues.Clone();
        if (totp)
        {
            user.TwoFactorEnabled = false;
            user.LastTotpValidatedWindow = null;
            user.RecoveryCodes = null;
            var nativeCodes = await _dbContext.UserTokens.Where(token => token.UserId == user.Id &&
                token.LoginProvider == "[AspNetUserStore]" && token.Name == "RecoveryCodes").ToListAsync(ct);
            _dbContext.UserTokens.RemoveRange(nativeCodes);
        }
        else
        {
            user.EmailMfaEnabled = false;
            user.EmailMfaCode = null;
            user.EmailMfaCodeExpiry = null;
            user.EmailMfaVerificationAttempts = 0;
            if (!user.TwoFactorEnabled) user.RecoveryCodes = null;
        }
        user.SecurityStamp = Guid.NewGuid().ToString();
        if (retirePasskeys)
            foreach (var passkey in passkeys) passkey.DisabledAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // ResetAuthenticatorKey persists the reset, user state and staged retirements together.
        // Both factor types compete on the user's Identity concurrency stamp.
        var persisted = false;
        try
        {
            persisted = await PersistProofAsync(user, previous,
                () => totp ? _userManager.ResetAuthenticatorKeyAsync(user) : _userManager.UpdateAsync(user), ct);
            if (persisted) return MfaRemovalResult.Succeeded;
        }
        finally
        {
            if (!persisted)
                foreach (var passkey in passkeys) await _dbContext.Entry(passkey).ReloadAsync(CancellationToken.None);
        }
        return MfaRemovalResult.PersistenceFailed;
    }

    #endregion

    #region Private Helpers

    private static string GetEmailMfaCooldownKey(Guid userId) => $"EmailMfa_Cooldown_{userId}";

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
