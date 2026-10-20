using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace Core.Application.DTOs;

/// <summary>Fresh native-profile data for one established identity; never credential or eligibility proof.</summary>
public sealed record ProviderNativeProfileRequest(string ContractVersion, string ProviderNamespace, string StableSubject);

public sealed record ProviderNativeProfileResponse(
    string ContractVersion, string ProviderNamespace, string StableSubject,
    AssuredProfile Profile, string EmailTrustOrigin);

public static class ProviderNativeProfileContract
{
    public const string Version = "1.0";
    public const int MaximumResponseBytes = 64 * 1024;

    public static bool IsValidIdentity(string? providerNamespace, string? stableSubject) =>
        IsRequiredString(providerNamespace, 200) && IsRequiredString(stableSubject, 256);

    public static ProviderNativeProfileResponse Parse(ReadOnlySpan<byte> utf8, ProviderNativeProfileRequest request)
    {
        if (utf8.Length > MaximumResponseBytes || request.ContractVersion != Version ||
            !IsValidIdentity(request.ProviderNamespace, request.StableSubject)) throw new JsonException();
        // Reject malformed UTF-8 instead of accepting a replacement-character identity.
        using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(utf8),
            new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        CheckObject(root, ["contractVersion", "providerNamespace", "stableSubject", "profile", "emailTrustOrigin"]);
        if (Required(root, "contractVersion", 8) != Version ||
            Required(root, "providerNamespace", 200) != request.ProviderNamespace ||
            Required(root, "stableSubject", 256) != request.StableSubject) throw new JsonException();
        var origin = Required(root, "emailTrustOrigin", 20);
        if (origin is not ("Unknown" or "SourceVerified" or "PolicyTrusted")) throw new JsonException();
        if (!root.TryGetProperty("profile", out var profile)) throw new JsonException();
        CheckObject(profile, ["displayName", "givenName", "surname", "email", "department", "title", "employeeId", "assuredFields"]);
        if (!profile.TryGetProperty("assuredFields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            throw new JsonException();
        var assured = new List<AssuredProfileField>();
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.String ||
                !Enum.GetNames<AssuredProfileField>().Contains(field.GetString(), StringComparer.Ordinal) ||
                !Enum.TryParse<AssuredProfileField>(field.GetString(), out var parsed) || assured.Contains(parsed))
                throw new JsonException();
            assured.Add(parsed);
        }
        var result = new AssuredProfile
        {
            DisplayName = Optional(profile, "displayName", 256),
            GivenName = Optional(profile, "givenName", 100),
            Surname = Optional(profile, "surname", 100),
            Email = Optional(profile, "email", 256),
            Department = Optional(profile, "department", 200),
            Title = Optional(profile, "title", 200),
            EmployeeId = Optional(profile, "employeeId", 50),
            AssuredFields = assured
        };
        if (!result.IsValid() || !string.IsNullOrWhiteSpace(result.Email) && !IsValidEmail(result.Email))
            throw new JsonException();
        return new(Version, request.ProviderNamespace, request.StableSubject, result, origin);
    }

    public static bool IsValidEmail(string value) =>
        value.Length <= 256 && !value.Any(char.IsControl) && MailAddress.TryCreate(value, out var address) &&
        string.Equals(address.Address, value, StringComparison.Ordinal) && string.IsNullOrEmpty(address.DisplayName);

    private static bool IsRequiredString(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static string Required(JsonElement element, string name, int maximum) =>
        Optional(element, name, maximum) is { } value && IsRequiredString(value, maximum)
            ? value : throw new JsonException();

    private static string? Optional(JsonElement element, string name, int maximum)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new JsonException();
        var text = value.GetString()!;
        if (text.Length > maximum || text.Any(char.IsControl)) throw new JsonException();
        return text;
    }

    private static void CheckObject(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new JsonException();
    }
}
