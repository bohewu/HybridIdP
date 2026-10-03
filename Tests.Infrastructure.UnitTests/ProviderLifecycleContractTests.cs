using System.Text;
using System.Text.Json.Nodes;
using Core.Application.DTOs;
using Core.Application.Utilities;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderLifecycleContractTests
{
    private const string Id = "73aab4b5-2dfa-471a-8c2b-1d70d7540092";
    private static ProviderLifecycleRequest Request => new() { RequestId = Id,
        Binding = new() { ProviderNamespace = "example.provider", StableSubject = "subject-001" } };
    private const string Found = """
        {"contractType":"lifecycle-status","contractVersion":"1.0","requestId":"73aab4b5-2dfa-471a-8c2b-1d70d7540092","outcome":"Found",
        "binding":{"providerNamespace":"example.provider","stableSubject":"subject-001"},"accountState":"Enabled",
        "evidence":{"sourceAuthority":"authority-1","mappingVersion":"mapping-1","snapshotVersion":"snapshot-1",
        "observedAt":"2026-10-03T00:00:00.000Z","effectiveFrom":"2026-10-02T00:00:00.000Z","effectiveUntil":"2026-10-04T00:00:00.000Z"}}
        """;
    private static bool Read(string json, int status = 200, string? media = "application/json") =>
        ProviderLifecycleJson.TryReadResponse(status, media, Encoding.UTF8.GetBytes(json), Request, out _);

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Disabled")]
    [InlineData("Retired")]
    [InlineData("Superseded")]
    [InlineData("Unknown")]
    public void Read_ShouldAcceptExactStates(string state) => Assert.True(Read(Found.Replace("Enabled", state)));

    [Theory]
    [InlineData("NotFound", 200)]
    [InlineData("Ambiguous", 200)]
    [InlineData("Unavailable", 503)]
    [InlineData("Unsupported", 400)]
    [InlineData("Malformed", 400)]
    public void Read_ShouldRequireExactNonFoundStatusAndNoFoundFields(string outcome, int status)
    {
        var json = JsonNode.Parse(Found)!.AsObject();
        json["outcome"] = outcome;
        foreach (var key in new[] { "binding", "evidence", "accountState" }) json.Remove(key);
        Assert.True(Read(json.ToJsonString(), status));
        Assert.False(Read(json.ToJsonString(), status == 200 ? 503 : 200));
        foreach (var forbidden in new[] { "binding", "accountState", "evidence", "successor" })
        {
            json[forbidden] = null;
            Assert.False(Read(json.ToJsonString(), status));
            json.Remove(forbidden);
        }
        json["requestId"] = null;
        Assert.False(Read(json.ToJsonString(), status));
        Assert.Equal(outcome == "Malformed", ProviderLifecycleJson.TryDeserializeResponse(Encoding.UTF8.GetBytes(json.ToJsonString()), out _));
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("Application/JSON; charset=\"UTF-8\"", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("application/json; charset=utf-16", false)]
    [InlineData("application/json; charset=utf-8; extra=x", false)]
    [InlineData("application/json; charset=utf-8; charset=utf-8", false)]
    [InlineData("application/json; extra=x", false)]
    [InlineData("application/problem+json", false)]
    [InlineData("text/json", false)]
    [InlineData(null, false)]
    public void Read_ShouldRequireExactMediaParameters(string? media, bool valid) => Assert.Equal(valid, Read(Found, media: media));

    [Theory]
    [InlineData("contractType", "other")]
    [InlineData("contractVersion", "2.0")]
    [InlineData("outcome", "found")]
    [InlineData("outcome", "0")]
    [InlineData("accountState", "enabled")]
    [InlineData("accountState", "0")]
    [InlineData("requestId", "73AAB4B5-2DFA-471A-8C2B-1D70D7540092")]
    [InlineData("requestId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("requestId", "73aab4b5-2dfa-471a-8c2b-1d70d7540093")]
    public void Read_ShouldRejectEnvelopeSubstitutions(string key, string value)
    {
        var json = JsonNode.Parse(Found)!;
        json[key] = value;
        Assert.False(Read(json.ToJsonString()));
    }

    [Theory]
    [InlineData("0000-01-01T00:00:00.000Z")]
    [InlineData("2026-02-29T00:00:00.000Z")]
    [InlineData("2026-10-03T00:00:60.000Z")]
    [InlineData("2026-10-03T00:00:00Z")]
    [InlineData("2026-10-03T00:00:00.000+00:00")]
    [InlineData("2026-10-03T00:00:00.000z")]
    [InlineData("2026-10-03T00:00:00.0000Z")]
    public void Read_ShouldRejectNonCanonicalOrInvalidTimestamp(string value)
    {
        foreach (var name in new[] { "observedAt", "effectiveFrom", "effectiveUntil" })
        {
            var json = JsonNode.Parse(Found)!;
            json["evidence"]![name] = value;
            Assert.False(Read(json.ToJsonString()));
        }
    }

    [Fact]
    public void Read_ShouldLeaveFreshnessAndTimeOrderingToActionPolicy()
    {
        var json = JsonNode.Parse(Found)!;
        json["evidence"]!["observedAt"] = "9999-12-31T23:59:59.999Z";
        json["evidence"]!["effectiveUntil"] = "0001-01-01T00:00:00.000Z";
        Assert.True(Read(json.ToJsonString()));
    }

    [Fact]
    public void Read_ShouldRejectUnknownDuplicateEscapedEquivalentAndMissingMembers()
    {
        foreach (var prefix in new[] { "\"outcome\":\"Found\",", "\"outc\\u006fme\":\"Found\",", "\"extra\":1,", "\"Outcome\":\"Found\"," })
            Assert.False(Read(Found.Insert(1, prefix)));
        foreach (var name in JsonNode.Parse(Found)!.AsObject().Select(p => p.Key))
        {
            var json = JsonNode.Parse(Found)!.AsObject();
            json.Remove(name);
            Assert.False(Read(json.ToJsonString()));
        }
        Assert.False(Read(Found.Replace("\"sourceAuthority\":", "\"sourceAuthority\":\"x\",\"sourceAuthority\":")));
        Assert.False(Read(Found.Replace("\"binding\":{", "\"binding\":{\"extra\":true,")));
    }

    [Fact]
    public void Read_ShouldRejectMalformedJsonUnicodeBomAndDepth()
    {
        foreach (var value in new[] { "\\uD800", "\\uDC00", "\\u0000", "\\u0085", "   " })
            Assert.False(Read(Found.Replace("subject-001", value)));
        foreach (var json in new[] { "\ufeff" + Found, Found + "{}", Found[..^1] + ",}", "/*comment*/" + Found,
            "[" + Found + "]", Found.Replace("\"snapshot-1\"", "[[[[[[[[[0]]]]]]]]]") }) Assert.False(Read(json));
        var bytes = Encoding.UTF8.GetBytes(Found);
        bytes[Array.IndexOf(bytes, (byte)'s')] = 0xff;
        Assert.False(ProviderLifecycleJson.TryDeserializeResponse(bytes, out _));
        Assert.False(Read(Found.Insert(1, "\"\\uD800\":1,")));
    }

    [Fact]
    public void Read_ShouldEnforceSuccessorRestrictions()
    {
        var json = JsonNode.Parse(Found)!;
        json["successor"] = JsonNode.Parse("{\"providerNamespace\":\"example.provider\",\"stableSubject\":\"new-subject\"}");
        Assert.False(Read(json.ToJsonString()));
        foreach (var state in new[] { "Retired", "Superseded" })
        {
            json["accountState"] = state;
            Assert.True(Read(json.ToJsonString()));
        }
        json["successor"]!["stableSubject"] = "subject-001";
        Assert.False(Read(json.ToJsonString()));
        json["successor"] = null;
        Assert.False(Read(json.ToJsonString()));
    }

    [Fact]
    public void Request_ShouldEnforceOpaqueScalarAndWireByteLimits()
    {
        Assert.True(ProviderLifecycleContract.IsOpaque(string.Concat(Enumerable.Repeat("\U0001f642", 200)), 200));
        Assert.False(ProviderLifecycleContract.IsOpaque(string.Concat(Enumerable.Repeat("\U0001f642", 201)), 200));
        Assert.True(ProviderLifecycleContract.IsOpaque(" subject ", 256));
        foreach (var value in new[] { "", " \t", "\u0085", "\ud800", new string('a', 257) })
            Assert.False(ProviderLifecycleJson.TrySerializeRequest(Request with { Binding = Request.Binding with { StableSubject = value } }, out _));
        var escapedLarge = Request with { Binding = new() { ProviderNamespace = new string('<', 200), StableSubject = new string('<', 256) } };
        Assert.True(ProviderLifecycleJson.TrySerializeRequest(escapedLarge, out var bytes));
        Assert.InRange(bytes.Length, 1, 4096);
        var oversized = Request with { Binding = new() { ProviderNamespace = string.Concat(Enumerable.Repeat("\U0001f642", 200)),
            StableSubject = string.Concat(Enumerable.Repeat("\U0001f642", 256)) } };
        Assert.False(ProviderLifecycleJson.TrySerializeRequest(oversized, out _));
        Assert.True(Read(Found.PadRight(8192)));
        Assert.False(Read(Found.PadRight(8193)));
    }

    [Fact]
    public void Read_ShouldUseOrdinalTupleComparisonAndRedactDiagnostics()
    {
        Assert.False(Read(Found.Replace("example.provider", "Example.provider")));
        Assert.False(Read(Found.Replace("subject-001", " subject-001")));
        Assert.True(ProviderLifecycleJson.TryReadResponse(200, "application/json", Encoding.UTF8.GetBytes(Found), Request, out var result));
        foreach (var item in new object[] { Request, Request.Binding, result!, result!.Evidence! })
            Assert.Contains("[redacted]", item.ToString());
    }
}
