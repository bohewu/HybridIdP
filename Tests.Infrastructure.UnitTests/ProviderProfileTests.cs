using System.Net;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderProfileTests
{
    private static readonly Dictionary<string, string> Schema = new() { ["flag"] = "Boolean", ["code"] = "String" };

    [Fact]
    public async Task LegacySnapshot_ShouldRequireFreshConfirmationAfterQueryConfigurationRevisionChanges()
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = Core.Domain.Constants.SettingKeys.NeutralQueryConfiguration, Value = "{}" });
        await db.SaveChangesAsync();
        var revision = Guid.NewGuid();
        var query = new Mock<INeutralQueryService>();
        query.Setup(x => x.ResolveAsync("profile", user.Id, "source", null, It.IsAny<CancellationToken>())).ReturnsAsync(new NeutralQueryResolution("Legacy"));
        query.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new QueryConfigurationStateDto(revision, [], []));
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(Success("{\"code\":\"fresh\"}"))));
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)),
            Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = Source() } }), queries: query.Object);
        Assert.Single(await service.GetPropertiesAsync(user, "source"));
        revision = Guid.NewGuid();
        Assert.Empty((await service.ReadSnapshotAsync(user, "source")).Properties);
        Assert.Single(await service.GetPropertiesAsync(user, "source"));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task NeutralProfile_ShouldRefreshWithoutLoginBindingAndKeepPreviewReadOnlyAndInvalidateChangedQuery()
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        db.UserLogins.RemoveRange(db.UserLogins);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var context = new NeutralQueryContext(user.Id, user.PersonId, "source", new NeutralQueryRequest
        {
            ContractType = "profile", ProviderNamespace = "provider", Query = new(new Dictionary<string, string> { ["employeeId"] = "SYNTH-001" })
        }, "revision-1", Guid.NewGuid(), new(new Uri("https://example.test/query"), "synthetic-secret", TimeSpan.FromSeconds(5)), []);
        var queries = new Mock<INeutralQueryService>();
        queries.Setup(x => x.ResolveAsync("profile", user.Id, "source", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new NeutralQueryResolution("Ready", context));
        queries.Setup(x => x.IsCurrentAsync(It.IsAny<NeutralQueryContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var code = "initial";
        var unavailable = false;
        var handler = new UpstreamStubHandler(async (message, _) =>
        {
            Assert.Equal("profile/2.0", message.Headers.GetValues("X-Provider-Contract").Single());
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.TryGetProperty("stableSubject", out var ignoredSubject));
            var requestId = body.RootElement.GetProperty("requestId").GetString();
            return Response(JsonSerializer.Serialize(new { contractType = "profile", contractVersion = "2.0", requestId,
                providerNamespace = "provider", queryOutcome = unavailable ? "Unavailable" : "Found",
                result = new { extraProperties = new { code, flag = true, unapproved = "ignored" } } }),
                unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        var options = Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = Source() },
            QuerySources = new() { ["source"] = new() } });
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)), options, queries: queries.Object);
        await service.RefreshAfterLoginAsync(user, "unrelated-login", "unrelated-subject");
        Assert.Equal("initial", (await service.ReadSnapshotAsync(user, "source")).Properties["code"].GetString());
        var stored = (await db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson;
        db.ChangeTracker.Clear();
        code = "preview";
        Assert.Equal("preview", (await service.ReadForPreviewAsync(user, "source")).Properties["code"].GetString());
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(stored, (await db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
        Assert.Equal("initial", (await service.GetPropertiesAsync(user, "source"))["code"].GetString());
        context = context with { Fingerprint = "revision-2" };
        Assert.Empty((await service.ReadSnapshotAsync(user, "source")).Properties);
        unavailable = true;
        Assert.Empty(await service.GetPropertiesAsync(user, "source"));
        Assert.Empty((await service.ReadSnapshotAsync(user, "source")).Properties);
        Assert.Empty(await db.UserLogins.ToListAsync());
    }

    [Theory]
    [InlineData("MissingInput")]
    [InlineData("Unavailable")]
    public async Task NeutralProfile_ShouldNotFallbackToLegacyOrContactSourceWhenQueryUnavailable(string status)
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var queries = new Mock<INeutralQueryService>();
        queries.Setup(x => x.ResolveAsync("profile", user.Id, "source", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NeutralQueryResolution(status));
        var handler = new UpstreamStubHandler((_, _) => throw new InvalidOperationException("must not fetch"));
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)),
            Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = Source() } }), queries: queries.Object);
        Assert.Empty(await service.GetPropertiesAsync(user, "source"));
        Assert.Empty((await service.ReadForPreviewAsync(user, "source")).Properties);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task FetchAsync_ShouldPreserveMixedScalarTypesAndNormalizeArraySets()
    {
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(Success(
            "{\"flag\":true,\"code\":\"4\",\"categories\":[\"b\",\" a\",\"B\",\"b\",\"\"],\"empty\":[]}"))));
        var result = await new ProviderProfileClient(new HttpClient(handler)).FetchAsync(Request(), Source(), CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result.ExtraProperties["flag"].GetBoolean());
        Assert.Equal("4", result.ExtraProperties["code"].GetString());
        Assert.Equal(new[] { "", " a", "B", "b" }, result.ExtraProperties["categories"].EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(0, result.ExtraProperties["empty"].GetArrayLength());
        Assert.False(result.ExtraProperties.ContainsKey("missing"));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("[true]")]
    [InlineData("[{}]")]
    [InlineData("[[]]")]
    [InlineData("[\"valid\",1]")]
    public async Task FetchAsync_ShouldRejectEntireResponseForInvalidUnapprovedArray(string array)
    {
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(Success("{\"flag\":true,\"unapproved\":" + array + "}"))));
        Assert.Null(await new ProviderProfileClient(new HttpClient(handler)).FetchAsync(Request(), Source(), CancellationToken.None));
    }

    [Theory]
    [InlineData(32, 1024, true)]
    [InlineData(33, 1, false)]
    [InlineData(1, 1025, false)]
    public async Task FetchAsync_ShouldEnforceRawCountBeforeDedupAndUtf16Length(int count, int length, bool accepted)
    {
        var value = new string('a', length);
        var json = JsonSerializer.Serialize(new { categories = Enumerable.Repeat(value, count).ToArray() });
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(Success(json))));
        var result = await new ProviderProfileClient(new HttpClient(handler)).FetchAsync(Request(), Source(), CancellationToken.None);
        Assert.Equal(accepted, result is not null);
        if (result is not null) Assert.Equal(1, result.ExtraProperties["categories"].GetArrayLength());
        var supplementary = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600), 513));
        Assert.False(ProviderProfileContract.IsValueOfType(JsonSerializer.SerializeToElement(new[] { supplementary }), "StringArray"));
    }

    [Fact]
    public void Hash_ShouldNormalizeArraySetsAndDistinguishEmptyAbsentAndScalar()
    {
        string Hash(string json) => ProviderProfileService.ComputePropertyHash(Properties(json));
        Assert.Equal(Hash("{\"categories\":[\"b\",\"a\",\"a\"]}"), Hash("{\"categories\":[\"a\",\"b\"]}"));
        Assert.NotEqual(Hash("{\"categories\":[\"a\"]}"), Hash("{\"categories\":[\"A\"]}"));
        Assert.NotEqual(Hash("{\"categories\":[\"a\"]}"), Hash("{\"categories\":[\" a\"]}"));
        Assert.NotEqual(Hash("{\"categories\":[\"a\"]}"), Hash("{\"categories\":\"a\"}"));
        Assert.NotEqual(Hash("{\"categories\":[]}"), Hash("{}"));
    }

    [Fact]
    public async Task GetPropertiesAsync_ShouldRequireNewConfirmationAfterScalarToArrayPolicyChange()
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var source = Source();
        var json = Success("{\"code\":\"4\"}");
        var status = HttpStatusCode.OK;
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(json, status)));
        var config = Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = source } });
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)), config);
        Assert.Equal("4", (await service.GetPropertiesAsync(user, "source"))["code"].GetString());
        source.AllowedProperties["code"] = "StringArray";
        status = HttpStatusCode.ServiceUnavailable;
        Assert.Empty(await service.GetPropertiesAsync(user, "source"));
        Assert.Equal(2, handler.CallCount);
        Assert.DoesNotContain("\"code\":\"4\"", db.Persons.Single().ProviderProfilesJson!);
        status = HttpStatusCode.OK;
        json = Success("{\"code\":[]}");
        // A new issuance scope retries the failed confirmation.
        service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)), config);
        Assert.Equal(0, (await service.GetPropertiesAsync(user, "source"))["code"].GetArrayLength());
        source.AllowedProperties["flag"] = "StringArray";
        json = Success("{}");
        Assert.Empty(await service.GetPropertiesAsync(user, "source"));
        Assert.DoesNotContain("\"code\":[]", db.Persons.Single().ProviderProfilesJson!);
    }

    [Theory]
    [InlineData("{\"contractVersion\":\"2.0\",\"providerNamespace\":\"provider\",\"stableSubject\":\"subject\",\"extraProperties\":{}}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"providerNamespace\":\"Provider\",\"stableSubject\":\"subject\",\"extraProperties\":{}}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"providerNamespace\":\"provider\",\"stableSubject\":\"other\",\"extraProperties\":{}}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"providerNamespace\":\"provider\",\"stableSubject\":\"subject\"}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"providerNamespace\":\"provider\",\"stableSubject\":\"subject\",\"extraProperties\":{\"flag\":null}}")]
    [InlineData("{\"contractVersion\":\"1.0\",\"providerNamespace\":\"provider\",\"stableSubject\":\"subject\",\"extraProperties\":{\"flag\":true,\"flag\":false}}")]
    public async Task FetchAsync_ShouldRejectInvalidContractOrIdentity(string json)
    {
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(json)));
        var result = await new ProviderProfileClient(new HttpClient(handler)).FetchAsync(
            Request(), Source(), CancellationToken.None);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchAsync_ShouldRejectNonSuccessWithoutRetry(HttpStatusCode status)
    {
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        Assert.Null(await new ProviderProfileClient(new HttpClient(handler)).FetchAsync(Request(), Source(), CancellationToken.None));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Hash_ShouldIgnoreOrderAndDetectTypesValuesAndRemovals()
    {
        var first = Properties("{\"flag\":true,\"code\":\"4\"}");
        Assert.Equal(ProviderProfileService.ComputePropertyHash(first),
            ProviderProfileService.ComputePropertyHash(Properties("{\"code\":\"4\",\"flag\":true}")));
        Assert.Equal(ProviderProfileService.ComputePropertyHash(first),
            ProviderProfileService.ComputePropertyHash(Properties("{\"code\":\"\\u0034\",\"flag\":true}")));
        Assert.NotEqual(ProviderProfileService.ComputePropertyHash(first),
            ProviderProfileService.ComputePropertyHash(Properties("{\"code\":\"4\",\"flag\":\"true\"}")));
        Assert.NotEqual(ProviderProfileService.ComputePropertyHash(first),
            ProviderProfileService.ComputePropertyHash(Properties("{\"flag\":true}")));
    }

    [Fact]
    public async Task GetPropertiesAsync_ShouldReplaceRemovedKeysAndInvalidateFailedConfirmation()
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var now = new TestClock();
        var json = Success("{\"flag\":true,\"code\":\"4\",\"unapproved\":\"discard\"}");
        var status = HttpStatusCode.OK;
        var handler = new UpstreamStubHandler((message, _) =>
        {
            Assert.Equal("synthetic-profile-secret", message.Headers.GetValues("X-Internal-Secret").Single());
            return Task.FromResult(Response(json, status));
        });
        var config = Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = Source() } });
        ProviderProfileService Create() => new(db, new ProviderProfileClient(new HttpClient(handler)), config, now);
        var first = await Create().GetPropertiesAsync(user, "source");
        Assert.Equal(2, first.Count);
        Assert.False(first.ContainsKey("unapproved"));
        var stored = db.Persons.Single().ProviderProfilesJson!;
        Assert.Single(await Task.WhenAll(Create().GetPropertiesAsync(user, "source")));
        Assert.Equal(1, handler.CallCount);
        now.Now += TimeSpan.FromMinutes(6);
        json = Success("{\"flag\":false}");
        var changed = await Create().GetPropertiesAsync(user, "source");
        Assert.False(changed.ContainsKey("code"));
        Assert.False(changed["flag"].GetBoolean());
        now.Now += TimeSpan.FromMinutes(6);
        status = HttpStatusCode.ServiceUnavailable;
        Assert.Empty(await Create().GetPropertiesAsync(user, "source"));
        Assert.Contains("\"flag\":false", db.Persons.Single().ProviderProfilesJson!);
        Assert.Contains("\"ConfirmedAt\":null", db.Persons.Single().ProviderProfilesJson!);
        Assert.NotEqual(stored, db.Persons.Single().ProviderProfilesJson!);
    }

    [Fact]
    public async Task GetPropertiesAsync_ShouldOmitWhenSourceIsUnlinkedDuringFetch()
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var handler = new UpstreamStubHandler(async (_, _) =>
        {
            user.PersonId = null;
            await db.SaveChangesAsync();
            return Response(Success("{\"flag\":true}"));
        });
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)),
            Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = Source() } }));
        Assert.Empty(await service.GetPropertiesAsync(user, "source"));
        Assert.Null(db.Persons.Single().ProviderProfilesJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPropertiesAsync_ShouldOmitDisabledOrAmbiguousSources(bool ambiguous)
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        if (ambiguous)
        {
            var second = new ApplicationUser { Id = Guid.NewGuid(), UserName = "other", PersonId = user.PersonId };
            db.Users.Add(second);
            db.UserLogins.Add(new IdentityUserLogin<Guid> { UserId = second.Id, LoginProvider = "provider", ProviderKey = "other" });
            await db.SaveChangesAsync();
        }
        var handler = new UpstreamStubHandler((_, _) => throw new InvalidOperationException("Must not dispatch"));
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)),
            Options.Create(new ProviderProfileOptions { Enabled = ambiguous, Sources = new() { ["source"] = Source() } }));
        Assert.Empty(await service.GetPropertiesAsync(user, "source"));
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("confirmed", "confirmed")]
    [InlineData("expired", "expired")]
    [InlineData("unconfirmed", "unconfirmed")]
    [InlineData("policy", "configuration_changed")]
    [InlineData("link", "link_missing_or_ambiguous")]
    [InlineData("missing", "snapshot_missing")]
    [InlineData("disabled", "disabled")]
    [InlineData("inactive", "account_ineligible")]
    [InlineData("person", "person_ineligible")]
    [InlineData("zero-age", "expired")]
    public async Task ReadSnapshotAsync_ShouldValidateConfirmationWithoutNetworkTrackingOrWrites(string scenario, string expected)
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var clock = new TestClock();
        var source = Source();
        var config = Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = source } });
        var status = HttpStatusCode.OK;
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(Success("{\"flag\":false,\"code\":\"fixture-value\"}"), status)));
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)), config, clock);
        await service.GetPropertiesAsync(user, "source");
        switch (scenario)
        {
            case "expired": clock.Now += config.Value.MaximumAge; break;
            case "zero-age": config.Value.MaximumAge = TimeSpan.Zero; break;
            case "unconfirmed":
                status = HttpStatusCode.ServiceUnavailable;
                await service.RefreshAfterLoginAsync(user, "provider", "subject");
                break;
            case "policy": source.AllowedProperties["code"] = "StringArray"; break;
            case "link": db.UserLogins.RemoveRange(db.UserLogins); break;
            case "missing": db.Persons.Single().ProviderProfilesJson = null; break;
            case "disabled": config.Value.Enabled = false; break;
            case "inactive": db.Users.Single().IsActive = false; break;
            case "person": db.Persons.Single().IsDeleted = true; break;
        }
        await db.SaveChangesAsync();
        var before = db.Persons.Single().ProviderProfilesJson;
        var calls = handler.CallCount;
        db.ChangeTracker.Clear();
        // Reuse the service: its ordinary request cache must never bypass preview freshness.
        var result = await service.ReadSnapshotAsync(user, "source");
        Assert.Equal(expected, result.Status);
        Assert.Equal(calls, handler.CallCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before, (await db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
        if (expected == "confirmed")
        {
            Assert.False(result.Properties["flag"].GetBoolean());
            Assert.NotNull(result.ConfirmedAt);
            Assert.NotNull(result.ExpiresAt);
        }
        else Assert.Empty(result.Properties);
    }

    [Theory]
    [InlineData("missing", "snapshot_missing")]
    [InlineData("expired", "expired")]
    [InlineData("invalid", "snapshot_invalid")]
    public async Task ReadForPreviewAsync_ShouldFetchWithoutLoginAndLeaveStoredSnapshotUnchanged(string scenario, string storedStatus)
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var clock = new TestClock();
        var source = Source();
        source.AllowedProperties["divis"] = "String";
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(Success(
            "{\"flag\":true,\"code\":\"current\",\"divis\":\"4\",\"unapproved\":\"ignored\"}"))));
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)),
            Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = source } }), clock);
        if (scenario == "expired")
        {
            await service.GetPropertiesAsync(user, "source");
            clock.Now += TimeSpan.FromMinutes(10);
        }
        if (scenario == "invalid")
        {
            db.Persons.Single().ProviderProfilesJson = "not-json";
            await db.SaveChangesAsync();
        }
        var before = (await db.Persons.SingleAsync()).ProviderProfilesJson;
        var calls = handler.CallCount;
        db.ChangeTracker.Clear();

        var result = await service.ReadForPreviewAsync(user, "source");

        Assert.Equal("confirmed", result.Status);
        Assert.Equal("4", result.Properties["divis"].GetString());
        Assert.False(result.Properties.ContainsKey("unapproved"));
        Assert.Equal(clock.Now, result.ConfirmedAt);
        Assert.Null(result.ExpiresAt);
        Assert.Equal(calls + 1, handler.CallCount);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before, (await db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
        Assert.Equal(storedStatus, (await service.ReadSnapshotAsync(user, "source")).Status);
        Assert.Equal(calls + 1, handler.CallCount);
    }

    [Fact]
    public async Task ReadForPreviewAsync_ShouldNotChangeIssuanceCacheAndPreserveNormalLoginRefresh()
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var code = "login-value";
        var status = HttpStatusCode.OK;
        var handler = new UpstreamStubHandler((_, _) => Task.FromResult(Response(Success(
            JsonSerializer.Serialize(new { code, flag = true })), status)));
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)),
            Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = Source() } }));
        await service.RefreshAfterLoginAsync(user, "provider", "subject");
        var stored = (await db.Persons.SingleAsync()).ProviderProfilesJson;
        code = "preview-value";

        Assert.Equal("preview-value", (await service.ReadForPreviewAsync(user, "source")).Properties["code"].GetString());
        Assert.Equal(stored, (await db.Persons.SingleAsync()).ProviderProfilesJson);
        Assert.Equal("login-value", (await service.GetPropertiesAsync(user, "source"))["code"].GetString());
        status = HttpStatusCode.ServiceUnavailable;
        var failed = await service.ReadForPreviewAsync(user, "source");
        Assert.Equal("unavailable", failed.Status);
        Assert.Empty(failed.Properties);
        Assert.Equal(stored, (await db.Persons.SingleAsync()).ProviderProfilesJson);

        status = HttpStatusCode.OK;
        code = "next-login-value";
        await service.RefreshAfterLoginAsync(user, "provider", "subject");
        Assert.Equal("next-login-value", (await service.ReadSnapshotAsync(user, "source")).Properties["code"].GetString());
        Assert.Equal("next-login-value", (await service.GetPropertiesAsync(user, "source"))["code"].GetString());
    }

    [Fact]
    public async Task ReadForPreviewAsync_ShouldDenyBindingChangeDuringUpstreamReadWithoutSaving()
    {
        await using var db = Context();
        var user = await SeedAsync(db);
        var handler = new UpstreamStubHandler(async (_, _) =>
        {
            db.UserLogins.RemoveRange(db.UserLogins);
            await db.SaveChangesAsync();
            return Response(Success("{\"flag\":true}"));
        });
        var service = new ProviderProfileService(db, new ProviderProfileClient(new HttpClient(handler)),
            Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new() { ["source"] = Source() } }));
        var result = await service.ReadForPreviewAsync(user, "source");
        Assert.Equal("account_ineligible", result.Status);
        Assert.Empty(result.Properties);
        Assert.Null((await db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
    }

    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<ApplicationUser> SeedAsync(ApplicationDbContext db)
    {
        var person = new Person { Id = Guid.NewGuid() };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture", PersonId = person.Id };
        db.Persons.Add(person);
        db.Users.Add(user);
        db.UserLogins.Add(new IdentityUserLogin<Guid> { UserId = user.Id, LoginProvider = "provider", ProviderKey = "subject" });
        await db.SaveChangesAsync();
        return user;
    }
    private static ProviderProfileRequest Request() => new() { ProviderNamespace = "provider", StableSubject = "subject" };
    private static ProviderProfileSourceOptions Source() => new()
    {
        ProviderNamespace = "provider", Endpoint = "https://profile.example.test/profile",
        SharedSecret = "synthetic-profile-secret", AllowedProperties = new(Schema, StringComparer.Ordinal)
    };
    private static Dictionary<string, JsonElement> Properties(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    private static string Success(string properties) => "{\"contractVersion\":\"1.0\",\"providerNamespace\":\"provider\",\"stableSubject\":\"subject\",\"extraProperties\":" + properties + "}";
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
