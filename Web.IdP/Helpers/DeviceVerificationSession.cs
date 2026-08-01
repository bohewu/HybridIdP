using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Helpers;

/// <summary>
/// Tracks short-lived, one-time browser intents for device verification.
/// Intents are bound to the current user and, when the verification page was
/// opened with a valid user code, the exact resolved device interaction.
/// </summary>
public static class DeviceVerificationSession
{
    public const string FormFieldName = "device_verification_intent";

    private const string SessionKey = "DeviceVerification:Intents";
    private const int MaximumPendingIntents = 8;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static string Issue(
        ISession session,
        ClaimsPrincipal principal,
        AuthenticateResult authenticateResult,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(authenticateResult);

        var subject = GetSubject(principal);
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException(
                "An authenticated subject is required to issue a device verification intent.");
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var intents = Read(session)
            .Where(intent => intent.ExpiresUtc > now)
            .OrderBy(intent => intent.ExpiresUtc)
            .TakeLast(MaximumPendingIntents - 1)
            .ToList();

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        intents.Add(new VerificationIntent(
            token,
            subject,
            ComputeInteractionFingerprint(authenticateResult),
            now.Add(Lifetime)));
        Write(session, intents);
        return token;
    }

    public static bool TryConsume(
        ISession session,
        ClaimsPrincipal principal,
        AuthenticateResult authenticateResult,
        string? token,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(authenticateResult);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var intents = Read(session);
        var intent = string.IsNullOrWhiteSpace(token)
            ? null
            : intents.FirstOrDefault(candidate =>
                string.Equals(candidate.Token, token, StringComparison.Ordinal));

        var remaining = intents
            .Where(candidate => candidate.ExpiresUtc > now && candidate != intent)
            .ToList();
        Write(session, remaining);

        if (intent == null || intent.ExpiresUtc <= now)
        {
            return false;
        }

        var subject = GetSubject(principal);
        if (!string.Equals(intent.Subject, subject, StringComparison.Ordinal))
        {
            return false;
        }

        // An intent issued for the manual-entry page deliberately accepts the
        // device interaction the user subsequently types into that page.
        if (intent.InteractionFingerprint == null)
        {
            return true;
        }

        var actualFingerprint = ComputeInteractionFingerprint(authenticateResult);
        if (actualFingerprint == null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actualFingerprint),
            Convert.FromHexString(intent.InteractionFingerprint));
    }

    private static string? ComputeInteractionFingerprint(
        AuthenticateResult authenticateResult)
    {
        if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
        {
            return null;
        }

        var clientId = authenticateResult.Principal.GetClaim(Claims.ClientId);
        var userCode = authenticateResult.Properties?.GetTokenValue(
            OpenIddictServerAspNetCoreConstants.Tokens.UserCode);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(userCode))
        {
            return null;
        }

        var normalizedUserCode = string.Concat(
            userCode.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        var value = $"{clientId}\n{normalizedUserCode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string? GetSubject(ClaimsPrincipal principal)
    {
        return principal.GetClaim(Claims.Subject) ??
               principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private static List<VerificationIntent> Read(ISession session)
    {
        var value = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<VerificationIntent>>(value) ?? [];
        }
        catch (JsonException)
        {
            session.Remove(SessionKey);
            return [];
        }
    }

    private static void Write(ISession session, List<VerificationIntent> intents)
    {
        if (intents.Count == 0)
        {
            session.Remove(SessionKey);
            return;
        }

        session.SetString(SessionKey, JsonSerializer.Serialize(intents));
    }

    private sealed record VerificationIntent(
        string Token,
        string Subject,
        string? InteractionFingerprint,
        DateTimeOffset ExpiresUtc);
}
