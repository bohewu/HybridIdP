using System.Net.Mail;
using System.Text.Json.Serialization;

namespace Core.Application.DTOs;

public static class ProviderMetadataContract
{
    public const string CurrentVersion = "1.0";
}

public sealed record ProviderMetadataRequest
{
    [JsonRequired]
    public string ContractVersion { get; init; } = ProviderMetadataContract.CurrentVersion;
    public string ProviderNamespace { get; init; } = string.Empty;
    public string StableSubject { get; init; } = string.Empty;

    public bool TryValidate(out string? error)
    {
        if (!string.Equals(ContractVersion, ProviderMetadataContract.CurrentVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(ProviderNamespace) ||
            string.IsNullOrWhiteSpace(StableSubject) ||
            ProviderNamespace.Length > 200 ||
            StableSubject.Length > 256)
        {
            error = "Invalid provider metadata request.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed record ProviderMetadataResult
{
    [JsonRequired]
    public string ContractVersion { get; init; } = ProviderMetadataContract.CurrentVersion;
    public string ProviderNamespace { get; init; } = string.Empty;
    public string StableSubject { get; init; } = string.Empty;
    public string? Email { get; init; }
    public EmailTrustOrigin EmailTrustOrigin { get; init; } = EmailTrustOrigin.Unknown;
    public DateTimeOffset? VerifiedAt { get; init; }

    public bool TryValidate(out string? error)
    {
        if (!string.Equals(ContractVersion, ProviderMetadataContract.CurrentVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(ProviderNamespace) ||
            string.IsNullOrWhiteSpace(StableSubject) ||
            ProviderNamespace.Length > 200 ||
            StableSubject.Length > 256 ||
            !Enum.IsDefined(EmailTrustOrigin) ||
            (EmailTrustOrigin != EmailTrustOrigin.Unknown && !IsValidEmail(Email)) ||
            (VerifiedAt is not null && EmailTrustOrigin != EmailTrustOrigin.SourceVerified))
        {
            error = "Invalid provider metadata result.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        MailAddress.TryCreate(value, out var parsed) &&
        string.Equals(parsed.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
}

[JsonConverter(typeof(JsonStringEnumConverter<EmailTrustOrigin>))]
public enum EmailTrustOrigin
{
    Unknown,
    SourceVerified,
    PolicyTrusted
}
