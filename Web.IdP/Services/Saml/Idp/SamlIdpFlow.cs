using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Core.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Web.IdP.Services.Saml.Idp;

public sealed class SamlIdpFlow(SamlFederationRuntime runtime, SamlIdpProtocol protocol,
    ISamlStateStore states, IDataProtectionProvider protection, ISamlIdpAccountPolicy accountPolicy, TimeProvider clock)
{
    private const string CookiePrefix = "__Secure-HybridIdP.Saml.Idp.";
    private sealed record Continuation(SamlIdpRequest Request, string BrowserNonce);

    public async Task<IActionResult> BeginAsync(HttpContext http, CancellationToken ct)
    {
        var request = protocol.ReadRequest(http.Request);
        var replayKey = request.Partner.Length + ":" + request.Partner + request.RequestId;
        if (!await states.TryAddAsync(SamlStatePurpose.IdpRequestReplay, replayKey, "", request.ExpiresAt, ct))
            return new BadRequestResult();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var continuation = new Continuation(request, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var payload = JsonSerializer.Serialize(continuation);
        if (!await states.TryAddAsync(SamlStatePurpose.IdpContinuation, token, payload, request.ExpiresAt, ct))
            return new BadRequestResult();
        http.Response.Cookies.Append(CookiePrefix + token, Protector(token).Protect(payload), Cookie(request.ExpiresAt));
        if (request.ForceAuthentication)
        {
            await http.SignOutAsync(IdentityConstants.ApplicationScheme);
            await http.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            await http.SignOutAsync(IdentityConstants.TwoFactorRememberMeScheme);
        }
        return new RedirectResult(ContinueUrl(token));
    }

    public async Task<IActionResult> ContinueAsync(HttpContext http, string? token, string continueLabel, CancellationToken ct)
    {
        if (!runtime.Options.Idp.Enabled || !http.Request.IsHttps || token is not { Length: 64 } ||
            token.Any(c => !Uri.IsHexDigit(c)) || !http.Request.Cookies.TryGetValue(CookiePrefix + token, out var cookie) ||
            cookie.Length > 8192) return new BadRequestResult();
        Continuation? continuation;
        string payload;
        try
        {
            payload = Protector(token).Unprotect(cookie);
            continuation = JsonSerializer.Deserialize<Continuation>(payload);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return new BadRequestResult();
        }
        if (continuation == null || continuation.Request.ExpiresAt <= clock.GetUtcNow()) return new BadRequestResult();
        var account = await accountPolicy.EvaluateAsync(http, ContinueUrl(token), ct);
        if (account.RequiredAction != null) return account.RequiredAction;

        // The protected cookie supplies read-only browser continuity during login/MFA.
        // Only the winner of the atomic DB consume can cross the issuance boundary.
        var stored = await states.TryTakeAsync(SamlStatePurpose.IdpContinuation, token, ct);
        if (stored != payload) return new BadRequestResult();
        http.Response.Cookies.Delete(CookiePrefix + token, Cookie(continuation.Request.ExpiresAt));
        var current = await accountPolicy.EvaluateAsync(http, ContinueUrl(token), ct);
        if (current.RequiredAction != null || current.UserId != account.UserId) return new StatusCodeResult(403);
        var response = protocol.CreateResponse(continuation.Request, current.UserId, current.AuthenticatedAt);
        var destination = runtime.Options.Idp.ServiceProviders.Single(p => p.EntityId == continuation.Request.Partner)
            .AssertionConsumerServiceUrl;
        // Add a stricter response-local policy. It composes with the existing CSP; no global relaxation.
        http.Response.Headers.Append("Content-Security-Policy", $"form-action {destination}; base-uri 'none'; frame-ancestors 'none'");
        static string Encode(string value) => WebUtility.HtmlEncode(value);
        var relay = continuation.Request.RelayState is { } relayState
            ? $"<input type=\"hidden\" name=\"RelayState\" value=\"{Encode(relayState)}\">" : "";
        return new ContentResult
        {
            ContentType = "text/html; charset=utf-8",
            Content = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body>" +
                $"<form id=\"saml-post\" method=\"post\" action=\"{Encode(destination)}\">" +
                $"<input type=\"hidden\" name=\"SAMLResponse\" value=\"{response}\">{relay}" +
                $"<noscript><button type=\"submit\">{Encode(continueLabel)}</button></noscript></form>" +
                "<script src=\"/saml/idp/post.js\" defer></script></body></html>"
        };
    }

    private IDataProtector Protector(string token) => protection.CreateProtector("HybridIdP.Saml.Idp.Continuation.v1", token);
    private static string ContinueUrl(string token) => QueryHelpers.AddQueryString(SamlProfile.IdpContinuePath, "state", token);
    private static CookieOptions Cookie(DateTimeOffset expires) => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true,
        Path = "/saml/idp", Expires = expires
    };
}
