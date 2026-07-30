using System.Security.Claims;
using System.Text.Json;
using Core.Domain.Constants;

namespace Web.IdP.Helpers;

public static class AuthenticationMethodSession
{
    public const string SessionKey = "AuthenticationMethods";

    public static void Replace(ISession session, params string[] methods)
    {
        ArgumentNullException.ThrowIfNull(session);

        var normalized = methods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        session.SetString(SessionKey, JsonSerializer.Serialize(normalized));
    }

    public static void Add(ISession session, params string[] methods)
    {
        ArgumentNullException.ThrowIfNull(session);

        var current = Get(session).ToList();
        foreach (var method in methods.Where(method => !string.IsNullOrWhiteSpace(method)))
        {
            if (!current.Contains(method, StringComparer.Ordinal))
            {
                current.Add(method);
            }
        }

        session.SetString(SessionKey, JsonSerializer.Serialize(current));
    }

    public static IReadOnlyList<string> Get(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var value = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value)?
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<Claim> CreateClaims(
        ISession session,
        string fallbackPrimaryMethod = AuthConstants.Amr.Password)
    {
        var methods = Get(session).ToList();
        if (!methods.Any(IsPrimaryMethod) &&
            !string.IsNullOrWhiteSpace(fallbackPrimaryMethod))
        {
            methods.Insert(0, fallbackPrimaryMethod);
        }

        return methods
            .Select(method => new Claim(AuthConstants.ClaimTypes.Amr, method))
            .ToList();
    }

    private static bool IsPrimaryMethod(string method) =>
        method is AuthConstants.Amr.Password
            or AuthConstants.Amr.External
            or AuthConstants.Amr.HardwareKey;
}
