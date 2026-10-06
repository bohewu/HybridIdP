using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using OpenIddict.Abstractions;

namespace Web.IdP.Helpers;

public static class AuthorizationAuthenticationSession
{
    public const string PreserveTimeKey = "Identity:PreserveAuthenticationTime";
    private const string PendingKey = "Authorization:FreshAuthentication";
    private const string CeremonyClaim = "idp_authentication_ceremony";

    public static long? GetAuthenticationTime(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirst("auth_time")?.Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 ? seconds : null;

    public static bool IsFresh(ClaimsPrincipal principal, long? maxAge, TimeProvider? clock = null)
    {
        if (maxAge is null) return true;
        if (maxAge <= 0 || !long.TryParse(principal.FindFirst("auth_time")?.Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        var now = (clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds();
        return seconds >= 0 && seconds <= now && now - seconds <= maxAge;
    }

    public static void Begin(ISession session, OpenIddictRequest request, TimeProvider? clock = null) =>
        session.SetString(PendingKey, JsonSerializer.Serialize(new PendingAuthentication(
            Guid.NewGuid().ToString("N"), AuthorizationConsentSession.ComputeRequestFingerprint(request),
            (clock ?? TimeProvider.System).GetUtcNow().AddMinutes(5))));

    public static bool TryConsume(ISession session, ClaimsPrincipal principal, OpenIddictRequest request, TimeProvider? clock = null)
    {
        var pending = Read(session, clock);
        if (pending == null || principal.FindFirst(CeremonyClaim)?.Value != pending.Nonce ||
            pending.Fingerprint != AuthorizationConsentSession.ComputeRequestFingerprint(request)) return false;
        session.Remove(PendingKey);
        return true;
    }

    public static void PreserveTime(HttpContext context, ClaimsPrincipal principal) =>
        context.Items[PreserveTimeKey] = principal.FindFirst("auth_time")?.Value;

    public static bool RequiresChallenge(ISession session, ClaimsPrincipal principal, OpenIddictRequest request,
        bool forceLogin, TimeProvider? clock = null)
    {
        var completedCeremony = TryConsume(session, principal, request, clock);
        var fresh = IsFresh(principal, request.MaxAge, clock);
        return (forceLogin || !fresh) &&
            !(completedCeremony && (request.MaxAge is null or 0 || fresh));
    }

    public static void OnSigningIn(CookieSigningInContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity) return;
        foreach (var claim in identity.FindAll("auth_time").Concat(identity.FindAll(CeremonyClaim)).ToArray())
            identity.RemoveClaim(claim);
        if (context.HttpContext.Items.TryGetValue(PreserveTimeKey, out var originalTime))
        {
            if (originalTime is string value) identity.AddClaim(new Claim("auth_time", value, ClaimValueTypes.Integer64));
            return;
        }
        if (!identity.Claims.Any(c => c.Type == "amr" || c.Type == ClaimTypes.AuthenticationMethod)) return;
        identity.AddClaim(new Claim("auth_time", (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        var pending = Read(context.HttpContext.Session, context.Options.TimeProvider);
        if (pending != null) identity.AddClaim(new Claim(CeremonyClaim, pending.Nonce));
    }

    private static PendingAuthentication? Read(ISession session, TimeProvider? clock)
    {
        var value = session.GetString(PendingKey);
        if (value == null) return null;
        try
        {
            var pending = JsonSerializer.Deserialize<PendingAuthentication>(value);
            if (pending?.Expires > (clock ?? TimeProvider.System).GetUtcNow()) return pending;
        }
        catch (JsonException) { }
        session.Remove(PendingKey);
        return null;
    }

    private sealed record PendingAuthentication(string Nonce, string Fingerprint, DateTimeOffset Expires);
}
