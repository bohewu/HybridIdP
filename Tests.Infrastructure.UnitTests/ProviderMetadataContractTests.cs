using System.Text.Json;
using Core.Application.DTOs;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderMetadataContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("request.valid.json", true)]
    [InlineData("response.valid.json", true)]
    [InlineData("response-null.valid.json", true)]
    [InlineData("response-missing-version.invalid.json", false)]
    [InlineData("response-unsupported-version.invalid.json", false)]
    [InlineData("response-timestamp.invalid.json", false)]
    [InlineData("response-null-identity.invalid.json", false)]
    public void PublicFixture_ShouldMatchContractValidation(string file, bool valid)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProviderMetadataFixtures", file));
        try
        {
            var accepted = file.StartsWith("request", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<ProviderMetadataRequest>(json, JsonOptions)!.TryValidate(out _)
                : JsonSerializer.Deserialize<ProviderMetadataResult>(json, JsonOptions)!.TryValidate(out _);
            Assert.Equal(valid, accepted);
        }
        catch (JsonException)
        {
            Assert.False(valid);
        }
    }

    [Theory]
    [InlineData("contractVersion", "null")]
    [InlineData("contractVersion", "\"2.0\"")]
    [InlineData("providerNamespace", "null")]
    [InlineData("providerNamespace", "\" \"")]
    [InlineData("stableSubject", "null")]
    [InlineData("stableSubject", "\"\"")]
    public void Identity_ShouldRejectInvalidRequiredFields(string name, string value)
    {
        var json = $$"""{"contractVersion":"1.0","providerNamespace":"example.provider","stableSubject":"opaque-subject","{{name}}":{{value}}} """;
        Assert.False(JsonSerializer.Deserialize<ProviderMetadataRequest>(json, JsonOptions)!.TryValidate(out _));
        Assert.False(JsonSerializer.Deserialize<ProviderMetadataResult>(json, JsonOptions)!.TryValidate(out _));
    }

    [Fact]
    public void Request_ShouldRejectMissingVersionOnWire()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProviderMetadataRequest>(
            """{"providerNamespace":"example.provider","stableSubject":"opaque-subject"}""", JsonOptions));
    }

    [Theory]
    [InlineData(EmailTrustOrigin.Unknown, null, false, true)]
    [InlineData(EmailTrustOrigin.Unknown, "unverified", false, true)]
    [InlineData(EmailTrustOrigin.Unknown, "person@example.org", true, false)]
    [InlineData(EmailTrustOrigin.SourceVerified, null, false, false)]
    [InlineData(EmailTrustOrigin.SourceVerified, "not-an-address", false, false)]
    [InlineData(EmailTrustOrigin.SourceVerified, "person@example.org", false, true)]
    [InlineData(EmailTrustOrigin.SourceVerified, "person@example.org", true, true)]
    [InlineData(EmailTrustOrigin.PolicyTrusted, "person@example.org", false, true)]
    [InlineData(EmailTrustOrigin.PolicyTrusted, "person@example.org", true, false)]
    public void EmailEvidence_ShouldPreserveIndependentTrustSemantics(
        EmailTrustOrigin origin, string? email, bool timestamp, bool expected)
    {
        var result = new ProviderMetadataResult
        {
            ProviderNamespace = "example.provider",
            StableSubject = "opaque-subject",
            Email = email,
            EmailTrustOrigin = origin,
            VerifiedAt = timestamp ? DateTimeOffset.Parse("2026-09-01T12:30:00Z") : null
        };
        Assert.Equal(expected, result.TryValidate(out _));
    }

    [Theory]
    [InlineData("\"SourceVerified\"")]
    [InlineData("\"sourceverified\"")]
    [InlineData("1")]
    [InlineData("\"1\"")]
    public void TrustOrigin_ShouldRetainExistingTolerantReader(string origin)
    {
        var json = $$"""{"contractVersion":"1.0","providerNamespace":"example.provider","stableSubject":"opaque-subject","email":"person@example.org","emailTrustOrigin":{{origin}}} """;
        var result = JsonSerializer.Deserialize<ProviderMetadataResult>(json, JsonOptions)!;
        Assert.True(result.TryValidate(out _));
        Assert.Equal(EmailTrustOrigin.SourceVerified, result.EmailTrustOrigin);
    }

    [Theory]
    [InlineData("2026-09-01T12:30:00Z")]
    [InlineData("2026-09-01T12:30:00+02:00")]
    [InlineData("2026-09-01T12:30:00")]
    [InlineData("2026-09-01")]
    public void Timestamp_ShouldRetainExistingIsoReader(string timestamp)
    {
        var json = $$"""{"contractVersion":"1.0","providerNamespace":"example.provider","stableSubject":"opaque-subject","email":"person@example.org","emailTrustOrigin":"SourceVerified","verifiedAt":"{{timestamp}}"} """;
        Assert.True(JsonSerializer.Deserialize<ProviderMetadataResult>(json, JsonOptions)!.TryValidate(out _));
    }

    [Fact]
    public void Result_ShouldDiscardUnknownFieldsBeforeReserializing()
    {
        const string json = """{"contractVersion":"1.0","providerNamespace":"example.provider","stableSubject":"opaque-subject","legacyCategories":["synthetic-category"],"legacyStatus":"Complete","legacySourceTimestamp":"2026-09-01T00:00:00Z","legacyRoles":["synthetic-role"],"legacyGroups":["synthetic-group"],"legacyIdentityKey":"ignored"}""";
        var result = JsonSerializer.Deserialize<ProviderMetadataResult>(json, JsonOptions)!;
        Assert.True(result.TryValidate(out _));
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOptions));
        Assert.Equal(
            ["contractVersion", "providerNamespace", "stableSubject", "email", "emailTrustOrigin", "verifiedAt"],
            serialized.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(EmailTrustOrigin.Unknown, result.EmailTrustOrigin);
        Assert.Null(result.VerifiedAt);
    }
}
