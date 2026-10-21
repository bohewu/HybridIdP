using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Domain;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

// Lookup-only routing metadata. A hint never proves identity or credential authority.
internal static class ProofLoginHint
{
    private const string LoginProvider = "HybridIdP.ProofLogin";
    private sealed record Hint(string ProviderNamespace, string StableSubject, string CanonicalAccount);
    private static string Name(string providerNamespace) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerNamespace)));

    public static async Task<string?> ReadAsync(UserManager<ApplicationUser> users, ApplicationUser user,
        string providerNamespace, string stableSubject)
    {
        var value = await users.GetAuthenticationTokenAsync(user, LoginProvider, Name(providerNamespace));
        if (value is null) return null;
        try
        {
            if (value.Length > 4096) throw new JsonException();
            var hint = JsonSerializer.Deserialize<Hint>(value);
            if (hint is null || hint.ProviderNamespace != providerNamespace || hint.StableSubject != stableSubject ||
                !ValidAccount(hint.CanonicalAccount)) throw new JsonException();
            return hint.CanonicalAccount;
        }
        catch (JsonException) { throw new InvalidOperationException("Provider login hint is invalid."); }
    }

    public static async Task SaveAsync(UserManager<ApplicationUser> users, ApplicationUser user,
        string providerNamespace, string stableSubject, string canonicalAccount)
    {
        if (!ValidAccount(canonicalAccount)) throw new InvalidOperationException("Provider login hint is invalid.");
        var result = await users.SetAuthenticationTokenAsync(user, LoginProvider, Name(providerNamespace),
            JsonSerializer.Serialize(new Hint(providerNamespace, stableSubject, canonicalAccount)));
        if (!result.Succeeded) throw new InvalidOperationException("Provider login hint could not be saved.");
    }

    internal static bool ValidAccount(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        value == value.Trim() && !value.Any(char.IsControl);
}
