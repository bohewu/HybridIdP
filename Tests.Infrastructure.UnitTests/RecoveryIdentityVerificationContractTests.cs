using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Application.DTOs;
using Core.Application.Utilities;
using Xunit;
using FakeClient = Tests.Infrastructure.UnitTests.Fakes.RecoveryIdentityVerificationClient;

namespace Tests.Infrastructure.UnitTests;

public sealed class RecoveryIdentityVerificationContractTests
{
    private const string RequestId = "73aab4b5-2dfa-471a-8c2b-1d70d7540092";

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HybridAuthIdP.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository fixture directory unavailable.");
        }
    }

    private static JsonElement Fixture(int number)
    {
        var path = Path.Combine(RepositoryRoot, "docs", "examples", "recovery-identity-verification", $"C{number:00}.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static string RequestJson => Fixture(1).GetProperty("requestJson").GetString()!;
    private static string ResponseJson => Fixture(1).GetProperty("responseJson").GetString()!;

    private static RecoveryIdentityVerificationRequest Request(string identifier = "TEST-ID-0001") => new()
    {
        RequestId = RequestId,
        ProviderNamespace = "example.provider",
        StableSubject = "synthetic-subject-001",
        Evidence = new() { IdentityIdentifier = identifier }
    };

    [Fact]
    public void CanonicalBundle_ShouldRetainExactSchemaAndAllNamedCases()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot, "docs", "schemas", "recovery-identity-verification.schema.json"));
        Assert.Equal("2b9336d9ebda44098a706ad7fecd8af7239b5e0ae32c603d1a4cef8f843d47f1",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\r', bytes);
        for (var i = 1; i <= 28; i++)
        {
            var fixture = Fixture(i);
            Assert.Equal($"C{i:00}", fixture.GetProperty("id").GetString());
            Assert.Equal(i is >= 23 and <= 25 ? "consumer" : "both", fixture.GetProperty("owner").GetString());
        }
    }

    public static IEnumerable<object[]> RequestVariants()
    {
        foreach (var number in new[] { 8, 9, 11, 12, 26 })
            foreach (var variant in Fixture(number).GetProperty("requestVariants").EnumerateArray())
                yield return [$"C{number:00}/{variant.GetProperty("name").GetString()}",
                    variant.GetProperty("json").GetString()!, variant.GetProperty("valid").GetBoolean()];
    }

    [Theory]
    [MemberData(nameof(RequestVariants))]
    public void RequestParser_ShouldHonorSyntheticWireCases(string caseId, string json, bool expected)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseId));
        Assert.Equal(expected, RecoveryIdentityVerificationJson.TryDeserializeRequest(Bytes(json), out var parsed));
        Assert.Equal(expected, parsed is not null);
    }

    public static IEnumerable<object[]> ResponseCases()
    {
        for (var number = 1; number <= 28; number++)
        {
            var fixture = Fixture(number);
            if (fixture.GetProperty("httpStatus").ValueKind != JsonValueKind.Number) continue;
            yield return [$"C{number:00}", fixture.GetProperty("httpStatus").GetInt32(),
                fixture.GetProperty("contentType").GetString()!, fixture.GetProperty("responseJson").GetString()!,
                fixture.GetProperty("consumerAcceptsEnvelope").GetBoolean()];
        }
    }

    [Theory]
    [MemberData(nameof(ResponseCases))]
    public void ResponseParser_ShouldHonorSyntheticEnvelopes(string caseId, int status, string mediaType, string json, bool expected)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseId));
        Assert.Equal(expected, RecoveryIdentityVerificationJson.TryReadResponse(status, mediaType, Bytes(json), Request(), out var result));
        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public void Serialization_ShouldPreserveOriginalEvidenceAndOpaqueBinding()
    {
        var request = Request("  test-id-0001  ") with { ProviderNamespace = " example.Provider ", StableSubject = " Subject-A " };
        Assert.True(RecoveryIdentityVerificationJson.TrySerializeRequest(request, out var bytes));
        Assert.True(RecoveryIdentityVerificationJson.TryDeserializeRequest(bytes, out var parsed));
        Assert.Equal(request, parsed);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(["contractVersion", "requestId", "providerNamespace", "stableSubject", "scheme", "evidence"],
            document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("  test-id-0001  ", document.RootElement.GetProperty("evidence").GetProperty("identityIdentifier").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("73AAB4B5-2DFA-471A-8C2B-1D70D7540092")]
    [InlineData("73aab4b52dfa471a8c2b1d70d7540092")]
    [InlineData("{73aab4b5-2dfa-471a-8c2b-1d70d7540092}")]
    [InlineData(" 73aab4b5-2dfa-471a-8c2b-1d70d7540092")]
    public void RequestId_ShouldRejectNoncanonicalOrZeroUuid(string requestId)
    {
        Assert.False((Request() with { RequestId = requestId }).IsValid());
        Assert.False(RecoveryIdentityVerificationJson.TryDeserializeRequest(
            Bytes(RequestJson.Replace(RequestId, requestId, StringComparison.Ordinal)), out _));
        Assert.False(RecoveryIdentityVerificationJson.TryDeserializeResponse(
            Bytes(ResponseJson.Replace(RequestId, requestId, StringComparison.Ordinal)), out _));
    }

    [Theory]
    [InlineData("providerNamespace", 200)]
    [InlineData("stableSubject", 256)]
    public void Binding_ShouldCountUnicodeScalarsAndRejectInvalidValues(string property, int limit)
    {
        var value = string.Concat(Enumerable.Repeat("\U0001F642", limit));
        var request = property == "providerNamespace" ? Request() with { ProviderNamespace = value } : Request() with { StableSubject = value };
        Assert.True(RecoveryIdentityVerificationJson.TrySerializeRequest(request, out var wire));
        Assert.True(RecoveryIdentityVerificationJson.TryDeserializeRequest(wire, out _));
        foreach (var invalid in new[] { value + "X", "", "control\0", "\uD800", "\uDC00" })
        {
            var changed = property == "providerNamespace" ? request with { ProviderNamespace = invalid } : request with { StableSubject = invalid };
            Assert.False(RecoveryIdentityVerificationJson.TrySerializeRequest(changed, out _));
            var response = JsonNode.Parse(ResponseJson)!;
            response["binding"]![property] = invalid;
            // Direct validation is necessary: a general JSON serializer can replace invalid UTF-16.
            var binding = property == "providerNamespace"
                ? new RecoveryIdentityVerificationBinding { ProviderNamespace = invalid, StableSubject = "subject" }
                : new RecoveryIdentityVerificationBinding { ProviderNamespace = "provider", StableSubject = invalid };
            Assert.False(binding.IsValid());
        }
    }

    [Fact]
    public void Evidence_ShouldRejectInvalidUtf16BeforeAnySerializerCanReplaceIt()
    {
        foreach (var value in new[] { "\uD800", "\uDC00", "\uD800X", "\uDC00\uD800", "\u0085", "\u009F" })
        {
            Assert.False(RecoveryIdentityVerificationJson.TrySerializeRequest(Request(value), out var body));
            Assert.Empty(body);
        }
        Assert.True(Request(string.Concat(Enumerable.Repeat("\U0001F642", 128))).IsValid());
        Assert.False(Request(string.Concat(Enumerable.Repeat("\U0001F642", 129))).IsValid());
    }

    [Fact]
    public void Parser_ShouldRejectInvalidUtf8AndEscapedSurrogatesWithoutLeakingDiagnostics()
    {
        var malformedUtf8 = Bytes(RequestJson.Replace("TEST-ID-0001", "X", StringComparison.Ordinal));
        malformedUtf8[Array.IndexOf(malformedUtf8, (byte)'X')] = 0xFF;
        Assert.False(RecoveryIdentityVerificationJson.TryDeserializeRequest(malformedUtf8, out _));
        foreach (var json in new[]
        {
            RequestJson.Replace("TEST-ID-0001", "\\uD800"),
            RequestJson.Replace("requestId", "\\uD800"),
            ResponseJson.Replace("synthetic-subject-001", "\\uDC00"),
            ResponseJson.Replace(RequestId, "\\uD800")
        })
        {
            Assert.False(RecoveryIdentityVerificationJson.TryDeserializeRequest(Bytes(json), out _));
            Assert.False(RecoveryIdentityVerificationJson.TryDeserializeResponse(Bytes(json), out _));
        }
    }

    [Fact]
    public void BodyLimits_ShouldAcceptExactLimitAndRejectOneByteOver()
    {
        Assert.True(RecoveryIdentityVerificationJson.TryDeserializeRequest(Bytes(RequestJson.PadRight(8192)), out _));
        Assert.False(RecoveryIdentityVerificationJson.TryDeserializeRequest(Bytes(RequestJson.PadRight(8193)), out _));
        Assert.True(RecoveryIdentityVerificationJson.TryDeserializeResponse(Bytes(ResponseJson.PadRight(4096)), out _));
        Assert.False(RecoveryIdentityVerificationJson.TryDeserializeResponse(Bytes(ResponseJson.PadRight(4097)), out _));
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        foreach (var number in new[] { 15, 16 })
            foreach (var variant in Fixture(number).GetProperty("responseVariants").EnumerateArray())
                yield return [variant.GetProperty("json").GetString()!];
        yield return [ResponseJson.Replace("\"Verified\"", "\"verified\"")];
        yield return [ResponseJson.Replace("\"Verified\"", "\"0\"")];
        yield return [ResponseJson.Replace("\"Verified\"", "0")];
        yield return [ResponseJson.Replace("\"1.0\"", "\"2.0\"")];
        yield return [ResponseJson.Replace("contractVersion", "ContractVersion")];
        yield return [ResponseJson.Replace("synthetic-subject-001", "Synthetic-subject-001")];
        yield return [ResponseJson.Replace("example.provider", " example.provider")];
        yield return [ResponseJson.Replace("\"binding\":{", "\"binding\":{\"scheme\":\"identity-identifier\",")];
        yield return [ResponseJson[..^1] + ",\"outcome\":\"Verified\"}"];
        yield return [ResponseJson[..^1] + ",\"out\\u0063ome\":\"Verified\"}"];
        yield return [ResponseJson[..^1] + ",\"email\":\"synthetic@example.invalid\"}"];
        yield return ["[]"];
        yield return ["[" + ResponseJson + "]"];
        yield return [ResponseJson + ResponseJson];
        yield return [ResponseJson[..^1] + ",}"];
        yield return ["/* comment */" + ResponseJson];
        yield return [new string('[', 9) + "0" + new string(']', 9)];
        foreach (var property in new[] { "contractVersion", "requestId", "outcome", "binding" })
        {
            var missing = JsonNode.Parse(ResponseJson)!;
            missing.AsObject().Remove(property);
            yield return [missing.ToJsonString()];
            var nulled = JsonNode.Parse(ResponseJson)!;
            nulled[property] = null;
            yield return [nulled.ToJsonString()];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public void ResponseParser_ShouldRejectMalformedOrUnboundSuccess(string json) =>
        Assert.False(RecoveryIdentityVerificationJson.TryReadResponse(200, "application/json", Bytes(json), Request(), out _));

    [Theory]
    [InlineData("Denied", 200)]
    [InlineData("Unavailable", 503)]
    [InlineData("Unsupported", 400)]
    [InlineData("Malformed", 400)]
    public void NonSuccess_ShouldRequireExactShapeStatusAndCorrelation(string outcome, int status)
    {
        var response = JsonNode.Parse(ResponseJson)!.AsObject();
        response.Remove("binding");
        response["outcome"] = outcome;
        Assert.True(RecoveryIdentityVerificationJson.TryReadResponse(status, "application/json", Bytes(response.ToJsonString()), Request(), out _));
        foreach (var property in new[] { "binding", "email", "identityIdentifier", "stableSubject", "birthDate" })
        {
            response[property] = null;
            Assert.False(RecoveryIdentityVerificationJson.TryDeserializeResponse(Bytes(response.ToJsonString()), out _));
            response.Remove(property);
        }
        response["requestId"] = null;
        Assert.Equal(outcome == "Malformed", RecoveryIdentityVerificationJson.TryDeserializeResponse(Bytes(response.ToJsonString()), out _));
        Assert.False(RecoveryIdentityVerificationJson.TryReadResponse(status, "application/json", Bytes(response.ToJsonString()), Request(), out _));
        response["requestId"] = 12;
        Assert.False(RecoveryIdentityVerificationJson.TryDeserializeResponse(Bytes(response.ToJsonString()), out _));
    }

    [Theory]
    [InlineData(200, "application/json", true)]
    [InlineData(200, "application/json; charset=utf-8", true)]
    [InlineData(200, "Application/Json", true)]
    [InlineData(200, "text/html", false)]
    [InlineData(200, "application/problem+json", false)]
    [InlineData(200, "application/json; charset=utf-16", false)]
    [InlineData(200, null, false)]
    [InlineData(201, "application/json", false)]
    [InlineData(302, "application/json", false)]
    [InlineData(400, "application/json", false)]
    [InlineData(401, "application/json", false)]
    [InlineData(403, "application/json", false)]
    [InlineData(413, "application/json", false)]
    [InlineData(415, "application/json", false)]
    [InlineData(429, "application/json", false)]
    [InlineData(503, "application/json", false)]
    public void Envelope_ShouldRequireCorrectStatusAndMediaType(int status, string? mediaType, bool expected) =>
        Assert.Equal(expected, RecoveryIdentityVerificationJson.TryReadResponse(status, mediaType, Bytes(ResponseJson), Request(), out _));

    [Fact]
    public void SensitiveDtos_ShouldRedactEveryDiagnosticString()
    {
        var request = Request("SENSITIVE-IDENTIFIER");
        var binding = new RecoveryIdentityVerificationBinding { ProviderNamespace = "SENSITIVE-PROVIDER", StableSubject = "SENSITIVE-SUBJECT" };
        var response = new RecoveryIdentityVerificationResponse { RequestId = RequestId, Binding = binding };
        foreach (var value in new object[] { request, request.Evidence, binding, response })
        {
            Assert.DoesNotContain("SENSITIVE", value.ToString());
            Assert.DoesNotContain(RequestId, value.ToString());
        }
    }

    [Fact]
    public void RequestParser_ShouldRejectBrowserAuthorizationFields()
    {
        var request = JsonNode.Parse(RequestJson)!;
        foreach (var extra in Fixture(28).GetProperty("browserSubmission").EnumerateObject())
        {
            if (extra.Name == "scheme") continue; // Server-supplied wire scheme is valid; browser authority is a later gate.
            request[extra.Name] = JsonNode.Parse(extra.Value.GetRawText());
            Assert.False(RecoveryIdentityVerificationJson.TryDeserializeRequest(Bytes(request.ToJsonString()), out _));
            request.AsObject().Remove(extra.Name);
        }
    }

    [Fact]
    public async Task FakeClient_ShouldReverifyRepeatedCorrelationWithoutCaching()
    {
        var fake = new FakeClient((_, _) => Task.FromResult((200, (string?)"application/json", Bytes(ResponseJson))));
        Assert.Equal(RecoveryIdentityVerificationOutcome.Verified, (await fake.VerifyAsync(Request()))!.Outcome);
        Assert.Equal(RecoveryIdentityVerificationOutcome.Verified, (await fake.VerifyAsync(Request()))!.Outcome);
        Assert.Equal(2, fake.Calls);
        Assert.Null(await fake.VerifyAsync(Request("")));
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task FakeClient_ShouldHonorCancellationBeforeAndAfterResponseWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var fake = new FakeClient((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult((200, (string?)"application/json", Bytes(ResponseJson)));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.VerifyAsync(Request(), cancellation.Token));
        Assert.Equal(1, fake.Calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.VerifyAsync(Request(), cancellation.Token));
        Assert.Equal(1, fake.Calls);
    }
}
