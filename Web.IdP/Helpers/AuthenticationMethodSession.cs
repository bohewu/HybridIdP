using System.Security.Claims;
using System.Text.Json;
using Core.Domain;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Web.IdP.Helpers;

// Pending ceremony evidence only. This is never factor-management authority.
public static class AuthenticationMethodSession
{
    public const string SessionKey = "AuthenticationMethods";
    // Matches the five-minute Identity temporary two-factor cookie used by AddIdentity.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static void Replace(ISession session, ApplicationUser user, params string[] methods)
    {
        if (string.IsNullOrEmpty(user.SecurityStamp))
        {
            Consume(session);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        Write(session, new PendingMethods(user.Id, user.SecurityStamp, Normalize(methods), now, now.Add(Lifetime)));
    }

    public static void Add(ISession session, ApplicationUser user, params string[] methods)
    {
        var pending = Read(session, user);
        if (pending == null)
        {
            Replace(session, user, methods);
            return;
        }
        Write(session, pending with { Methods = Normalize(pending.Methods.Concat(methods)) });
    }

    // Existing enrollment authorization must succeed before calling this. A current
    // same-subject cookie may retain its methods while a newly verified factor is added.
    public static async Task AddForEnrollmentAsync(HttpContext context, ApplicationUser user, params string[] methods)
    {
        var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = authentication.Principal;
        if (authentication.Succeeded && AuthorizationAuthenticationSession.HasCurrentAssuranceVersion(principal) &&
            principal!.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString() &&
            !string.IsNullOrEmpty(user.SecurityStamp))
        {
            var stampType = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>()
                .Options.ClaimsIdentity.SecurityStampClaimType;
            if (principal!.FindFirstValue(stampType) == user.SecurityStamp)
                Add(context.Session, user, principal.Claims.Where(claim =>
                        claim.Type is AuthConstants.ClaimTypes.Amr or ClaimTypes.AuthenticationMethod)
                    .Select(claim => claim.Value).ToArray());
        }
        Add(context.Session, user, methods);
    }

    public static IReadOnlyList<string> Get(ISession session, ApplicationUser user) =>
        Read(session, user)?.Methods ?? [];

    public static IReadOnlyList<Claim> CreateClaims(ISession session, ApplicationUser user) =>
        Get(session, user).Select(method => new Claim(AuthConstants.ClaimTypes.Amr, method)).ToList();

    public static DateTimeOffset? GetAuthenticationTime(ISession session, ApplicationUser user) =>
        Read(session, user)?.AuthenticatedAt;

    public static void Consume(ISession session) => session.Remove(SessionKey);

    // Only called after MfaEnrollmentSession has authorized this exact transition.
    internal static void CarryAuthorizedStamp(ISession session, ApplicationUser user, string previousStamp)
    {
        var previous = new ApplicationUser { Id = user.Id, SecurityStamp = previousStamp };
        var pending = Read(session, previous);
        if (pending != null && !string.IsNullOrEmpty(user.SecurityStamp))
            Write(session, pending with { SecurityStamp = user.SecurityStamp });
    }

    private static PendingMethods? Read(ISession session, ApplicationUser user)
    {
        var value = session.GetString(SessionKey);
        if (value == null) return null;
        try
        {
            var pending = JsonSerializer.Deserialize<PendingMethods>(value);
            var now = DateTimeOffset.UtcNow;
            if (pending != null && pending.UserId == user.Id &&
                !string.IsNullOrEmpty(user.SecurityStamp) && pending.SecurityStamp == user.SecurityStamp &&
                pending.AuthenticatedAt <= now && pending.ExpiresUtc > now && pending.Methods != null &&
                pending.Methods.All(method => !string.IsNullOrWhiteSpace(method))) return pending;
        }
        catch (JsonException) { }
        Consume(session);
        return null;
    }

    private static string[] Normalize(IEnumerable<string> methods) => methods
        .Where(method => !string.IsNullOrWhiteSpace(method)).Distinct(StringComparer.Ordinal).ToArray();

    private static void Write(ISession session, PendingMethods pending) =>
        session.SetString(SessionKey, JsonSerializer.Serialize(pending));

    private sealed record PendingMethods(Guid UserId, string SecurityStamp, string[] Methods,
        DateTimeOffset AuthenticatedAt, DateTimeOffset ExpiresUtc);
}
