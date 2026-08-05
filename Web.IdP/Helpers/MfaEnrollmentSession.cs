using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Helpers;

/// <summary>
/// Tracks a short-lived, user-bound proof that an interactive reauthentication
/// was completed specifically for MFA enrollment.
/// </summary>
public static class MfaEnrollmentSession
{
    private const string PendingKey = "MfaEnrollment:Pending";
    private const string ProofKey = "MfaEnrollment:Proof";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static void Begin(ISession session, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        session.Remove(ProofKey);
        session.SetString(
            PendingKey,
            JsonSerializer.Serialize(new PendingEnrollment(now.Add(Lifetime))));
    }

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
        if (!Guid.TryParse(userIdValue, out var userId))
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
    }

    public static async Task<bool> IsAuthorizedAsync(
        HttpContext httpContext,
        Guid userId,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var partialAuthentication =
            await httpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        if (PrincipalMatchesUser(partialAuthentication.Principal, userId))
        {
            return true;
        }

        var applicationAuthentication =
            await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return PrincipalMatchesUser(applicationAuthentication.Principal, userId) &&
               HasFreshProof(httpContext.Session, userId, timeProvider);
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

    private sealed record PendingEnrollment(DateTimeOffset ExpiresUtc);

    private sealed record EnrollmentProof(Guid UserId, DateTimeOffset ExpiresUtc);
}
