using System.Net;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProviderProfileTests
{
    private static readonly Dictionary<string, string> Schema = new() { ["flag"] = "Boolean", ["code"] = "String" };

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
