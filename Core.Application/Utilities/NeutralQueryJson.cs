using System.Globalization;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;

namespace Core.Application.Utilities;

/// <summary>Strict v2 admission and correlation. A parsed lookup never grants capability authority.</summary>
public static class NeutralQueryJson
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static bool TrySerializeRequest(NeutralQueryRequest request, out byte[] body)
    {
        body = [];
        if (!IsValidRequest(request)) return false;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, WireJson);
        if (bytes.Length > NeutralQueryContract.MaximumRequestBytes) return false;
        body = bytes;
        return true;
    }

    public static bool TryReadRequest(string? contractHeader, ReadOnlyMemory<byte> body, out NeutralQueryRequest? request)
    {
        request = null;
        try
        {
            using var document = Parse(body, NeutralQueryContract.MaximumRequestBytes);
            var root = document.RootElement;
            var type = Text(root, "contractType");
            var names = new List<string> { "contractType", "contractVersion", "requestId", "providerNamespace", "query" };
            if (type == "recovery-identity-verification") names.AddRange(["scheme", "evidence"]);
            if (type == "password-sync") names.AddRange(["operationId", "password"]);
            if (!Exactly(root, names.ToArray()) || contractHeader != type + "/2.0") return false;
            var query = root.GetProperty("query");
            if (!Exactly(query, query.TryGetProperty("queryRule", out _) ? ["attributes", "queryRule"] : ["attributes"])) return false;
            var parsed = root.Deserialize<NeutralQueryRequest>(WireJson);
            if (parsed is null || !IsValidRequest(parsed)) return false;
            request = parsed;
            return true;
        }
        catch (Exception ex) when (IsParseFailure(ex)) { return false; }
    }

    public static bool TryReadResponse(int statusCode, string? contentType, ReadOnlyMemory<byte> body,
        NeutralQueryRequest expected, out NeutralQueryResponse? response)
    {
        response = null;
        try
        {
            if (!IsValidRequest(expected) || !ProviderLifecycleJson.IsMediaType(contentType)) return false;
            using var document = Parse(body, NeutralQueryContract.MaximumResponseBytes(expected.ContractType));
            var root = document.RootElement;
            var outcome = Text(root, "queryOutcome");
            var names = new List<string> { "contractType", "contractVersion", "requestId", "providerNamespace", "queryOutcome" };
            if (outcome == "Found") names.Add("result");
            if (expected.ContractType == "password-sync") names.Add("operationId");
            if (!Exactly(root, names.ToArray()) || Text(root, "contractType") != expected.ContractType ||
                Text(root, "contractVersion") != "2.0" || Text(root, "requestId") != expected.RequestId ||
                Text(root, "providerNamespace") != expected.ProviderNamespace ||
                expected.ContractType == "password-sync" && Text(root, "operationId") != expected.OperationId) return false;
            var status = outcome switch
            {
                "Found" or "NotFound" => 200,
                "Ambiguous" => expected.ContractType == "recovery-identity-verification" ? 503 : 200,
                "Unavailable" => 503, "Unsupported" => 400, _ => 0
            };
            if (status == 0 || status != statusCode) return false;
            JsonElement? result = null;
            if (outcome == "Found")
            {
                var value = root.GetProperty("result");
                if (!ValidResult(expected.ContractType, value)) return false;
                result = value.Clone();
            }
            response = new(expected.ContractType, expected.RequestId, expected.ProviderNamespace, outcome!, result, expected.OperationId);
            return true;
        }
        catch (Exception ex) when (IsParseFailure(ex)) { return false; }
    }

    public static bool IsValidRequest(NeutralQueryRequest? request)
    {
        if (request is null || !NeutralQueryContract.Capabilities.Contains(request.ContractType) || request.ContractVersion != "2.0" ||
            !NeutralQueryContract.IsId(request.RequestId) || !NeutralQueryContract.IsText(request.ProviderNamespace, 200) ||
            request.Query?.Attributes is not { Count: > 0 and <= 3 } attributes || !NeutralQueryContract.IsRule(request.Query.QueryRule) ||
            attributes.Any(x => !NeutralQueryContract.TryNormalize(x.Key, x.Value, out var value) || value != x.Value)) return false;
        if (request.ContractType == "recovery-identity-verification")
            return request.Scheme == "identity-identifier" && request.Evidence is not null &&
                NeutralQueryContract.IsText(request.Evidence.IdentityIdentifier, 128) && request.OperationId is null && request.Password is null;
        if (request.ContractType == "password-sync")
            return NeutralQueryContract.IsId(request.OperationId) && request.Password is { Length: > 0 } &&
                ValidUnicode(request.Password) && request.Scheme is null && request.Evidence is null;
        return request.Scheme is null && request.Evidence is null && request.OperationId is null && request.Password is null;
    }

    private static bool ValidResult(string type, JsonElement result)
    {
        switch (type)
        {
            case "native-profile":
                if (!Exactly(result, "profile", "emailTrustOrigin") || !Origin(Text(result, "emailTrustOrigin"))) return false;
                var profile = result.GetProperty("profile");
                var allowed = new Dictionary<string, int> { ["displayName"] = 256, ["givenName"] = 100, ["surname"] = 100,
                    ["email"] = 256, ["department"] = 200, ["title"] = 200, ["employeeId"] = 50 };
                if (profile.ValueKind != JsonValueKind.Object || !profile.TryGetProperty("assuredFields", out var assured) || assured.ValueKind != JsonValueKind.Array ||
                    assured.GetArrayLength() > 7 || assured.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String ||
                        !Enum.GetNames<AssuredProfileField>().Contains(x.GetString(), StringComparer.Ordinal)) ||
                    assured.EnumerateArray().Select(x => x.GetString()).Distinct().Count() != assured.GetArrayLength()) return false;
                foreach (var property in profile.EnumerateObject())
                    if (property.Name != "assuredFields" && (!allowed.TryGetValue(property.Name, out var limit) || !OptionalText(property.Value, limit))) return false;
                var model = profile.Deserialize<AssuredProfile>(WireJson);
                return model?.IsValid() == true && (string.IsNullOrWhiteSpace(model.Email) || ProviderNativeProfileContract.IsValidEmail(model.Email));
            case "profile":
                if (!Exactly(result, "extraProperties")) return false;
                var properties = result.GetProperty("extraProperties");
                return properties.ValueKind == JsonValueKind.Object && properties.EnumerateObject().Count() <= 32 &&
                    properties.EnumerateObject().All(x => ProviderProfileContract.IsValidKey(x.Name) &&
                    (ProviderProfileContract.IsValueOfType(x.Value, "String") || ProviderProfileContract.IsValueOfType(x.Value, "Boolean") ||
                     ProviderProfileContract.IsValueOfType(x.Value, "StringArray")));
            case "metadata":
                if (!Exactly(result, "email", "emailTrustOrigin", "verifiedAt") || !Origin(Text(result, "emailTrustOrigin")) ||
                    !OptionalText(result.GetProperty("email"), 256)) return false;
                var email = Text(result, "email");
                var origin = Text(result, "emailTrustOrigin");
                if (origin != "Unknown" && (email is null || !ProviderNativeProfileContract.IsValidEmail(email))) return false;
                return result.GetProperty("verifiedAt").ValueKind == JsonValueKind.Null || origin == "SourceVerified" && Timestamp(Text(result, "verifiedAt"), false);
            case "lifecycle-status":
                if (!Exactly(result, "accountState", "evidence") || !Enum.GetNames<ProviderLifecycleAccountState>().Contains(Text(result, "accountState"))) return false;
                var evidence = result.GetProperty("evidence");
                return Exactly(evidence, "sourceAuthority", "mappingVersion", "snapshotVersion", "observedAt", "effectiveFrom", "effectiveUntil") &&
                    NeutralQueryContract.IsText(Text(evidence, "sourceAuthority"), 200) && NeutralQueryContract.IsText(Text(evidence, "mappingVersion"), 128) &&
                    NeutralQueryContract.IsText(Text(evidence, "snapshotVersion"), 128) && Timestamp(Text(evidence, "observedAt"), true) &&
                    Timestamp(Text(evidence, "effectiveFrom"), true) && Timestamp(Text(evidence, "effectiveUntil"), true);
            case "recovery-identity-verification":
                return Exactly(result, "outcome", "scheme") && Text(result, "scheme") == "identity-identifier" && Text(result, "outcome") is "Verified" or "Denied";
            case "password-sync":
                return Exactly(result, "outcome") && Text(result, "outcome") is "NoOp" or "Success" or "PartialSuccess" or "Failed" or "CommitUnknown";
            default: return false;
        }
    }

    public static JsonDocument Parse(ReadOnlyMemory<byte> body, int maximumBytes)
    {
        if (body.IsEmpty || body.Length > maximumBytes || body.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) throw new JsonException();
        _ = Utf8.GetCharCount(body.Span);
        var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
        try { CheckMembers(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void CheckMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || !ValidUnicode(property.Name)) throw new JsonException();
                CheckMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) CheckMembers(value);
        else if (element.ValueKind == JsonValueKind.String && !ValidUnicode(element.GetString()!)) throw new JsonException();
    }
    private static bool ValidUnicode(string value) { try { _ = Utf8.GetByteCount(value); return true; } catch (EncoderFallbackException) { return false; } }
    private static bool IsParseFailure(Exception ex) => ex is JsonException or DecoderFallbackException or InvalidOperationException or ArgumentException or KeyNotFoundException;
    private static bool Origin(string? text) => text is "Unknown" or "SourceVerified" or "PolicyTrusted";
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Exactly(JsonElement element, params string[] names) => element.ValueKind == JsonValueKind.Object &&
        element.EnumerateObject().Count() == names.Length && names.All(x => element.TryGetProperty(x, out _));
    private static bool OptionalText(JsonElement value, int maximum) => value.ValueKind == JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.String && NeutralQueryContract.IsText(value.GetString(), maximum, false);
    private static bool Timestamp(string? text, bool exact) => text is not null && (exact
        ? DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _)
        : System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$") &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
}
