using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Web.IdP.Helpers;

// A performed verification for one account-security operation, never an old cookie's AMR.
public static class AccountSecurityOperationSession
{
    private const string SessionKey = "account-security.operation";
    public const string ExternalLinkPurpose = "external-login-link";
    public const string MfaResetPurpose = "administrative-mfa-reset";
    public const string MfaEnrollmentPurpose = "mfa-enrollment";
    public const string CorrelationProperty = "account-security.operation-nonce";

    public static async Task<string?> BeginAsync(HttpContext http, ApplicationUser user,
        string purpose, string target, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.SecurityStamp) || !user.IsActive || user.IsDeleted || user.RequiresPasswordChange)
            return null;
        var credentials = await http.RequestServices.GetRequiredService<IApplicationDbContext>().UserCredentials
            .Where(credential => credential.UserId == user.Id && credential.DisabledAtUtc == null)
            .Select(credential => credential.CredentialId).ToListAsync(ct);
        var hasFactors = user.TwoFactorEnabled || user.EmailMfaEnabled || credentials.Count > 0;
        if (purpose == MfaResetPurpose && !hasFactors) return null;
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var state = new Operation(user.Id, user.SecurityStamp, purpose, target, nonce,
            DateTimeOffset.UtcNow.AddMinutes(5), user.TwoFactorEnabled, user.EmailMfaEnabled, user.Email,
            credentials.Select(WebEncoders.Base64UrlEncode).ToArray(),
            purpose == MfaResetPurpose || hasFactors, false);
        await http.Session.LoadAsync(ct);
        PendingExternalLoginLink.Cancel(http);
        http.Session.SetString(SessionKey, JsonSerializer.Serialize(state));
        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        await http.SignOutAsync(IdentityConstants.TwoFactorRememberMeScheme);
        return nonce;
    }

    public static async Task<string> GetLoginUrlAsync(HttpContext http, ApplicationUser user, string returnUrl)
    {
        var state = Read(http);
        if (state?.UserId == user.Id && (state.Totp || state.EmailMfa || state.Passkeys.Length > 0))
        {
            var identity = TwoFactorAuthenticationSession.CreateIdentity(user,
                http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>());
            await http.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity));
            AuthenticationMethodSession.Replace(http.Session, user);
            return QueryHelpers.AddQueryString("/Account/LoginMfa", "returnUrl", returnUrl);
        }
        return QueryHelpers.AddQueryString("/Account/Login", "returnUrl", returnUrl);
    }

    // Call only at the successful password/existing-factor verification sites, never enrollment or cookie refresh.
    public static void MarkVerified(HttpContext http, ApplicationUser user, string method, string? credentialId = null)
    {
        var state = Read(http);
        if (state == null || state.UserId != user.Id || state.Stamp != user.SecurityStamp) return;
        var accepted = method switch
        {
            "password" => !state.RequireMfa,
            "totp" => state.Totp && user.TwoFactorEnabled,
            "email" => state.EmailMfa && user.EmailMfaEnabled && state.Email == user.Email,
            "recovery" => state.Totp && user.TwoFactorEnabled || state.EmailMfa && user.EmailMfaEnabled,
            "passkey" => credentialId != null && state.Passkeys.Contains(credentialId, StringComparer.Ordinal),
            _ => false
        };
        if (accepted) http.Session.SetString(SessionKey, JsonSerializer.Serialize(state with { Verified = true }));
    }

    public static async Task<bool> IsAuthorizedAsync(HttpContext http, ApplicationUser user,
        string purpose, string target, string? nonce = null)
    {
        var state = Read(http);
        if (state == null || !state.Verified || state.UserId != user.Id || state.Stamp != user.SecurityStamp ||
            state.Purpose != purpose || state.Target != target || nonce != null && state.Nonce != nonce ||
            !user.IsActive || user.IsDeleted || user.RequiresPasswordChange) return false;
        var cookie = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return cookie.Succeeded && cookie.Principal?.Identity?.IsAuthenticated == true &&
            cookie.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString() &&
            cookie.Principal.FindFirstValue(http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>()
                .Options.ClaimsIdentity.SecurityStampClaimType) == user.SecurityStamp &&
            (!state.RequireMfa || MfaEnrollmentSession.HasMfa(cookie.Principal));
    }

    public static bool IsVerifiedEnrollment(HttpContext http, ClaimsPrincipal principal)
    {
        var state = Read(http);
        return state is { Verified: true, Purpose: MfaEnrollmentPurpose } &&
            state.Target == state.UserId.ToString() && principal.Identity?.IsAuthenticated == true &&
            principal.FindFirstValue(ClaimTypes.NameIdentifier) == state.UserId.ToString() &&
            principal.FindFirstValue(http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>()
                .Options.ClaimsIdentity.SecurityStampClaimType) == state.Stamp &&
            (!state.RequireMfa || MfaEnrollmentSession.HasMfa(principal));
    }

    public static string? GetNonce(HttpContext http, string purpose, string target)
    {
        var state = Read(http);
        return state?.Purpose == purpose && state.Target == target ? state.Nonce : null;
    }

    public static void Consume(HttpContext http) => http.Features.Get<ISessionFeature>()?.Session.Remove(SessionKey);

    private static Operation? Read(HttpContext http)
    {
        var json = http.Features.Get<ISessionFeature>()?.Session.GetString(SessionKey);
        if (json == null) return null;
        try
        {
            var state = JsonSerializer.Deserialize<Operation>(json);
            return state?.ExpiresUtc > DateTimeOffset.UtcNow ? state : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed record Operation(Guid UserId, string Stamp, string Purpose, string Target, string Nonce,
        DateTimeOffset ExpiresUtc, bool Totp, bool EmailMfa, string? Email, string[] Passkeys,
        bool RequireMfa, bool Verified);
}
