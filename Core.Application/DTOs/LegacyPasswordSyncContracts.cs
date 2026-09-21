using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Application.DTOs;

public sealed class LegacyPasswordSyncRequest
{
    [JsonPropertyName("operationId")]
    public required Guid OperationId { get; init; }

    [JsonPropertyName("accountIdentity")]
    public required string AccountIdentity { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }

    public override string ToString() => nameof(LegacyPasswordSyncRequest);
}

[JsonConverter(typeof(JsonStringEnumConverter<LegacyPasswordSyncOutcome>))]
public enum LegacyPasswordSyncOutcome
{
    NoOp,
    Success,
    PartialSuccess,
    Failed,
    CommitUnknown
}

public sealed class LegacyPasswordSyncResponse
{
    [JsonPropertyName("operationId")]
    public required Guid OperationId { get; init; }

    [JsonPropertyName("outcome")]
    public required LegacyPasswordSyncOutcome Outcome { get; init; }
}

public static class LegacyPasswordSyncContract
{
    public const int MaximumAccountIdentityLength = 256;

    public static bool IsCanonicalAccountIdentity(string? value) =>
        value is { Length: > 0 and <= MaximumAccountIdentityLength } &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl) &&
        Guid.TryParseExact(value, "D", out var accountIdentity) &&
        string.Equals(value, accountIdentity.ToString("D"), StringComparison.Ordinal);

    public static bool TryValidateRequest(LegacyPasswordSyncRequest? request) =>
        request is not null && request.OperationId != Guid.Empty &&
        IsCanonicalAccountIdentity(request.AccountIdentity) &&
        !string.IsNullOrEmpty(request.Password);

    public static bool TryParseResponse(
        string? responseJson,
        Guid expectedOperationId,
        out LegacyPasswordSyncResponse? response)
    {
        response = null;
        if (expectedOperationId == Guid.Empty || string.IsNullOrWhiteSpace(responseJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 2 ||
                properties.Count(property => property.NameEquals("operationId")) != 1 ||
                properties.Count(property => property.NameEquals("outcome")) != 1 ||
                !root.TryGetProperty("operationId", out var operationProperty) ||
                operationProperty.ValueKind != JsonValueKind.String ||
                !operationProperty.TryGetGuid(out var operationId) || operationId != expectedOperationId ||
                !root.TryGetProperty("outcome", out var outcomeProperty) ||
                outcomeProperty.ValueKind != JsonValueKind.String ||
                !TryParseExactOutcome(outcomeProperty.GetString(), out var outcome))
            {
                return false;
            }

            response = new LegacyPasswordSyncResponse
            {
                OperationId = operationId,
                Outcome = outcome
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseExactOutcome(string? value, out LegacyPasswordSyncOutcome outcome) =>
        Enum.TryParse(value, ignoreCase: false, out outcome) &&
        Enum.GetNames<LegacyPasswordSyncOutcome>().Contains(value, StringComparer.Ordinal);
}

public enum LegacyPasswordSyncCohort
{
    CompletedDirectoryRecovery,
    CompletedDirectoryRequiredChange,
    Stage2Migration
}

public sealed record LegacyPasswordSyncTarget(Guid AccountIdentityGuid, string MappingVersion)
{
    public string AccountIdentity => AccountIdentityGuid.ToString("D");
}

public enum LegacyPasswordSyncMappingOutcome
{
    Resolved,
    Disabled,
    Missing,
    Duplicate,
    Stale,
    Contradictory,
    Malformed
}

public sealed record LegacyPasswordSyncMappingResolution(
    LegacyPasswordSyncMappingOutcome Outcome,
    LegacyPasswordSyncTarget? Target = null);
