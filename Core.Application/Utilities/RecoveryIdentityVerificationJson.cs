using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;

namespace Core.Application.Utilities;

/// <summary>Strict RIV wire boundary. No parser exception or input value is returned as a diagnostic.</summary>
public static class RecoveryIdentityVerificationJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = RecoveryIdentityVerificationContract.MaximumJsonDepth,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    public static bool TrySerializeRequest(RecoveryIdentityVerificationRequest? request, out byte[] body)
    {
        body = [];
        if (request is null || !request.IsValid()) return false;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", request.ContractVersion);
            writer.WriteString("requestId", request.RequestId);
            writer.WriteString("providerNamespace", request.ProviderNamespace);
            writer.WriteString("stableSubject", request.StableSubject);
            writer.WriteString("scheme", request.Scheme);
            writer.WriteStartObject("evidence");
            writer.WriteString("identityIdentifier", request.Evidence.IdentityIdentifier);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        if (stream.Length > RecoveryIdentityVerificationContract.MaximumRequestBytes) return false;
        body = stream.ToArray();
        return true;
    }

    public static bool TryDeserializeRequest(ReadOnlyMemory<byte> body, out RecoveryIdentityVerificationRequest? request)
    {
        request = null;
        if (!TryDocument(body, RecoveryIdentityVerificationContract.MaximumRequestBytes, out var document)) return false;
        using (document)
        {
            var root = document!.RootElement;
            if (!HasExactly(root, "contractVersion", "requestId", "providerNamespace", "stableSubject", "scheme", "evidence")) return false;
            var evidence = root.GetProperty("evidence");
            if (!HasExactly(evidence, "identityIdentifier")) return false;
            var candidate = new RecoveryIdentityVerificationRequest
            {
                ContractVersion = String(root, "contractVersion")!,
                RequestId = String(root, "requestId")!,
                ProviderNamespace = String(root, "providerNamespace")!,
                StableSubject = String(root, "stableSubject")!,
                Scheme = String(root, "scheme")!,
                Evidence = new() { IdentityIdentifier = String(evidence, "identityIdentifier")! }
            };
            if (!candidate.IsValid()) return false;
            request = candidate;
            return true;
        }
    }

    public static bool TryDeserializeResponse(ReadOnlyMemory<byte> body, out RecoveryIdentityVerificationResponse? response)
    {
        response = null;
        if (!TryDocument(body, RecoveryIdentityVerificationContract.MaximumResponseBytes, out var document)) return false;
        using (document)
        {
            var root = document!.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("outcome", out var outcomeElement) ||
                outcomeElement.ValueKind != JsonValueKind.String) return false;
            var outcomeText = String(root, "outcome");
            // Enum.TryParse alone also accepts numeric strings and is not a wire enum validator.
            if (!Enum.TryParse<RecoveryIdentityVerificationOutcome>(outcomeText, false, out var outcome) ||
                !Enum.IsDefined(outcome) || outcome.ToString() != outcomeText) return false;
            var verified = outcome == RecoveryIdentityVerificationOutcome.Verified;
            if (!(verified
                ? HasExactly(root, "contractVersion", "requestId", "outcome", "binding")
                : HasExactly(root, "contractVersion", "requestId", "outcome"))) return false;
            var requestIdElement = root.GetProperty("requestId");
            if (requestIdElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return false;
            RecoveryIdentityVerificationBinding? binding = null;
            if (verified)
            {
                var element = root.GetProperty("binding");
                if (!HasExactly(element, "providerNamespace", "stableSubject", "scheme")) return false;
                binding = new()
                {
                    ProviderNamespace = String(element, "providerNamespace")!,
                    StableSubject = String(element, "stableSubject")!,
                    Scheme = String(element, "scheme")!
                };
            }
            var candidate = new RecoveryIdentityVerificationResponse
            {
                ContractVersion = String(root, "contractVersion")!,
                RequestId = String(root, "requestId"),
                Outcome = outcome,
                Binding = binding
            };
            if (!candidate.IsValid()) return false;
            response = candidate;
            return true;
        }
    }

    /// <summary>
    /// Validates the transport envelope and exact request correlation. Only a returned Verified
    /// outcome may advance to a local precheck; this method itself grants no authority.
    /// </summary>
    public static bool TryReadResponse(int statusCode, string? contentType, ReadOnlyMemory<byte> body,
        RecoveryIdentityVerificationRequest expected, out RecoveryIdentityVerificationResponse? response)
    {
        response = null;
        if (expected is null || !expected.IsValid() ||
            !MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(mediaType.CharSet) &&
             !string.Equals(mediaType.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
            !TryDeserializeResponse(body, out var candidate)) return false;
        var expectedStatus = candidate!.Outcome switch
        {
            RecoveryIdentityVerificationOutcome.Verified or RecoveryIdentityVerificationOutcome.Denied => 200,
            RecoveryIdentityVerificationOutcome.Unavailable => 503,
            RecoveryIdentityVerificationOutcome.Unsupported or RecoveryIdentityVerificationOutcome.Malformed => 400,
            _ => 0
        };
        if (statusCode != expectedStatus || candidate.RequestId != expected.RequestId) return false;
        if (candidate.Outcome == RecoveryIdentityVerificationOutcome.Verified &&
            (candidate.Binding!.ProviderNamespace != expected.ProviderNamespace ||
             candidate.Binding.StableSubject != expected.StableSubject ||
             candidate.Binding.Scheme != expected.Scheme)) return false;
        response = candidate;
        return true;
    }

    private static bool TryDocument(ReadOnlyMemory<byte> body, int maximumBytes, out JsonDocument? document)
    {
        document = null;
        if (body.IsEmpty || body.Length > maximumBytes) return false;
        try
        {
            // JsonDocument defers string decoding. Check UTF-8 and every decoded name/value now,
            // so unpaired escaped surrogates cannot become replacement characters later.
            _ = StrictUtf8.GetCharCount(body.Span);
            document = JsonDocument.Parse(body, DocumentOptions);
            if (HasValidStringsAndUniqueNames(document.RootElement)) return true;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            // Intentionally suppress parser diagnostics, which can contain submitted data.
        }
        document?.Dispose();
        document = null;
        return false;
    }

    private static bool HasValidStringsAndUniqueNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || !HasValidStringsAndUniqueNames(property.Value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (!HasValidStringsAndUniqueNames(item)) return false;
        }
        else if (element.ValueKind == JsonValueKind.String)
            _ = element.GetString();
        return true;
    }

    private static bool HasExactly(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object &&
        element.EnumerateObject().Count() == names.Length &&
        names.All(name => element.TryGetProperty(name, out _));

    private static string? String(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.String ? element.GetProperty(name).GetString() : null;
}
