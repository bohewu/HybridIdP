using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

/// <summary>Presentation of durable organization credentials, separate from login identifiers.</summary>
public static class ManagedCredentialDisplay
{
    public static bool IsManaged(string provider, string? trustedNamespace,
        StringComparison comparison = StringComparison.Ordinal) =>
        string.Equals(provider, AuthConstants.Providers.Legacy, comparison) ||
        (!string.IsNullOrWhiteSpace(trustedNamespace) &&
         string.Equals(provider, trustedNamespace, comparison));

    public static string? GetAssuredName(IEnumerable<UserLoginInfo> logins, string? trustedNamespace) =>
        string.IsNullOrWhiteSpace(trustedNamespace) ? null : logins
            .Where(login => login.LoginProvider != AuthConstants.Providers.Legacy &&
                string.Equals(login.LoginProvider, trustedNamespace, StringComparison.Ordinal) &&
                !string.Equals(login.ProviderDisplayName, login.LoginProvider, StringComparison.Ordinal) &&
                !string.Equals(login.ProviderDisplayName, login.ProviderKey, StringComparison.Ordinal))
            .OrderBy(login => login.ProviderKey, StringComparer.Ordinal)
            .Select(login => login.ProviderDisplayName?.Trim())
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

    public static string GetDisplayName(ApplicationUser user, Person? person, string? assuredName,
        string fallback) =>
        NameFormatter.BuildDisplayName(user.FirstName, user.MiddleName, user.LastName)
        ?? NameFormatter.BuildDisplayName(person?.FirstName, person?.MiddleName, person?.LastName)
        ?? (string.IsNullOrWhiteSpace(user.Nickname) ? null : user.Nickname.Trim())
        ?? (string.IsNullOrWhiteSpace(person?.Nickname) ? null : person.Nickname.Trim())
        ?? (string.IsNullOrWhiteSpace(assuredName) ? null : assuredName.Trim())
        ?? fallback;
}
