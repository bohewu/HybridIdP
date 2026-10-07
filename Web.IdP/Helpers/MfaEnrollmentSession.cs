using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Constants;
using Core.Application;
using Microsoft.AspNetCore.Http.Features;
using OpenIddict.Validation.AspNetCore;
using Web.IdP.Services;

namespace Web.IdP.Helpers;

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
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static void Begin(ISession session, Guid userId, bool requiresMfa = false, TimeProvider? timeProvider = null,
        string? securityStamp = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        PendingExternalLoginLink.Cancel(session);
        session.Remove(ProofKey);
        session.Remove(InitialKey);
        session.SetString(
            PendingKey,
            JsonSerializer.Serialize(new PendingEnrollment(userId, requiresMfa, now.Add(Lifetime), securityStamp)));
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
                pending.RequiresMfa && HasMfa(principal))));
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
        string? previousStamp, CancellationToken ct)
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
            var principal = authentication.Principal.Clone();
            var identity = (ClaimsIdentity)principal.Identity!;
            foreach (var claim in identity.FindAll(stampType).ToArray()) identity.RemoveClaim(claim);
            identity.AddClaim(new Claim(stampType, user.SecurityStamp));
            await context.SignInAsync(scheme, principal, authentication.Properties);
            return true;
        }
        return false;
    }

    public static async Task<bool> IsRemovalAuthorizedAsync(HttpContext context, ApplicationUser user,
        CancellationToken cancellationToken = default, TimeProvider? timeProvider = null)
    {
        var session = context.Features.Get<ISessionFeature>()?.Session;
        var proof = session == null ? null : Read<EnrollmentProof>(session, ProofKey);
        if (proof == null || !proof.MfaCompleted || proof.UserId != user.Id ||
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
            (await passkeys.GetUserPasskeysAsync(user.Id, cancellationToken)).Count > 0;
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

    private sealed record PendingEnrollment(Guid UserId, bool RequiresMfa, DateTimeOffset ExpiresUtc, string? SecurityStamp);

    private sealed record InitialEnrollment(Guid UserId, string Nonce, DateTimeOffset ExpiresUtc, string? SecurityStamp);

    private sealed record EnrollmentProof(Guid UserId, DateTimeOffset ExpiresUtc, string? SecurityStamp, bool MfaCompleted);
}
