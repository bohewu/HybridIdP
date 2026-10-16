using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Constants;
using Core.Application;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Helpers;

public enum MfaEnrollmentAction
{
    None,
    AddAuthenticator,
    EnableEmailOtp,
    RegisterPasskey,
    RemoveAuthenticator,
    DisableEmailOtp,
    DeletePasskey,
    RegenerateRecoveryCodes,
    RegenerateAccountRecoveryCodes,
    RecoverVerificationMethod
}

/// <summary>
/// Tracks a short-lived, user-bound proof that an interactive reauthentication
/// was completed specifically for MFA enrollment.
/// </summary>
public static class MfaEnrollmentSession
{
    private const string PendingKey = "MfaEnrollment:Pending";
    private const string ProofKey = "MfaEnrollment:Proof";
    private const string InitialKey = "MfaEnrollment:Initial";
    public const string InitialPurposeClaim = "mfa_enrollment";
    public const string RecoveryEnrollmentPurpose = "account-recovery-enrollment";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // Called only at the successful password boundary, before pending methods are
    // consumed. An application cookie's AMR or authentication time cannot mint this.
    public static async Task<bool> TryBeginInitialAfterPasswordAsync(HttpContext http, ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        if (!AuthenticationMethodSession.HasFreshPassword(http.Session, user) ||
            HasPending(http.Session) || HasFreshProof(http.Session, user.Id) ||
            !await HasNoFactorHistoryAsync(http, user, cancellationToken)) return false;

        var authenticatedAt = AuthenticationMethodSession.GetAuthenticationTime(http.Session, user);
        if (authenticatedAt == null) return false;
        http.Session.SetString(InitialKey, JsonSerializer.Serialize(new InitialEnrollment(user.Id,
            Guid.NewGuid().ToString("N"), authenticatedAt.Value.Add(Lifetime), user.SecurityStamp,
            ApplicationContinuation: true)));
        return true;
    }

    public static async Task<bool> TryContinueInitialAfterPasswordAsync(HttpContext http, ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        var initial = Read<InitialEnrollment>(http.Session, InitialKey);
        if (initial is not { ApplicationContinuation: true }) return false;
        if (initial.UserId != user.Id || string.IsNullOrEmpty(user.SecurityStamp) ||
            initial.SecurityStamp != user.SecurityStamp || initial.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            http.Session.Remove(InitialKey);
            return false;
        }
        var cookie = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var stampType = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>()
            .Options.ClaimsIdentity.SecurityStampClaimType;
        if (!cookie.Succeeded || !PrincipalMatchesUser(cookie.Principal, user.Id) ||
            cookie.Principal!.FindFirstValue(stampType) != user.SecurityStamp ||
            !await HasNoFactorHistoryAsync(http, user, cancellationToken))
        {
            http.Session.Remove(InitialKey);
            return false;
        }

        // Transfer the bounded continuation once, retaining the original expiry.
        // Existing successful setup endpoints consume this enrollment-only proof.
        Consume(http.Session);
        http.Session.SetString(ProofKey, JsonSerializer.Serialize(new EnrollmentProof(user.Id,
            initial.ExpiresUtc, user.SecurityStamp, MfaCompleted: false, EnrollmentOnly: true)));
        return true;
    }

    private static async Task<bool> HasNoFactorHistoryAsync(HttpContext http, ApplicationUser user,
        CancellationToken cancellationToken)
    {
        if (!user.IsActive || user.IsDeleted || user.RequiresPasswordChange ||
            string.IsNullOrEmpty(user.SecurityStamp) || user.TwoFactorEnabled || user.EmailMfaEnabled ||
            user.RecoveryCodes != null) return false;
        var codes = await http.RequestServices.GetRequiredService<IAccountRecoveryCodeService>()
            .GetStatusAsync(user.Id, cancellationToken);
        if (codes.HasBatch || codes.HasLegacyBatch || codes.TotalRemainingCodes > 0) return false;
        // Retired passkeys are history too. Missing persistence cannot prove a first enrollment.
        var db = http.RequestServices.GetService<IApplicationDbContext>();
        return db != null && !await db.UserCredentials.AsNoTracking()
            .AnyAsync(value => value.UserId == user.Id, cancellationToken);
    }

    public static bool TryBeginRecoveryEnrollment(HttpContext http, ApplicationUser user, TimeProvider? timeProvider = null)
    {
        // The scoped receipt exists only after durable consumption for this exact purpose.
        // Neither an MFA cookie nor a caller-supplied method can create this continuation.
        if (http.Items.ContainsKey(RecoveryEnrollmentPurpose) || string.IsNullOrEmpty(user.SecurityStamp) ||
            !http.RequestServices.GetRequiredService<IAccountRecoveryCodeService>()
                .HasRedemptionProof(user, RecoveryEnrollmentPurpose)) return false;

        http.Items[RecoveryEnrollmentPurpose] = true;
        Consume(http.Session);
        http.Session.SetString(ProofKey, JsonSerializer.Serialize(new EnrollmentProof(user.Id,
            (timeProvider ?? TimeProvider.System).GetUtcNow().Add(Lifetime), user.SecurityStamp,
            true, MfaEnrollmentAction.RecoverVerificationMethod, EnrollmentOnly: true)));
        return true;
    }

    public static void Begin(ISession session, Guid userId, bool requiresMfa = false, TimeProvider? timeProvider = null,
        string? securityStamp = null, MfaEnrollmentAction action = MfaEnrollmentAction.None)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        PendingExternalLoginLink.Cancel(session);
        session.Remove(ProofKey);
        session.Remove(InitialKey);
        session.SetString(
            PendingKey,
            JsonSerializer.Serialize(new PendingEnrollment(userId, requiresMfa, now.Add(Lifetime), securityStamp, action)));
    }

    public static bool TryParseAction(string? value, out MfaEnrollmentAction action)
    {
        action = value switch
        {
            "totp-add" => MfaEnrollmentAction.AddAuthenticator,
            "email-enable" => MfaEnrollmentAction.EnableEmailOtp,
            "passkey-register" => MfaEnrollmentAction.RegisterPasskey,
            "totp-disable" => MfaEnrollmentAction.RemoveAuthenticator,
            "email-disable" => MfaEnrollmentAction.DisableEmailOtp,
            "passkey-delete" => MfaEnrollmentAction.DeletePasskey,
            "account-recovery-codes" => MfaEnrollmentAction.RegenerateAccountRecoveryCodes,
            "recovery-codes" => MfaEnrollmentAction.RegenerateRecoveryCodes,
            _ => MfaEnrollmentAction.None
        };
        return value == null || action != MfaEnrollmentAction.None;
    }

    public static string GetPendingNoticeKey(ISession session, TimeProvider? timeProvider = null)
    {
        var pending = HasPending(session, timeProvider) ? Read<PendingEnrollment>(session, PendingKey) : null;
        return pending?.Action switch
        {
            MfaEnrollmentAction.AddAuthenticator => "MfaReauthenticationAddAuthenticatorNotice",
            MfaEnrollmentAction.EnableEmailOtp => "MfaReauthenticationEnableEmailOtpNotice",
            MfaEnrollmentAction.RegisterPasskey => "MfaReauthenticationRegisterPasskeyNotice",
            MfaEnrollmentAction.RemoveAuthenticator => "MfaReauthenticationRemoveAuthenticatorNotice",
            MfaEnrollmentAction.DisableEmailOtp => "MfaReauthenticationDisableEmailOtpNotice",
            MfaEnrollmentAction.DeletePasskey => "MfaReauthenticationDeletePasskeyNotice",
            MfaEnrollmentAction.RegenerateAccountRecoveryCodes => "MfaReauthenticationAccountRecoveryCodesNotice",
            MfaEnrollmentAction.RegenerateRecoveryCodes => "MfaReauthenticationRecoveryCodesNotice",
            _ => "MfaEnrollmentReauthenticationNotice"
        };
    }

    // Navigation only. The caller must authorize the current account before taking it.
    public static string? TakeSetupMethod(ISession session, ApplicationUser user, TimeProvider? timeProvider = null)
    {
        var proof = Read<EnrollmentProof>(session, ProofKey);
        if (proof == null || proof.UserId != user.Id || string.IsNullOrEmpty(user.SecurityStamp) ||
            proof.SecurityStamp != user.SecurityStamp ||
            proof.ExpiresUtc <= (timeProvider ?? TimeProvider.System).GetUtcNow() ||
            proof.Action != MfaEnrollmentAction.AddAuthenticator) return null;

        session.SetString(ProofKey, JsonSerializer.Serialize(proof with { Action = MfaEnrollmentAction.None }));
        return "totp";
    }

    public static Claim BeginInitial(ISession session, Guid userId, TimeProvider? timeProvider = null,
        string? securityStamp = null)
    {
        Consume(session);
        var nonce = Guid.NewGuid().ToString("N");
        session.SetString(InitialKey, JsonSerializer.Serialize(new InitialEnrollment(
            userId, nonce, (timeProvider ?? TimeProvider.System).GetUtcNow().Add(Lifetime), securityStamp)));
        return new Claim(InitialPurposeClaim, nonce);
    }

    public static bool HasInitial(ISession session, Guid userId, string? nonce = null, TimeProvider? timeProvider = null)
    {
        var initial = Read<InitialEnrollment>(session, InitialKey);
        if (initial == null || initial.ExpiresUtc <= (timeProvider ?? TimeProvider.System).GetUtcNow())
        {
            session.Remove(InitialKey);
            return false;
        }
        return initial.UserId == userId && (nonce == null || initial.Nonce == nonce);
    }

    public static bool HasMfa(ClaimsPrincipal principal) => principal.Claims.Any(claim =>
        (claim.Type == AuthConstants.ClaimTypes.Amr || claim.Type == ClaimTypes.AuthenticationMethod) &&
        claim.Value.Equals(AuthConstants.Amr.Mfa, StringComparison.OrdinalIgnoreCase));

    public static bool HasPending(
        ISession session,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var pending = Read<PendingEnrollment>(session, PendingKey);
        if (pending == null || pending.ExpiresUtc <= now)
        {
            session.Remove(PendingKey);
            return false;
        }

        return true;
    }

    public static bool CompletePending(HttpContext http, ClaimsPrincipal principal)
    {
        if (!AccountSecurityOperationSession.IsVerifiedEnrollment(http, principal)) return false;
        var completed = CompletePending(http.Session, principal, securityStampClaimType:
            http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>().Options.ClaimsIdentity.SecurityStampClaimType);
        if (completed) AccountSecurityOperationSession.Consume(http);
        return completed;
    }

    public static bool CompletePending(
        ISession session,
        ClaimsPrincipal principal,
        TimeProvider? timeProvider = null, string? securityStampClaimType = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(principal);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var pending = Read<PendingEnrollment>(session, PendingKey);
        if (pending == null || pending.ExpiresUtc <= now)
        {
            session.Remove(PendingKey);
            return false;
        }

        var userIdValue =
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            principal.FindFirst("sub")?.Value;
        var stamp = principal.FindFirst(securityStampClaimType ?? new IdentityOptions().ClaimsIdentity.SecurityStampClaimType)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId) || userId != pending.UserId ||
            (pending.SecurityStamp != null && pending.SecurityStamp != stamp) ||
            principal.Identity?.IsAuthenticated != true || (pending.RequiresMfa && !HasMfa(principal)))
        {
            session.Remove(PendingKey);
            return false;
        }

        session.SetString(
            ProofKey,
            JsonSerializer.Serialize(new EnrollmentProof(userId, now.Add(Lifetime), stamp,
                pending.RequiresMfa && HasMfa(principal), pending.Action)));
        session.Remove(PendingKey);
        return true;
    }

    public static bool HasFreshProof(
        ISession session,
        Guid userId,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var proof = Read<EnrollmentProof>(session, ProofKey);
        if (proof == null || proof.ExpiresUtc <= now)
        {
            session.Remove(ProofKey);
            return false;
        }

        return proof.UserId == userId;
    }

    public static void Consume(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Remove(ProofKey);
        session.Remove(InitialKey);
        session.Remove(PendingKey);
    }

    public static async Task<bool> CarryAuthorizedStampAsync(HttpContext context, ApplicationUser user,
        string? previousStamp, CancellationToken ct, bool retireMfaAssurance = false)
    {
        if (user.SecurityStamp == previousStamp) return true;
        if (string.IsNullOrEmpty(previousStamp) || string.IsNullOrEmpty(user.SecurityStamp) ||
            !await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(
                    context.RequestServices.GetRequiredService<IApplicationDbContext>().Users),
                value => value.Id == user.Id && value.SecurityStamp == user.SecurityStamp, ct)) return false;
        var users = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var stampType = users.Options.ClaimsIdentity.SecurityStampClaimType;
        foreach (var scheme in new[] { IdentityConstants.ApplicationScheme, IdentityConstants.TwoFactorUserIdScheme })
        {
            var authentication = await context.AuthenticateAsync(scheme);
            if (!authentication.Succeeded || !PrincipalMatchesUser(authentication.Principal, user.Id) ||
                authentication.Principal!.FindFirstValue(stampType) != previousStamp) continue;
            if (scheme == IdentityConstants.ApplicationScheme)
            {
                var proof = Read<EnrollmentProof>(context.Session, ProofKey);
                if (proof == null || proof.SecurityStamp != previousStamp || !HasFreshProof(context.Session, user.Id)) return false;
                context.Session.SetString(ProofKey, JsonSerializer.Serialize(proof with { SecurityStamp = user.SecurityStamp }));
            }
            else
            {
                var initial = Read<InitialEnrollment>(context.Session, InitialKey);
                if (initial?.SecurityStamp != previousStamp || !HasInitial(context.Session, user.Id,
                        authentication.Principal.FindFirstValue(InitialPurposeClaim))) return false;
                context.Session.SetString(InitialKey, JsonSerializer.Serialize(initial with { SecurityStamp = user.SecurityStamp }));
            }
            var principal = new ClaimsPrincipal(authentication.Principal.Identities.Select(identity => identity.Clone()));
            var identity = (ClaimsIdentity)principal.Identity!;
            foreach (var claim in identity.FindAll(stampType).ToArray()) identity.RemoveClaim(claim);
            identity.AddClaim(new Claim(stampType, user.SecurityStamp));
            if (retireMfaAssurance)
            {
                AuthorizationAuthenticationSession.ClearMfa(principal);
                AuthenticationMethodSession.Consume(context.Session);
                AccountSecurityOperationSession.Consume(context);
                Consume(context.Session);
            }
            else AuthenticationMethodSession.CarryAuthorizedStamp(context.Session, user, previousStamp);
            if (scheme == IdentityConstants.ApplicationScheme)
            {
                var assurance = authentication.Principal.Clone();
                if (retireMfaAssurance) AuthorizationAuthenticationSession.ClearMfa(assurance);
                AuthorizationAuthenticationSession.PreserveAssurance(context, assurance);
            }
            await context.SignInAsync(scheme, principal, authentication.Properties);
            if (scheme == IdentityConstants.ApplicationScheme)
                await context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            return true;
        }
        return false;
    }

    public static async Task<bool> IsRemovalAuthorizedAsync(HttpContext context, ApplicationUser user,
        CancellationToken cancellationToken = default, TimeProvider? timeProvider = null)
    {
        var session = context.Features.Get<ISessionFeature>()?.Session;
        var proof = session == null ? null : Read<EnrollmentProof>(session, ProofKey);
        if (proof == null || !proof.MfaCompleted || proof.EnrollmentOnly || proof.UserId != user.Id ||
            string.IsNullOrEmpty(user.SecurityStamp) || proof.SecurityStamp != user.SecurityStamp ||
            proof.ExpiresUtc <= (timeProvider ?? TimeProvider.System).GetUtcNow() ||
            user.RequiresPasswordChange || !user.IsActive || user.IsDeleted) return false;

        // Authenticate each participating scheme; an aggregate principal must not borrow
        // another subject's MFA. Bearers use the server proof, never a required auth_time claim.
        var authenticated = false;
        foreach (var scheme in new[] { IdentityConstants.ApplicationScheme,
                     OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme })
        {
            var authentication = await context.AuthenticateAsync(scheme);
            if (!authentication.Succeeded) continue;
            if (!PrincipalMatchesUser(authentication.Principal, user.Id) || !HasMfa(authentication.Principal!))
                return false;
            authenticated = true;
        }
        if (!authenticated) return false;
        return await context.RequestServices.GetRequiredService<ICurrentUserLifecycleEligibility>()
                   .IsEligibleAsync(user.Id, cancellationToken) &&
               await context.RequestServices.GetRequiredService<IMigrationIssuanceGuard>()
                   .CanIssueAsync(user.Id, cancellationToken);
    }

    public static async Task<bool> IsAuthorizedAsync(
        HttpContext context, ApplicationUser user, IPasskeyService passkeys,
        CancellationToken cancellationToken = default, bool requireFreshProof = true, bool requireCurrentStamp = true)
    {
        var enrollment = Read<EnrollmentProof>(context.Session, ProofKey);
        if (enrollment is { EnrollmentOnly: true, MfaCompleted: false } &&
            !await HasNoFactorHistoryAsync(context, user, cancellationToken)) return false;
        var application = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var partial = await context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (!PrincipalMatchesUser(application.Principal, user.Id) && PrincipalMatchesUser(partial.Principal, user.Id))
        {
            var stampType = context.RequestServices.GetService<UserManager<ApplicationUser>>()?.Options
                .ClaimsIdentity.SecurityStampClaimType ?? new IdentityOptions().ClaimsIdentity.SecurityStampClaimType;
            var initial = Read<InitialEnrollment>(context.Session, InitialKey);
            if (string.IsNullOrEmpty(user.SecurityStamp) || initial?.SecurityStamp != user.SecurityStamp ||
                partial.Principal!.FindFirstValue(stampType) != user.SecurityStamp) return false;
        }
        if (requireCurrentStamp)
        {
            var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (PrincipalMatchesUser(cookie.Principal, user.Id))
            {
                var stampType = context.RequestServices.GetService<UserManager<ApplicationUser>>()?.Options
                    .ClaimsIdentity.SecurityStampClaimType ?? new IdentityOptions().ClaimsIdentity.SecurityStampClaimType;
                var proof = Read<EnrollmentProof>(context.Session, ProofKey);
                if (string.IsNullOrEmpty(user.SecurityStamp) || (requireFreshProof && proof?.SecurityStamp != user.SecurityStamp) ||
                    cookie.Principal!.FindFirstValue(stampType) != user.SecurityStamp) return false;
            }
        }
        var hasFactors = user.TwoFactorEnabled || user.EmailMfaEnabled ||
            (await passkeys.GetUserPasskeysAsync(user.Id, cancellationToken)).Count > 0 ||
            (await context.RequestServices.GetRequiredService<IAccountRecoveryCodeService>()
                .GetStatusAsync(user.Id, cancellationToken)).RemainingCodes > 0;
        return await IsAuthorizedAsync(context, user.Id, hasExistingFactor: hasFactors,
            requireFreshProof: requireFreshProof);
    }

    public static async Task<bool> IsAuthorizedAsync(
        HttpContext httpContext,
        Guid userId,
        TimeProvider? timeProvider = null,
        bool hasExistingFactor = false,
        bool requireFreshProof = true)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var applicationAuthentication =
            await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (PrincipalMatchesUser(applicationAuthentication.Principal, userId))
        {
            return (!hasExistingFactor || HasMfa(applicationAuthentication.Principal!)) &&
                (!requireFreshProof || HasFreshProof(httpContext.Session, userId, timeProvider));
        }

        var partialAuthentication =
            await httpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (PrincipalMatchesUser(partialAuthentication.Principal, userId))
        {
            var nonce = partialAuthentication.Principal!.FindFirst(InitialPurposeClaim)?.Value;
            return !hasExistingFactor && nonce != null &&
                HasInitial(httpContext.Session, userId, nonce, timeProvider);
        }

        return false;
    }

    private static bool PrincipalMatchesUser(ClaimsPrincipal? principal, Guid userId)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var subject =
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            principal.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out var authenticatedUserId) &&
               authenticatedUserId == userId;
    }

    private static T? Read<T>(ISession session, string key)
    {
        var value = session.GetString(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch (JsonException)
        {
            session.Remove(key);
            return default;
        }
    }

    private sealed record PendingEnrollment(Guid UserId, bool RequiresMfa, DateTimeOffset ExpiresUtc, string? SecurityStamp,
        MfaEnrollmentAction Action = MfaEnrollmentAction.None);

    private sealed record InitialEnrollment(Guid UserId, string Nonce, DateTimeOffset ExpiresUtc, string? SecurityStamp,
        bool ApplicationContinuation = false);

    private sealed record EnrollmentProof(Guid UserId, DateTimeOffset ExpiresUtc, string? SecurityStamp, bool MfaCompleted,
        MfaEnrollmentAction Action = MfaEnrollmentAction.None, bool EnrollmentOnly = false);
}
