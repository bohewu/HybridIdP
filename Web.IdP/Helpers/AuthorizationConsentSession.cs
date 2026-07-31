using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Web.IdP.Helpers;

/// <summary>
/// Tracks short-lived, one-time consent intents bound to the current user and
/// the exact OpenID Connect authorization request rendered by the server.
/// </summary>
public static class AuthorizationConsentSession
{
    public const string FormFieldName = "consent_intent";

    private const string SessionKey = "AuthorizationConsent:Intents";
    private const int MaximumPendingIntents = 8;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> ConsentFormParameters =
        new(StringComparer.Ordinal)
        {
            "__RequestVerificationToken",
            FormFieldName,
            "granted_scopes",
            "submit"
        };

    public static string Issue(
        ISession session,
        ClaimsPrincipal principal,
        OpenIddictRequest request,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);

        var subject = GetSubject(principal);
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException(
                "An authenticated subject is required to issue a consent intent.");
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var intents = Read(session)
            .Where(intent => intent.ExpiresUtc > now)
            .OrderBy(intent => intent.ExpiresUtc)
            .TakeLast(MaximumPendingIntents - 1)
            .ToList();

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        intents.Add(new ConsentIntent(
            token,
            subject,
            ComputeRequestFingerprint(request),
            now.Add(Lifetime)));
        Write(session, intents);
        return token;
    }

    public static bool TryConsume(
        ISession session,
        ClaimsPrincipal principal,
        OpenIddictRequest request,
        string? token,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);

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

        var actualFingerprint = Convert.FromHexString(ComputeRequestFingerprint(request));
        var expectedFingerprint = Convert.FromHexString(intent.RequestFingerprint);
        return CryptographicOperations.FixedTimeEquals(
            actualFingerprint,
            expectedFingerprint);
    }

    private static string ComputeRequestFingerprint(OpenIddictRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var parameter in request.GetParameters()
                         .Where(parameter =>
                             !ConsentFormParameters.Contains(parameter.Key))
                         .OrderBy(parameter => parameter.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(parameter.Key);
                parameter.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    private static string? GetSubject(ClaimsPrincipal principal)
    {
        return principal.GetClaim(Claims.Subject) ??
               principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private static List<ConsentIntent> Read(ISession session)
    {
        var value = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ConsentIntent>>(value) ?? [];
        }
        catch (JsonException)
        {
            session.Remove(SessionKey);
            return [];
        }
    }

    private static void Write(ISession session, List<ConsentIntent> intents)
    {
        if (intents.Count == 0)
        {
            session.Remove(SessionKey);
            return;
        }

        session.SetString(SessionKey, JsonSerializer.Serialize(intents));
    }

    private sealed record ConsentIntent(
        string Token,
        string Subject,
        string RequestFingerprint,
        DateTimeOffset ExpiresUtc);
}
