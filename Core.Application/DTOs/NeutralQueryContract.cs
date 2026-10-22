using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Application.DTOs;

public static class NeutralQueryContract
{
    public const string Version = "2.0";
    public const string Legacy = "legacy";
    public const string WireMode = "neutral-v2";
    public const int MaximumRequestBytes = 8192;
    public static readonly IReadOnlyList<string> Capabilities = Array.AsReadOnly(new[]
        { "native-profile", "profile", "metadata", "lifecycle-status", "recovery-identity-verification", "password-sync" });
    public static readonly IReadOnlyDictionary<string, string[]> ValueSources = new ReadOnlyDictionary<string, string[]>(
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["employeeId"] = ["person.employeeId", "user.employeeId", "runtime.proof.employeeId", "runtime.directory.employeeId"],
            ["nationalIdHash"] = ["person.nationalId"],
            ["accountName"] = ["user.userName", "runtime.proof.canonicalAccount", "runtime.directory.canonicalAccount"]
        });
    public static int MaximumResponseBytes(string capability) => capability switch
    {
        "metadata" => 16384, "lifecycle-status" => 8192, "recovery-identity-verification" => 4096, _ => 65536
    };
    public static bool IsText(string? value, int maximum, bool required = true)
    {
        if (value is null || value.Length > maximum || (required && string.IsNullOrWhiteSpace(value)) || value.Any(char.IsControl)) return false;
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }
    public static bool IsId(string? value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && id.ToString("D") == value;
    public static bool IsRule(string? value) => value is null || value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    public static bool TryNormalize(string attribute, string? value, out string normalized)
    {
        normalized = string.Empty;
        if (attribute == "nationalIdHash")
        {
            if (value is not { Length: 64 } || !value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')) return false;
            normalized = value;
            return true;
        }
        var maximum = attribute switch { "employeeId" => 50, "accountName" => 256, _ => 0 };
        if (maximum == 0 || value is null || !IsText(value, int.MaxValue)) return false;
        normalized = value.Trim();
        return IsText(normalized, maximum);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NeutralQueryDto(
    IReadOnlyDictionary<string, string> Attributes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QueryRule = null);

// Separate v2 types deliberately leave every legacy serializer and parser unchanged.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NeutralQueryRequest
{
    public string ContractType { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = NeutralQueryContract.Version;
    public string RequestId { get; init; } = Guid.NewGuid().ToString("D");
    public string ProviderNamespace { get; init; } = string.Empty;
    public NeutralQueryDto Query { get; init; } = new(new Dictionary<string, string>());
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Scheme { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NeutralQueryEvidence? Evidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OperationId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Password { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NeutralQueryEvidence(string IdentityIdentifier);
public sealed record NeutralQueryResponse(string ContractType, string RequestId, string ProviderNamespace,
    string QueryOutcome, JsonElement? Result, string? OperationId = null);

public sealed record NeutralNativeProfileResult(AssuredProfile Profile, string EmailTrustOrigin);
public sealed record NeutralProfileResult(IReadOnlyDictionary<string, JsonElement> ExtraProperties);
public sealed record NeutralMetadataResult(string? Email, string EmailTrustOrigin, DateTimeOffset? VerifiedAt);
public sealed record NeutralLifecycleResult(string AccountState, ProviderLifecycleEvidence Evidence);
public sealed record NeutralRecoveryResult(string Outcome, string Scheme);
public sealed record NeutralPasswordSyncResult(string Outcome);
