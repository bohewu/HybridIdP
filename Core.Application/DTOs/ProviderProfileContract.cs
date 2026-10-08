using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Application.DTOs;

public static class ProviderProfileContract
{
    public const string CurrentVersion = "1.0";
    public const int MaximumProperties = 32;
    public const int MaximumStringLength = 1024;
    public const int MaximumArrayElements = 32;

    public static bool IsValueOfType(JsonElement value, string type) => type switch
    {
        "Boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "String" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= MaximumStringLength,
        "StringArray" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= MaximumArrayElements &&
            value.EnumerateArray().All(item => IsValueOfType(item, "String")),
        _ => false
    };

    // Validate raw counts before normalization; arrays are ordinal sets, not ordered lists.
    public static JsonElement NormalizeValue(JsonElement value)
    {
        if (!IsValueOfType(value, "String") && !IsValueOfType(value, "Boolean") && !IsValueOfType(value, "StringArray"))
            throw new ArgumentException("Invalid Provider Profile value.", nameof(value));
        return value.ValueKind == JsonValueKind.Array
            ? JsonSerializer.SerializeToElement(value.EnumerateArray().Select(item => item.GetString()!)
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray())
            : value.Clone();
    }

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
            (ProviderProfileContract.IsValueOfType(pair.Value, "Boolean") ||
             ProviderProfileContract.IsValueOfType(pair.Value, "String") ||
             ProviderProfileContract.IsValueOfType(pair.Value, "StringArray")));
}
