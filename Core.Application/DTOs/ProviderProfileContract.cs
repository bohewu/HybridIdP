using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Application.DTOs;

public static class ProviderProfileContract
{
    public const string CurrentVersion = "1.0";
    public const int MaximumProperties = 32;
    public const int MaximumStringLength = 1024;

    public static bool IsValidKey(string? key) => key is { Length: > 0 and <= 64 } &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}

public sealed record ProviderProfileRequest
{
    [JsonRequired]
    public string ContractVersion { get; init; } = ProviderProfileContract.CurrentVersion;
    [JsonRequired]
    public string ProviderNamespace { get; init; } = string.Empty;
    [JsonRequired]
    public string StableSubject { get; init; } = string.Empty;

    public bool IsValid() => ContractVersion == ProviderProfileContract.CurrentVersion &&
        !string.IsNullOrWhiteSpace(ProviderNamespace) && ProviderNamespace.Length <= 200 &&
        !string.IsNullOrWhiteSpace(StableSubject) && StableSubject.Length <= 256;
}

public sealed record ProviderProfileResult
{
    [JsonRequired]
    public string ContractVersion { get; init; } = string.Empty;
    [JsonRequired]
    public string ProviderNamespace { get; init; } = string.Empty;
    [JsonRequired]
    public string StableSubject { get; init; } = string.Empty;
    [JsonRequired]
    public Dictionary<string, JsonElement> ExtraProperties { get; init; } = new(StringComparer.Ordinal);

    public bool IsValidFor(ProviderProfileRequest request) => request.IsValid() &&
        ContractVersion == request.ContractVersion && ProviderNamespace == request.ProviderNamespace &&
        StableSubject == request.StableSubject && ExtraProperties is not null &&
        ExtraProperties.Count <= ProviderProfileContract.MaximumProperties &&
        ExtraProperties.All(pair => ProviderProfileContract.IsValidKey(pair.Key) &&
            (pair.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ||
             pair.Value.ValueKind == JsonValueKind.String &&
             pair.Value.GetString()!.Length <= ProviderProfileContract.MaximumStringLength));
}
