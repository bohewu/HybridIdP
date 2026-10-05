using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Constants;

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

    public static void Begin(ISession session, Guid userId, bool requiresMfa = false, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        session.Remove(ProofKey);
        session.Remove(InitialKey);
        session.SetString(
            PendingKey,
            JsonSerializer.Serialize(new PendingEnrollment(userId, requiresMfa, now.Add(Lifetime))));
    }

    public static Claim BeginInitial(ISession session, Guid userId, TimeProvider? timeProvider = null)
    {
        Consume(session);
        var nonce = Guid.NewGuid().ToString("N");
        session.SetString(InitialKey, JsonSerializer.Serialize(new InitialEnrollment(
            userId, nonce, (timeProvider ?? TimeProvider.System).GetUtcNow().Add(Lifetime))));
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

    public static bool CompletePending(
        ISession session,
        ClaimsPrincipal principal,
        TimeProvider? timeProvider = null)
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
        if (!Guid.TryParse(userIdValue, out var userId) || userId != pending.UserId ||
            principal.Identity?.IsAuthenticated != true || (pending.RequiresMfa && !HasMfa(principal)))
        {
            session.Remove(PendingKey);
            return false;
        }

        session.SetString(
            ProofKey,
            JsonSerializer.Serialize(new EnrollmentProof(userId, now.Add(Lifetime))));
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

    public static async Task<bool> IsAuthorizedAsync(
        HttpContext context, ApplicationUser user, IPasskeyService passkeys,
        CancellationToken cancellationToken = default, bool requireFreshProof = true)
    {
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
                (!requireFreshProof || HasFreshProof(httpContext.Session, userId, timeProvider) ||
                 (!hasExistingFactor && HasInitial(httpContext.Session, userId, timeProvider: timeProvider)));
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

    private sealed record PendingEnrollment(Guid UserId, bool RequiresMfa, DateTimeOffset ExpiresUtc);

    private sealed record InitialEnrollment(Guid UserId, string Nonce, DateTimeOffset ExpiresUtc);

    private sealed record EnrollmentProof(Guid UserId, DateTimeOffset ExpiresUtc);
}
