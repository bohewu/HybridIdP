using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;

namespace Core.Application.Utilities;

/// <summary>Strict lifecycle-only wire parsing. Does not grant lifecycle or local authority.</summary>
public static class ProviderLifecycleJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static bool TrySerializeRequest(ProviderLifecycleRequest? request, out byte[] body)
    {
        body = [];
        if (request is null || !request.IsValid()) return false;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contractType", ProviderLifecycleContract.Type);
            writer.WriteString("contractVersion", ProviderLifecycleContract.Version);
            writer.WriteString("requestId", request.RequestId);
            writer.WriteString("providerNamespace", request.Binding.ProviderNamespace);
            writer.WriteString("stableSubject", request.Binding.StableSubject);
            writer.WriteEndObject();
        }
        if (stream.Length > ProviderLifecycleContract.MaximumRequestBytes) return false;
        body = stream.ToArray();
        return true;
    }

    public static bool TryReadResponse(int statusCode, string? contentType, ReadOnlyMemory<byte> body,
        ProviderLifecycleRequest expected, out ProviderLifecycleResponse? response)
    {
        response = null;
        if (expected is null || !expected.IsValid() || !IsMediaType(contentType) ||
            !TryDeserializeResponse(body, out var candidate)) return false;
        var expectedStatus = candidate!.Outcome switch
        {
            ProviderLifecycleOutcome.Found or ProviderLifecycleOutcome.NotFound or ProviderLifecycleOutcome.Ambiguous => 200,
            ProviderLifecycleOutcome.Unavailable => 503,
            ProviderLifecycleOutcome.Unsupported or ProviderLifecycleOutcome.Malformed => 400,
            _ => 0
        };
        if (statusCode != expectedStatus || candidate.RequestId != expected.RequestId ||
            (candidate.Outcome == ProviderLifecycleOutcome.Found && candidate.Binding != expected.Binding)) return false;
        response = candidate;
        return true;
    }

    public static bool IsMediaType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var media) &&
        string.Equals(media.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
        (media.Parameters.Count == 0 || (media.Parameters.Count == 1 &&
            string.Equals(media.Parameters.Single().Name, "charset", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(media.Parameters.Single().Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)));

    public static bool TryDeserializeResponse(ReadOnlyMemory<byte> body, out ProviderLifecycleResponse? response)
    {
        response = null;
        if (body.IsEmpty || body.Length > ProviderLifecycleContract.MaximumResponseBytes ||
            body.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return false;
        try
        {
            _ = StrictUtf8.GetCharCount(body.Span);
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                MaxDepth = ProviderLifecycleContract.MaximumJsonDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var root = document.RootElement;
            if (!ValidStringsAndNames(root) || root.ValueKind != JsonValueKind.Object ||
                !TryEnum<ProviderLifecycleOutcome>(String(root, "outcome"), out var outcome)) return false;
            var found = outcome == ProviderLifecycleOutcome.Found;
            var successorPresent = root.TryGetProperty("successor", out var successorElement);
            if (!(found
                ? HasExactly(root, successorPresent
                    ? ["contractType", "contractVersion", "requestId", "outcome", "binding", "accountState", "evidence", "successor"]
                    : ["contractType", "contractVersion", "requestId", "outcome", "binding", "accountState", "evidence"])
                : HasExactly(root, "contractType", "contractVersion", "requestId", "outcome")) ||
                String(root, "contractType") != ProviderLifecycleContract.Type ||
                String(root, "contractVersion") != ProviderLifecycleContract.Version) return false;
            var requestId = String(root, "requestId");
            if (!ProviderLifecycleContract.IsRequestId(requestId) &&
                !(outcome == ProviderLifecycleOutcome.Malformed && root.GetProperty("requestId").ValueKind == JsonValueKind.Null)) return false;

            ProviderLifecycleBinding? binding = null;
            ProviderLifecycleBinding? successor = null;
            ProviderLifecycleEvidence? evidence = null;
            ProviderLifecycleAccountState? state = null;
            if (found)
            {
                binding = ReadBinding(root.GetProperty("binding"));
                if (binding is null || !TryEnum<ProviderLifecycleAccountState>(String(root, "accountState"), out var parsedState)) return false;
                state = parsedState;
                if (successorPresent)
                {
                    successor = ReadBinding(successorElement);
                    if (successor is null || successor == binding ||
                        state is not (ProviderLifecycleAccountState.Retired or ProviderLifecycleAccountState.Superseded)) return false;
                }
                var data = root.GetProperty("evidence");
                if (!HasExactly(data, "sourceAuthority", "mappingVersion", "snapshotVersion", "observedAt", "effectiveFrom", "effectiveUntil") ||
                    !ProviderLifecycleContract.IsOpaque(String(data, "sourceAuthority"), 200) ||
                    !ProviderLifecycleContract.IsOpaque(String(data, "mappingVersion"), 128) ||
                    !ProviderLifecycleContract.IsOpaque(String(data, "snapshotVersion"), 128) ||
                    !TryTimestamp(String(data, "observedAt"), out var observed) ||
                    !TryTimestamp(String(data, "effectiveFrom"), out var from) ||
                    !TryTimestamp(String(data, "effectiveUntil"), out var until)) return false;
                evidence = new()
                {
                    SourceAuthority = String(data, "sourceAuthority")!, MappingVersion = String(data, "mappingVersion")!,
                    SnapshotVersion = String(data, "snapshotVersion")!, ObservedAt = observed,
                    EffectiveFrom = from, EffectiveUntil = until
                };
            }
            response = new() { RequestId = requestId, Outcome = outcome, Binding = binding,
                AccountState = state, Evidence = evidence, Successor = successor };
            return true;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            // Parser diagnostics may contain source data; intentionally do not expose them.
            return false;
        }
    }

    private static bool TryTimestamp(string? value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParseExact(value, TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp) &&
        timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture) == value;

    private static ProviderLifecycleBinding? ReadBinding(JsonElement element)
    {
        if (!HasExactly(element, "providerNamespace", "stableSubject")) return null;
        var binding = new ProviderLifecycleBinding
        {
            ProviderNamespace = String(element, "providerNamespace")!, StableSubject = String(element, "stableSubject")!
        };
        return binding.IsValid() ? binding : null;
    }

    private static bool TryEnum<T>(string? text, out T value) where T : struct, Enum =>
        Enum.TryParse(text, false, out value) && Enum.IsDefined(value) && value.ToString() == text;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool HasExactly(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Count() == names.Length &&
        names.All(name => element.TryGetProperty(name, out _));

    private static bool ValidStringsAndNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || !ValidStringsAndNames(property.Value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray()) if (!ValidStringsAndNames(value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.String) _ = element.GetString();
        return true;
    }
}
