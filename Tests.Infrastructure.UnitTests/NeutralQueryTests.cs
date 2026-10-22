using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Directory;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class NeutralQueryTests
{
    [Theory]
    [InlineData("accountName", "user.userName")]
    [InlineData("employeeId", "person.employeeId")]
    [InlineData("employeeId", "user.employeeId")]
    public async Task DirectorySync_ShouldUseOnlyApprovedExactInputAndFillMappedFieldsWithoutLogin(string attribute, string valueSource)
    {
        using var f = new Fixture(nativeMode: true, direct: true);
        f.DirectoryNative.SamAccountNameQueryAttribute = attribute;
        f.DirectoryNative.AllowedValueSources = [valueSource];
        var id = await f.SeedAsync();
        var user = await f.Db.Users.SingleAsync();
        var person = await f.Db.Persons.SingleAsync();
        var personId = person.Id;
        user.UserName = "local-account";
        user.Department = "keep department";
        user.EmailMfaEnabled = true;
        f.NativeFields.ApplyManual(user, new Dictionary<NativeField, string?> { [NativeField.LastName] = "" });
        f.NativeFields.ApplyManual(person, new Dictionary<NativeField, string?> { [NativeField.FirstName] = "" });
        await f.Db.SaveChangesAsync();
        await f.ConfigureAsync(attribute, valueSource);
        var transport = new Mock<IProtectedDirectoryIdentityTransport>(MockBehavior.Strict);
        var expected = valueSource == "user.userName" ? "local-account" : valueSource == "person.employeeId" ? "0001" : "different";
        transport.Setup(x => x.FindExactAsync(expected, DirectoryTransport.Ldaps, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProtectedDirectoryTransportResult(ProtectedDirectoryTransportOutcome.Succeeded,
                [new(Guid.NewGuid(), expected, true, true, false, new AssuredProfile
                {
                    DisplayName = "Directory display", Surname = "Directory surname", Department = "Directory department",
                    Email = "directory@example.test",
                    AssuredFields = [AssuredProfileField.DisplayName, AssuredProfileField.Surname, AssuredProfileField.Department, AssuredProfileField.Email]
                })]));
        var http = new Mock<IProviderNativeProfileClient>(MockBehavior.Strict);
        var sync = f.CreateDirectorySync(transport.Object, http.Object);
        var deployed = Assert.Single((await f.Service.GetConfigurationAsync()).Sources);
        Assert.True(deployed.Available);
        Assert.Equal("directory", deployed.SourceType);
        Assert.Equal(attribute, Assert.Single(deployed.AvailableCandidates).Attribute);
        Assert.False(f.NativeOptions.Enabled);
        Assert.False(f.DirectoryIntegration.AuthenticationEnabled);
        Assert.True((await sync.GetStatusAsync()).Enabled);
        Assert.Equal("query_unavailable", (await sync.PreviewAsync("actor", id)).Reason);
        transport.VerifyNoOtherCalls();

        var mappings = await f.NativeFields.GetMappingsAsync();
        await f.NativeFields.SetMappingsAsync(new(mappings.Revision,
            mappings.Mappings.Select(x => x.Field == NativeField.FirstName ? x with { SourceProperty = "displayName" } : x).ToArray(), mappings.FirstNameFallback));
        Assert.False((await f.NativeFields.GetMappingsAsync()).FirstNameFallback.Enabled);
        await f.ApproveAsync(id);
        var context = (await f.Service.ResolveAsync("native-profile", id)).Context!;
        Assert.Null(context.Transport);
        Assert.Equal(attribute, context.DirectoryTransport!.SamAccountNameQueryAttribute);
        var preview = await sync.PreviewAsync("actor", id);
        Assert.Equal("ready", preview.Status);
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).FirstName);
        Assert.Equal("Directory display", preview.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").ProposedValue);
        Assert.Equal("manual_fixed", preview.Changes.Single(x => x.Target == "User" && x.Field == "LastName").Action);
        Assert.Equal("manual_fixed", preview.Changes.Single(x => x.Target == "Person" && x.Field == "FirstName").Action);
        Assert.Equal("preserve", preview.Changes.Single(x => x.Target == "User" && x.Field == "Department").Action);
        Assert.Equal("missing_source", preview.Changes.Single(x => x.Target == "User" && x.Field == "JobTitle").Action);
        Assert.Equal("mfa_protected", preview.Changes.Single(x => x.Target == "User" && x.Field == "Email").Action);
        Assert.Equal("preview_invalid", (await Assert.ThrowsAsync<ProfileSyncException>(() => sync.ApplyAsync("other-actor", id, preview.PreviewToken!))).Code);
        await sync.ApplyAsync("actor", id, preview.PreviewToken!);
        user = await f.Db.Users.AsNoTracking().SingleAsync();
        person = await f.Db.Persons.AsNoTracking().SingleAsync();
        Assert.Equal(id, user.Id);
        Assert.Equal(personId, user.PersonId);
        Assert.Equal(personId, person.Id);
        Assert.Equal("local-account", user.UserName);
        Assert.Equal("Directory display", user.FirstName);
        Assert.Equal("", user.LastName);
        Assert.Equal("", person.FirstName);
        Assert.Equal("Directory surname", person.LastName);
        Assert.Equal("keep department", user.Department);
        Assert.Equal("Directory department", person.Department);
        Assert.Null(user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("directory@example.test", person.Email);
        Assert.Equal("different", user.EmployeeId);
        Assert.Equal(" 0001 ", person.EmployeeId);
        Assert.Empty(await f.Db.UserLogins.ToArrayAsync());
        Assert.Empty(await f.Db.ProviderSubjectDirectoryBindings.ToArrayAsync());
        http.VerifyNoOtherCalls();
        transport.Verify(x => x.FindExactAsync(expected, DirectoryTransport.Ldaps, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("untrusted")]
    [InlineData("paused")]
    [InlineData("directory_disabled")]
    [InlineData("missing_scope")]
    [InlineData("unsupported_attribute")]
    [InlineData("undeployed_value_source")]
    [InlineData("duplicate_source_id")]
    public async Task DirectorySource_ShouldFailClosedForInvalidDeployment(string condition)
    {
        using var f = new Fixture(nativeMode: true, direct: true);
        switch (condition)
        {
            case "disabled": f.DirectoryNative.Enabled = false; break;
            case "untrusted": f.DirectoryNative.Trusted = false; break;
            case "paused": f.DirectoryNative.Paused = true; break;
            case "directory_disabled": f.DirectoryIntegration.Enabled = false; break;
            case "missing_scope": f.DirectoryConnection.ManagedSearchFilter = null; break;
            case "unsupported_attribute": f.DirectoryNative.SamAccountNameQueryAttribute = "nationalIdHash"; break;
            case "undeployed_value_source": f.DirectoryNative.AllowedValueSources = ["user.userName"]; break;
            case "duplicate_source_id": f.NativeOptions.QuerySources["example"] = f.Source; break;
        }
        Assert.False(Assert.Single((await f.Service.GetConfigurationAsync()).Sources).Available);
        Assert.Equal("source_unavailable", (await Assert.ThrowsAsync<NeutralQueryException>(() => f.ConfigureAsync("employeeId", "person.employeeId"))).Code);
    }

    [Fact]
    public async Task DirectorySource_ShouldRejectLegacyModeAndBlockSavedLegacySelection()
    {
        using var f = new Fixture(nativeMode: true, direct: true);
        var id = await f.SeedAsync();
        var legacy = new QueryConfigurationDto("native-profile", "example", "legacy", "example.provider", []);
        var revision = (await f.Service.GetConfigurationAsync()).Revision;
        Assert.Equal("invalid_mapping", (await Assert.ThrowsAsync<NeutralQueryException>(() =>
            f.Service.SetConfigurationAsync(new(revision, [legacy])))).Code);
        f.Db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = SettingKeys.NeutralQueryConfiguration,
            Value = JsonSerializer.Serialize(new { Revision = Guid.NewGuid(), Configurations = new[] { legacy } }) });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal("Unavailable", (await f.Service.ResolveAsync("native-profile", id)).Status);
        var transport = new Mock<IProtectedDirectoryIdentityTransport>(MockBehavior.Strict);
        var sync = f.CreateDirectorySync(transport.Object);
        Assert.False((await sync.GetStatusAsync()).Enabled);
        Assert.Equal("configuration_invalid", (await sync.PreviewAsync("actor", id)).Reason);
        transport.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("scope")]
    [InlineData("connection")]
    [InlineData("approval")]
    [InlineData("mapping")]
    [InlineData("object")]
    public async Task DirectorySync_ShouldRejectStalePreviewWithoutWriting(string change)
    {
        using var f = new Fixture(nativeMode: true, direct: true);
        var id = await f.SeedAsync();
        await f.ConfigureAsync("employeeId", "person.employeeId");
        await f.ApproveAsync(id);
        var objectId = Guid.NewGuid();
        var transport = new Mock<IProtectedDirectoryIdentityTransport>();
        transport.Setup(x => x.FindExactAsync("0001", DirectoryTransport.Ldaps, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new(ProtectedDirectoryTransportOutcome.Succeeded,
                [new(objectId, "0001", true, true, false, new AssuredProfile { DisplayName = "Directory display", AssuredFields = [AssuredProfileField.DisplayName] })]));
        var sync = f.CreateDirectorySync(transport.Object);
        var preview = await sync.PreviewAsync("actor", id);
        Assert.NotNull(preview.PreviewToken);
        switch (change)
        {
            case "policy": f.DirectoryNative.PolicyRevision = "changed"; break;
            case "scope": f.DirectoryConnection.ManagedSearchFilter = "(&(objectCategory=person)(department=example))"; break;
            case "connection": f.DirectoryConnection.Host = "changed.example.invalid"; break;
            case "approval":
                var approval = await f.Service.GetApprovalAsync("native-profile", "example", id);
                await f.Service.SetApprovalAsync("native-profile", "example", id, new(approval.Revision, false));
                break;
            case "mapping":
                var mappings = await f.NativeFields.GetMappingsAsync();
                await f.NativeFields.SetMappingsAsync(new(mappings.Revision, mappings.Mappings, mappings.FirstNameFallback));
                break;
            case "object": objectId = Guid.NewGuid(); break;
        }
        f.Db.ChangeTracker.Clear();
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() => sync.ApplyAsync("actor", id, preview.PreviewToken!))).Code);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
        Assert.Null((await f.Db.Persons.AsNoTracking().SingleAsync()).Nickname);
    }

    [Theory]
    [InlineData(DirectoryLookupOutcome.NotFound, "source_not_found")]
    [InlineData(DirectoryLookupOutcome.Ambiguous, "source_unavailable")]
    [InlineData(DirectoryLookupOutcome.Ineligible, "source_unavailable")]
    [InlineData(DirectoryLookupOutcome.Timeout, "source_unavailable")]
    [InlineData(DirectoryLookupOutcome.Unavailable, "source_unavailable")]
    public async Task DirectorySync_ShouldSurfaceFailedReadWithoutHttpFallback(DirectoryLookupOutcome outcome, string code)
    {
        using var f = new Fixture(nativeMode: true, direct: true);
        var id = await f.SeedAsync();
        await f.ConfigureAsync("employeeId", "person.employeeId");
        await f.ApproveAsync(id);
        var lookup = new Mock<IDirectoryIdentityLookup>();
        lookup.Setup(x => x.FindManagedIdentityAsync("0001", It.IsAny<CancellationToken>())).ReturnsAsync(new DirectoryLookupResult(outcome));
        var http = new Mock<IProviderNativeProfileClient>(MockBehavior.Strict);
        var sync = new AdminProfileSyncService(f.Db, f.NativeFields, http.Object, Options.Create(f.NativeOptions),
            f.Protection, new UpperInvariantLookupNormalizer(), Mock.Of<Core.Application.IAuditService>(), queries: f.Service, directoryLookup: lookup.Object);
        Assert.Equal(code, (await Assert.ThrowsAsync<ProfileSyncException>(() => sync.PreviewAsync("actor", id))).Code);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
        lookup.Verify(x => x.FindManagedIdentityAsync("0001", It.IsAny<CancellationToken>()), Times.Once);
        http.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("example.provider")]
    [InlineData("other.example")]
    public async Task NativeSync_ShouldUseSelectedSourceAndPropertyAndRejectEarlierReviews(string secondNamespace)
    {
        using var f = new Fixture(nativeMode: true);
        var second = new NeutralQuerySourceOptions
        {
            Enabled = true, Trusted = true, ProviderNamespace = secondNamespace, PolicyRevision = "second",
            Endpoint = "https://second.example.invalid/native", SharedSecret = "second-synthetic-secret",
            QueryRule = "rule-b", AcceptedAttributeSets = [["employeeId"]],
            AllowedValueSources = new() { ["employeeId"] = ["person.employeeId"] }
        };
        f.NativeOptions.QuerySources["arbitrary-z9"] = second;
        f.Source.QueryRule = "rule-a";
        var userId = await f.SeedAsync();
        var user = await f.Db.Users.SingleAsync();
        var personId = user.PersonId!.Value;
        user.UserName = "retained-account";
        user.Department = "retained department";
        f.NativeFields.ApplyManual(user, new Dictionary<NativeField, string?> { [NativeField.FirstName] = "" });
        f.NativeFields.ApplyManual(await f.Db.Persons.SingleAsync(),
            new Dictionary<NativeField, string?> { [NativeField.LastName] = "fixed surname" });
        f.Db.UserLogins.Add(new() { UserId = userId, LoginProvider = "login.example", ProviderKey = "opaque-login-key" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var calls = new List<(string Endpoint, string Namespace, string Rule)>();
        var omitTitle = false;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage message, CancellationToken ct) =>
            {
                Assert.Equal("native-profile/2.0", Assert.Single(message.Headers.GetValues("X-Provider-Contract")));
                Assert.True(NeutralQueryJson.TryReadRequest("native-profile/2.0", await message.Content!.ReadAsByteArrayAsync(ct), out var request));
                var isSecond = message.RequestUri!.AbsoluteUri == second.Endpoint;
                var selected = isSecond ? second : f.Source;
                Assert.Equal(selected.Endpoint, message.RequestUri.AbsoluteUri);
                Assert.Equal(selected.SharedSecret, Assert.Single(message.Headers.GetValues("X-Internal-Secret")));
                Assert.Equal(selected.ProviderNamespace, request!.ProviderNamespace);
                Assert.Equal(selected.QueryRule, request.Query.QueryRule);
                Assert.Equal("0001", Assert.Single(request.Query.Attributes).Value);
                calls.Add((message.RequestUri.AbsoluteUri, request.ProviderNamespace, request.Query.QueryRule!));
                var prefix = isSecond ? "B" : "A";
                var result = JsonSerializer.Serialize(new
                {
                    profile = new
                    {
                        displayName = prefix + " display", givenName = prefix + " first", surname = prefix + " surname",
                        department = prefix + " department", title = omitTitle ? null : prefix + " title",
                        assuredFields = new[] { "DisplayName", "GivenName", "Surname", "Department", "Title" }
                    },
                    emailTrustOrigin = "Unknown"
                });
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(Encoding.UTF8.GetString(Response(request, "Found", result)), Encoding.UTF8, "application/json")
                };
            });
        using var http = new HttpClient(handler.Object);
        var sync = new AdminProfileSyncService(f.Db, f.NativeFields,
            new ProviderNativeProfileClient(http, Options.Create(f.NativeOptions)), Options.Create(f.NativeOptions),
            f.Protection, new UpperInvariantLookupNormalizer(), Mock.Of<Core.Application.IAuditService>(), queries: f.Service);

        async Task SelectAsync(string id, NeutralQuerySourceOptions source)
        {
            var state = await f.Service.GetConfigurationAsync();
            await f.Service.SetConfigurationAsync(new(state.Revision,
                [new("native-profile", id, "neutral-v2", source.ProviderNamespace, [new("employeeId", "person.employeeId")], source.QueryRule)]));
            var approval = await f.Service.GetApprovalAsync("native-profile", id, userId);
            await f.Service.SetApprovalAsync("native-profile", id, userId, new(approval.Revision, true));
            f.Db.ChangeTracker.Clear();
        }

        await SelectAsync("example", f.Source);
        var first = await sync.PreviewAsync("actor", userId);
        Assert.Equal("A display", first.Changes.Single(x => x.Target == "User" && x.Field == "Nickname").ProposedValue);
        var firstCalls = calls.Count;
        await SelectAsync("arbitrary-z9", second);
        Assert.Equal(firstCalls, calls.Count);
        Assert.Equal("arbitrary-z9", Assert.Single((await f.Service.GetConfigurationAsync()).Configurations).SourceId);
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() => sync.ApplyAsync("actor", userId, first.PreviewToken!))).Code);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
        var beforeMapping = await sync.PreviewAsync("actor", userId);
        Assert.Equal("B display", beforeMapping.Changes.Single(x => x.Target == "User" && x.Field == "Nickname").ProposedValue);
        var mappings = await f.NativeFields.GetMappingsAsync();
        var rows = mappings.Mappings.Select(x => x.Source == NativeFieldSource.NeutralQuery &&
            x.ProviderNamespace == secondNamespace && x.Field == NativeField.Nickname ? x with { SourceProperty = "title" } : x).ToArray();
        var beforeSaveCalls = calls.Count;
        await f.NativeFields.SetMappingsAsync(new(mappings.Revision, rows, mappings.FirstNameFallback));
        Assert.Equal(beforeSaveCalls, calls.Count);
        Assert.Equal("title", (await f.NativeFields.GetMappingsAsync()).Mappings.Single(x => x.Source == NativeFieldSource.NeutralQuery &&
            x.ProviderNamespace == secondNamespace && x.Field == NativeField.Nickname).SourceProperty);
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() => sync.ApplyAsync("actor", userId, beforeMapping.PreviewToken!))).Code);

        omitTitle = true;
        var missing = await sync.PreviewAsync("actor", userId);
        Assert.Equal("missing_source", missing.Changes.Single(x => x.Target == "User" && x.Field == "Nickname").Action);
        await sync.ApplyAsync("actor", userId, missing.PreviewToken!);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
        Assert.Null((await f.Db.Persons.AsNoTracking().SingleAsync()).Nickname);
        // Apply changes concurrency/native state, so explicitly approve the current account again.
        var currentApproval = await f.Service.GetApprovalAsync("native-profile", "arbitrary-z9", userId);
        Assert.False(currentApproval.Approved);
        await f.Service.SetApprovalAsync("native-profile", "arbitrary-z9", userId, new(currentApproval.Revision, true));
        omitTitle = false;
        var fresh = await sync.PreviewAsync("actor", userId);
        Assert.Equal("B title", fresh.Changes.Single(x => x.Target == "User" && x.Field == "Nickname").ProposedValue);
        Assert.Equal("manual_fixed", fresh.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").Action);
        Assert.Equal("preserve", fresh.Changes.Single(x => x.Target == "User" && x.Field == "Department").Action);
        await sync.ApplyAsync("actor", userId, fresh.PreviewToken!);
        user = await f.Db.Users.AsNoTracking().SingleAsync();
        var person = await f.Db.Persons.AsNoTracking().SingleAsync();
        Assert.Equal("B title", user.Nickname);
        Assert.Equal("B surname", user.LastName);
        Assert.Equal("", user.FirstName);
        Assert.Equal("retained department", user.Department);
        Assert.Equal("B title", person.Nickname);
        Assert.Equal("B first", person.FirstName);
        Assert.Equal("fixed surname", person.LastName);
        Assert.Equal("B department", person.Department);
        Assert.Equal(userId, user.Id);
        Assert.Equal(personId, user.PersonId);
        Assert.Equal(personId, person.Id);
        Assert.Equal("retained-account", user.UserName);
        Assert.Equal("security", user.SecurityStamp);
        Assert.Equal("different", user.EmployeeId);
        Assert.Equal(" 0001 ", person.EmployeeId);
        var login = await f.Db.UserLogins.SingleAsync();
        Assert.Equal(userId, login.UserId);
        Assert.Equal("login.example", login.LoginProvider);
        Assert.Equal("opaque-login-key", login.ProviderKey);
        Assert.All(calls.Skip(firstCalls), call => Assert.Equal((second.Endpoint, secondNamespace, "rule-b"), call));
    }

    [Fact]
    public async Task ProfilePreview_ShouldConsumeSavedApprovedQueryAndBlockChangedStoredValueWithoutLegacyFallback()
    {
        using var f = new Fixture();
        var userId = await f.SeedAsync();
        await f.ConfigureAsync("employeeId", "person.employeeId");
        var approval = await f.Service.GetApprovalAsync("profile", "example", userId);
        await f.Service.SetApprovalAsync("profile", "example", userId, new(approval.Revision, true));
        f.Db.ChangeTracker.Clear();
        var user = await f.Db.Users.SingleAsync();
        var personId = user.PersonId;
        var calls = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage message, CancellationToken ct) =>
            {
                calls++;
                Assert.Equal(f.Source.Endpoint, message.RequestUri!.AbsoluteUri);
                Assert.Equal("profile/2.0", Assert.Single(message.Headers.GetValues("X-Provider-Contract")));
                Assert.Equal(f.Source.SharedSecret, Assert.Single(message.Headers.GetValues("X-Internal-Secret")));
                var body = await message.Content!.ReadAsByteArrayAsync(ct);
                Assert.True(NeutralQueryJson.TryReadRequest("profile/2.0", body, out var request));
                Assert.Equal("0001", request!.Query.Attributes["employeeId"]);
                Assert.Null(request.Query.QueryRule);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(Encoding.UTF8.GetString(Response(request, "Found",
                        "{\"extraProperties\":{\"flag\":true,\"unapproved\":\"omitted\"}}")), Encoding.UTF8, "application/json")
                };
            });
        using var http = new HttpClient(handler.Object);
        var profile = new ProviderProfileService(f.Db, new ProviderProfileClient(http), Options.Create(new ProviderProfileOptions
        {
            Enabled = true,
            Sources = new() { ["example"] = new() { ProviderNamespace = "example.provider", AllowedProperties = new() { ["flag"] = "Boolean" } } }
        }), queries: f.Service);

        var preview = await profile.ReadForPreviewAsync(user, "example");
        Assert.True(Assert.Single(preview.Properties).Value.GetBoolean());
        Assert.Null((await f.Db.Persons.SingleAsync()).ProviderProfilesJson);
        f.Db.Persons.Local.Single().EmployeeId = "changed";
        await f.Db.SaveChangesAsync();

        Assert.Empty((await profile.ReadForPreviewAsync(user, "example")).Properties);
        Assert.Equal(1, calls);
        Assert.False((await f.Service.GetApprovalAsync("profile", "example", userId)).Approved);
        Assert.Equal(personId, (await f.Db.Users.SingleAsync()).PersonId);
        Assert.Equal(userId, (await f.Db.Users.SingleAsync()).Id);
        Assert.Empty(f.Db.UserLogins);
    }

    [Theory]
    [InlineData("native-profile", "{\"profile\":{\"assuredFields\":[]},\"emailTrustOrigin\":\"Unknown\"}")]
    [InlineData("profile", "{\"extraProperties\":{\"example\":true}}")]
    [InlineData("metadata", "{\"email\":null,\"emailTrustOrigin\":\"Unknown\",\"verifiedAt\":null}")]
    [InlineData("lifecycle-status", "{\"accountState\":\"Enabled\",\"evidence\":{\"sourceAuthority\":\"example.authority\",\"mappingVersion\":\"1\",\"snapshotVersion\":\"1\",\"observedAt\":\"2026-10-21T00:00:00.000Z\",\"effectiveFrom\":\"2026-10-21T00:00:00.000Z\",\"effectiveUntil\":\"2026-10-21T00:01:00.000Z\"}}")]
    [InlineData("recovery-identity-verification", "{\"outcome\":\"Denied\",\"scheme\":\"identity-identifier\"}")]
    [InlineData("password-sync", "{\"outcome\":\"NoOp\"}")]
    public void Wire_ShouldCorrelateEachCapabilityAndRejectWrongStatusOrIdentity(string capability, string result)
    {
        var request = Request(capability);
        Assert.True(NeutralQueryJson.TrySerializeRequest(request, out var requestBody));
        Assert.True(NeutralQueryJson.TryReadRequest(capability + "/2.0", requestBody, out var parsed));
        Assert.Equal("0001", parsed!.Query.Attributes["employeeId"]);
        var body = Response(request, "Found", result);
        Assert.True(NeutralQueryJson.TryReadResponse(200, "application/json; charset=utf-8", body, request, out _));
        Assert.False(NeutralQueryJson.TryReadResponse(503, "application/json", body, request, out _));
        Assert.False(NeutralQueryJson.TryReadResponse(200, "application/json", body, request with { RequestId = Guid.NewGuid().ToString() }, out _));
        Assert.False(NeutralQueryJson.TryReadResponse(200, "application/json", body, request with { ProviderNamespace = "other.provider" }, out _));
        var ambiguous = Response(request, "Ambiguous");
        Assert.True(NeutralQueryJson.TryReadResponse(capability == "recovery-identity-verification" ? 503 : 200, "application/json", ambiguous, request, out _));
        Assert.False(NeutralQueryJson.TryReadResponse(200, "application/json", Response(request, "NotFound", "null"), request, out _));
    }

    [Theory]
    [InlineData("\"employeeId\":\"one\",\"employeeId\":\"two\"")]
    [InlineData("\"employeeId\":12")]
    [InlineData("\"employeeId\":\" one \"")]
    [InlineData("\"employeeId\":\"a\\u0000b\"")]
    [InlineData("\"employeeId\":\"\\ud800\"")]
    [InlineData("\"email\":\"example@example.invalid\"")]
    [InlineData("\"employeeId\":null")]
    [InlineData("\"employeeId\":[]")]
    public void Wire_ShouldRejectUntrustedShapes(string attributes)
    {
        var text = "{\"contractType\":\"profile\",\"contractVersion\":\"2.0\",\"requestId\":\"11111111-1111-4111-8111-111111111111\",\"providerNamespace\":\"example.provider\",\"query\":{\"attributes\":{" + attributes + "}}}";
        Assert.False(NeutralQueryJson.TryReadRequest("profile/2.0", Encoding.UTF8.GetBytes(text), out _));
    }

    [Fact]
    public void Wire_ShouldRejectMixedLegacyUnknownHeaderAndDuplicateResultMembers()
    {
        var request = Request("profile");
        Assert.True(NeutralQueryJson.TrySerializeRequest(request, out var body));
        Assert.False(NeutralQueryJson.TryReadRequest("profile/1.0", body, out _));
        Assert.False(NeutralQueryJson.TryReadRequest(null, body, out _));
        var mixed = Encoding.UTF8.GetString(body).Replace("\"query\":", "\"stableSubject\":\"old\",\"query\":");
        Assert.False(NeutralQueryJson.TryReadRequest("profile/2.0", Encoding.UTF8.GetBytes(mixed), out _));
        Assert.False(NeutralQueryJson.TryReadResponse(200, "application/json", Response(request, "Found",
            "{\"extraProperties\":{\"x\":true,\"x\":false}}"), request, out _));
        Assert.False(NeutralQueryJson.TryReadRequest("profile/2.0", new byte[] { 0xef, 0xbb, 0xbf }.Concat(body).ToArray(), out _));
    }

    [Fact]
    public async Task Configuration_ShouldRequireCurrentAccountApprovalAndInvalidateValueMappingAndPolicyChanges()
    {
        using var f = new Fixture();
        var userId = await f.SeedAsync();
        Assert.Equal("Legacy", (await f.Service.ResolveAsync("profile", userId)).Status);
        var saved = await f.ConfigureAsync("employeeId", "person.employeeId");
        Assert.Equal("MissingInput", (await f.Service.ResolveAsync("profile", userId)).Status);
        var approval = await f.Service.GetApprovalAsync("profile", "example", userId);
        await f.Service.SetApprovalAsync("profile", "example", userId, new(approval.Revision, true));
        var resolved = await f.Service.ResolveAsync("profile", userId);
        Assert.Equal("Ready", resolved.Status);
        Assert.Equal("0001", resolved.Context!.Request.Query.Attributes["employeeId"]);
        Assert.True(await f.Service.IsCurrentAsync(resolved.Context));
        var newSaved = await f.Service.SetConfigurationAsync(new(saved.Revision, saved.Configurations));
        Assert.NotEqual(saved.Revision, newSaved.Revision);
        Assert.False(await f.Service.IsCurrentAsync(resolved.Context));
        resolved = await f.Service.ResolveAsync("profile", userId);
        f.Source.PolicyRevision = "second";
        Assert.False(await f.Service.IsCurrentAsync(resolved.Context!));
        f.Db.Persons.Single().EmployeeId = "changed";
        await f.Db.SaveChangesAsync();
        Assert.Equal("MissingInput", (await f.Service.ResolveAsync("profile", userId)).Status);
        Assert.False((await f.Service.GetApprovalAsync("profile", "example", userId)).Approved);
        f.Db.Persons.Single().EmployeeId = " 0001 ";
        f.Db.Persons.Single().ModifiedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Service.GetApprovalAsync("profile", "example", userId)).Approved);
        Assert.Equal("MissingInput", (await f.Service.ResolveAsync("profile", userId)).Status);
        Assert.Empty(f.Db.UserLogins);
        Assert.Equal(userId, f.Db.Users.Single().Id);
    }

    [Fact]
    public async Task Configuration_ShouldBlockUnavailableSourcesUnsupportedSetsAndStaleWrites()
    {
        using var f = new Fixture();
        var saved = await f.ConfigureAsync("employeeId", "person.employeeId");
        var conflict = await Assert.ThrowsAsync<NeutralQueryException>(() => f.Service.SetConfigurationAsync(new(Guid.Empty, saved.Configurations)));
        Assert.Equal("conflict", conflict.Code);
        var invalid = saved.Configurations.Select(x => x with { Mappings = [new("accountName", "user.userName")] }).ToArray();
        Assert.Equal("invalid_mapping", (await Assert.ThrowsAsync<NeutralQueryException>(() =>
            f.Service.SetConfigurationAsync(new(saved.Revision, invalid)))).Code);
        f.Source.Trusted = false;
        var blocked = await f.Service.GetConfigurationAsync();
        Assert.False(Assert.Single(blocked.Sources).Available);
        Assert.Equal("source_untrusted", blocked.Sources[0].UnavailableReason);
        Assert.Empty(blocked.Sources[0].AvailableCandidates);
        Assert.Equal(JsonSerializer.Serialize(saved.Configurations), JsonSerializer.Serialize(blocked.Configurations));
    }

    [Theory]
    [InlineData("capability_disabled", "deployment_disabled")]
    [InlineData("source_disabled", "deployment_disabled")]
    [InlineData("untrusted", "source_untrusted")]
    [InlineData("paused", "source_paused")]
    [InlineData("namespace", "invalid_configuration")]
    [InlineData("policy", "invalid_configuration")]
    [InlineData("rule", "invalid_configuration")]
    [InlineData("timeout_zero", "invalid_configuration")]
    [InlineData("timeout_exceeded", "invalid_configuration")]
    [InlineData("endpoint", "invalid_configuration")]
    [InlineData("endpoint_userinfo", "invalid_configuration")]
    [InlineData("endpoint_query", "invalid_configuration")]
    [InlineData("endpoint_fragment", "invalid_configuration")]
    [InlineData("endpoint_http", "invalid_configuration")]
    [InlineData("secret_empty", "invalid_configuration")]
    [InlineData("secret_invalid", "invalid_configuration")]
    [InlineData("sets_empty", "invalid_configuration")]
    [InlineData("sets_invalid", "invalid_configuration")]
    [InlineData("values_invalid", "invalid_configuration")]
    [InlineData("runtime_only", "unsupported_inputs")]
    [InlineData("incomplete_set", "unsupported_inputs")]
    public async Task Configuration_ShouldReturnSafeReasonAndNoCandidates_WhenSourceIsUnavailable(string condition, string reason)
    {
        using var f = new Fixture(capabilityEnabled: condition != "capability_disabled");
        switch (condition)
        {
            case "source_disabled": f.Source.Enabled = false; break;
            case "untrusted": f.Source.Trusted = false; break;
            case "paused": f.Source.Paused = true; break;
            case "namespace": f.Source.ProviderNamespace = ""; break;
            case "policy": f.Source.PolicyRevision = ""; break;
            case "rule": f.Source.QueryRule = "invalid rule"; break;
            case "timeout_zero": f.Source.Timeout = TimeSpan.Zero; break;
            case "timeout_exceeded": f.Source.Timeout = TimeSpan.FromSeconds(31); break;
            case "endpoint": f.Source.Endpoint = "invalid endpoint"; break;
            case "endpoint_userinfo": f.Source.Endpoint = "https://private:example@example.test/query"; break;
            case "endpoint_query": f.Source.Endpoint = "https://example.test/query?private=value"; break;
            case "endpoint_fragment": f.Source.Endpoint = "https://example.test/query#private"; break;
            case "endpoint_http": f.Source.Endpoint = "http://example.test/query"; break;
            case "secret_empty": f.Source.SharedSecret = ""; break;
            case "secret_invalid": f.Source.SharedSecret = "private secret"; break;
            case "sets_empty": f.Source.AcceptedAttributeSets = []; break;
            case "sets_invalid": f.Source.AcceptedAttributeSets = [["unknown"]]; break;
            case "values_invalid": f.Source.AllowedValueSources["employeeId"] = ["unknown"]; break;
            case "runtime_only": f.Source.AllowedValueSources["employeeId"] = ["runtime.proof.employeeId"]; break;
            case "incomplete_set": f.Source.AcceptedAttributeSets = [["employeeId", "accountName"]]; break;
        }
        var source = Assert.Single((await f.Service.GetConfigurationAsync()).Sources);
        Assert.False(source.Available);
        Assert.Equal(reason, source.UnavailableReason);
        Assert.Empty(source.AvailableCandidates);
        Assert.Empty(source.AcceptedAttributeSets);
        Assert.Equal(reason == "unsupported_inputs" ? "invalid_mapping" : "source_unavailable", (await Assert.ThrowsAsync<NeutralQueryException>(() =>
            f.ConfigureAsync("employeeId", "person.employeeId"))).Code);
    }

    [Fact]
    public async Task Configuration_ShouldKeepAvailableCandidatesAndAllowedPrivateHttp()
    {
        using var f = new Fixture();
        f.Source.Endpoint = "http://example.test/query";
        f.Source.AllowPrivateNetworkHttp = true;
        var source = Assert.Single((await f.Service.GetConfigurationAsync()).Sources);
        Assert.True(source.Available);
        Assert.Null(source.UnavailableReason);
        Assert.Equal("person.employeeId", Assert.Single(Assert.Single(source.AvailableCandidates).ValueSources));
        Assert.Equal("employeeId", Assert.Single(Assert.Single(source.AcceptedAttributeSets)));
        await f.ConfigureAsync("employeeId", "person.employeeId");
    }

    [Fact]
    public async Task DocumentHash_ShouldRequireAlgorithmApprovalAndNeverRehashStoredDigest()
    {
        using var f = new Fixture();
        var id = await f.SeedAsync();
        var hash = PidHasher.Hash("synthetic-only");
        f.Db.Persons.Single().NationalId = hash;
        await f.Db.SaveChangesAsync();
        f.Source.AcceptedAttributeSets = [["nationalIdHash"]];
        f.Source.AllowedValueSources = new() { ["nationalIdHash"] = ["person.nationalId"] };
        await f.ConfigureAsync("nationalIdHash", "person.nationalId");
        var approval = await f.Service.GetApprovalAsync("profile", "example", id);
        Assert.Equal("algorithm_approval_required", (await Assert.ThrowsAsync<NeutralQueryException>(() =>
            f.Service.SetApprovalAsync("profile", "example", id, new(approval.Revision, true)))).Code);
        await f.Service.SetApprovalAsync("profile", "example", id, new(approval.Revision, true, true));
        Assert.Equal(hash, (await f.Service.ResolveAsync("profile", id)).Context!.Request.Query.Attributes["nationalIdHash"]);
        var settings = new SettingsService(f.Db, new MemoryCache(new MemoryCacheOptions()), f.Protection);
        Assert.Empty(await settings.GetByPrefixAsync(SettingKeys.NeutralQueryApprovalPrefix));
        await Assert.ThrowsAsync<Core.Application.SystemManagedSettingException>(() => settings.SetValueAsync(SettingKeys.NeutralQueryConfiguration, "{}"));
    }

    [Fact]
    public async Task Configuration_ShouldHideAndRejectRuntimeSourcesButRetainBlockedSavedSelections()
    {
        using var f = new Fixture();
        var id = await f.SeedAsync();
        f.Source.AllowedValueSources["employeeId"] = ["person.employeeId", "runtime.proof.employeeId", "runtime.directory.employeeId"];
        var state = await f.Service.GetConfigurationAsync();
        Assert.Equal(new[] { "person.employeeId" }, Assert.Single(Assert.Single(state.Sources).AvailableCandidates).ValueSources);
        Assert.Equal("invalid_mapping", (await Assert.ThrowsAsync<NeutralQueryException>(() =>
            f.ConfigureAsync("employeeId", "runtime.proof.employeeId"))).Code);
        var saved = new QueryConfigurationDto("profile", "example", "neutral-v2", "example.provider",
            [new("employeeId", "runtime.proof.employeeId")]);
        f.Db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = SettingKeys.NeutralQueryConfiguration,
            Value = JsonSerializer.Serialize(new { Revision = Guid.NewGuid(), Configurations = new[] { saved } }) });
        await f.Db.SaveChangesAsync();
        state = await f.Service.GetConfigurationAsync();
        Assert.Equal("runtime.proof.employeeId", Assert.Single(Assert.Single(state.Configurations).Mappings).ValueSource);
        Assert.Equal("Unavailable", (await f.Service.ResolveAsync("profile", id)).Status);
        var retained = await f.Service.SetConfigurationAsync(new(state.Revision, state.Configurations));
        Assert.Equal(JsonSerializer.Serialize(state.Configurations), JsonSerializer.Serialize(retained.Configurations));
        f.Source.AllowedValueSources["employeeId"] = ["runtime.directory.employeeId"];
        Assert.False(Assert.Single((await f.Service.GetConfigurationAsync()).Sources).Available);
    }

    [Fact]
    public async Task NativeFill_ShouldUseIndependentQueryWithoutLoginAndPreserveFieldProtection()
    {
        using var f = new Fixture(nativeMode: true);
        var id = await f.SeedAsync();
        var user = f.Db.Users.Single();
        var person = f.Db.Persons.Single();
        user.FirstName = "existing";
        f.NativeFields.ApplyManual(person, new Dictionary<NativeField, string?> { [NativeField.LastName] = "" });
        await f.Db.SaveChangesAsync();
        await f.ConfigureAsync("employeeId", "person.employeeId");
        var approval = await f.Service.GetApprovalAsync("native-profile", "example", id);
        await f.Service.SetApprovalAsync("native-profile", "example", id, new(approval.Revision, true));
        var context = (await f.Service.ResolveAsync("native-profile", id)).Context!;
        var changes = await f.NativeFields.ApplyMissingNeutralSourceAsync(user, person, context,
            new Dictionary<NativeField, string?> { [NativeField.FirstName] = "source", [NativeField.LastName] = "source", [NativeField.Department] = "source" });
        Assert.Equal("existing", user.FirstName);
        Assert.Equal("", person.LastName);
        Assert.Equal("source", user.Department);
        Assert.Contains(changes, x => x.Target == "Person" && x.Field == "LastName" && x.Action == "manual_fixed");
        Assert.Empty(f.Db.UserLogins);
        await f.Db.SaveChangesAsync();
        var state = await f.NativeFields.GetTargetAsync(NativeFieldTarget.User, id);
        var owner = state.Fields.Single(x => x.Field == NativeField.Department).Owner!;
        Assert.Equal(NativeFieldSource.NeutralQuery, owner.Source);
        Assert.Equal("example", owner.QuerySourceId);
        Assert.Empty(owner.StableSubject);
        Assert.Contains(owner, state.AvailableBindings);
    }

    [Fact]
    public async Task NativeFill_ShouldBlockPausedAndStaleContextsAndPreserveSharedPerson()
    {
        using var f = new Fixture(nativeMode: true);
        var id = await f.SeedAsync();
        var person = f.Db.Persons.Single();
        f.Db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id });
        await f.Db.SaveChangesAsync();
        await f.ConfigureAsync("employeeId", "person.employeeId");
        var approval = await f.Service.GetApprovalAsync("native-profile", "example", id);
        await f.Service.SetApprovalAsync("native-profile", "example", id, new(approval.Revision, true));
        var context = (await f.Service.ResolveAsync("native-profile", id)).Context!;
        var user = f.Db.Users.Single(x => x.Id == id);
        var changes = await f.NativeFields.ApplyMissingNeutralSourceAsync(user, person, context,
            new Dictionary<NativeField, string?> { [NativeField.Department] = "source" });
        Assert.Null(person.Department);
        Assert.Contains(changes, x => x.Target == "Person" && x.Field == "Department" && x.Action == "shared_person");
        f.Db.ChangeTracker.Clear();
        user = f.Db.Users.Single(x => x.Id == id);
        person = f.Db.Persons.Single();
        var source = (await f.NativeFields.GetSourcesAsync()).Single(x => x.Source == NativeFieldSource.NeutralQuery);
        await f.NativeFields.SetPausedAsync(NativeFieldSource.NeutralQuery, new(true, source.Revision));
        Assert.Equal("source_unavailable", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => f.NativeFields.ApplyMissingNeutralSourceAsync(
            user, person, context, new Dictionary<NativeField, string?>()))).Code);
        f.Source.PolicyRevision = "changed";
        Assert.Equal("conflict", (await Assert.ThrowsAsync<NativeFieldPolicyException>(() => f.NativeFields.ApplyMissingNeutralSourceAsync(
            user, person, context, new Dictionary<NativeField, string?>()))).Code);
    }

    [Fact]
    public async Task MetadataEvidence_ShouldBePrivateFromGenericSettings()
    {
        using var f = new Fixture();
        var key = SettingKeys.NeutralMetadataEvidencePrefix + "synthetic";
        f.Db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = key, Value = "private-evidence" });
        await f.Db.SaveChangesAsync();
        var settings = new SettingsService(f.Db, new MemoryCache(new MemoryCacheOptions()), f.Protection);
        Assert.Null(await settings.GetValueAsync(key));
        Assert.DoesNotContain(key, (await settings.GetByPrefixAsync("")).Keys);
        await Assert.ThrowsAsync<Core.Application.SystemManagedSettingException>(() => settings.SetValueAsync(key, "replacement"));
    }

    [Fact]
    public void ProfileOptions_ShouldAcceptMatchingNeutralTransportButRetainSchemaAndLegacyChecks()
    {
        var options = new ProviderProfileOptions { Enabled = true, Sources = new() { ["example"] = new()
            { ProviderNamespace = "example.provider", AllowedProperties = new() { ["flag"] = "Boolean" } } },
            QuerySources = new() { ["example"] = new() { Enabled = true, Trusted = true, ProviderNamespace = "example.provider",
                Endpoint = "https://example.invalid/api/neutral/profile", SharedSecret = "synthetic" } } };
        var validator = new ProviderProfileOptionsValidator();
        Assert.True(validator.Validate(null, options).Succeeded);
        options.QuerySources["example"].ProviderNamespace = "other.provider";
        Assert.True(validator.Validate(null, options).Failed);
        options.QuerySources["example"].ProviderNamespace = "example.provider";
        options.Sources["example"].AllowedProperties["flag"] = "Object";
        Assert.True(validator.Validate(null, options).Failed);
    }

    private static NeutralQueryRequest Request(string type) => new()
    {
        ContractType = type, ProviderNamespace = "example.provider", Query = new(new Dictionary<string, string> { ["employeeId"] = "0001" }),
        Scheme = type == "recovery-identity-verification" ? "identity-identifier" : null,
        Evidence = type == "recovery-identity-verification" ? new("submitted-synthetic") : null,
        OperationId = type == "password-sync" ? Guid.NewGuid().ToString() : null, Password = type == "password-sync" ? "synthetic-only" : null
    };
    private static byte[] Response(NeutralQueryRequest request, string outcome, string? result = null) => Encoding.UTF8.GetBytes(
        $"{{\"contractType\":\"{request.ContractType}\",\"contractVersion\":\"2.0\",\"requestId\":\"{request.RequestId}\",\"providerNamespace\":\"{request.ProviderNamespace}\",\"queryOutcome\":\"{outcome}\"" +
        (request.OperationId is null ? "" : $",\"operationId\":\"{request.OperationId}\"") + (result is null ? "" : ",\"result\":" + result) + "}");

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ApplicationDbContext Db { get; }
        public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
        public NeutralQuerySourceOptions Source { get; } = new()
        {
            Enabled = true, Trusted = true, ProviderNamespace = "example.provider", PolicyRevision = "first",
            Endpoint = "https://example.invalid/api/neutral/profile", SharedSecret = "synthetic-secret",
            AcceptedAttributeSets = [["employeeId"]], AllowedValueSources = new() { ["employeeId"] = ["person.employeeId"] }
        };
        public NeutralQueryService Service { get; }
        public NativeFieldPolicyService NativeFields { get; }
        public ProviderNativeProfileOptions NativeOptions { get; }
        private readonly string _capability;
        public DirectoryNativeProfileOptions DirectoryNative { get; } = new();
        public DirectoryIntegrationOptions DirectoryIntegration { get; } = new();
        public DirectoryLookupOptions DirectoryLookup { get; } = new();
        public DirectoryCredentialTransportOptions DirectoryConnection { get; } = new();
        public Fixture(bool nativeMode = false, bool capabilityEnabled = true, bool direct = false)
        {
            _capability = nativeMode ? "native-profile" : "profile";
            _connection.Open();
            Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
            NativeFields = new NativeFieldPolicyService(Db, Options.Create(new NativeFieldPolicyOptions()),
                Options.Create(new ProviderProofOptions()), Options.Create(new DirectoryIntegrationOptions()), () => Service!);
            NativeOptions = nativeMode ? new ProviderNativeProfileOptions { Enabled = capabilityEnabled, QuerySources = new() { ["example"] = Source } } : new();
            if (direct)
            {
                NativeOptions.Enabled = false;
                NativeOptions.QuerySources.Clear();
                DirectoryNative.Enabled = true;
                DirectoryNative.Trusted = true;
                DirectoryNative.SourceId = "example";
                DirectoryNative.ProviderNamespace = "example.provider";
                DirectoryNative.PolicyRevision = "first";
                DirectoryNative.SamAccountNameQueryAttribute = "employeeId";
                DirectoryNative.AllowedValueSources = ["person.employeeId"];
                DirectoryIntegration.Enabled = true;
                DirectoryConnection.Host = "directory.example.invalid";
                DirectoryConnection.BaseDistinguishedName = "DC=example,DC=invalid";
                DirectoryConnection.ManagedSearchFilter = "(objectCategory=person)";
            }
            Service = new(Db, Protection, Monitor(NativeOptions),
                Monitor(nativeMode ? new ProviderProfileOptions() : new ProviderProfileOptions { Enabled = capabilityEnabled, QuerySources = new() { ["example"] = Source } }),
                Monitor(new ProviderMetadataRefreshOptions()), Monitor(new ProviderLifecycleOptions()),
                Monitor(new RecoveryIdentityVerificationOptions()), Monitor(new LegacyPasswordSyncOptions()), NativeFields,
                Monitor(DirectoryNative), Options.Create(DirectoryIntegration), Options.Create(DirectoryLookup), Options.Create(DirectoryConnection));
        }
        public AdminProfileSyncService CreateDirectorySync(IProtectedDirectoryIdentityTransport transport, IProviderNativeProfileClient? client = null) =>
            new(Db, NativeFields, client ?? new Mock<IProviderNativeProfileClient>(MockBehavior.Strict).Object, Options.Create(NativeOptions),
                Protection, new UpperInvariantLookupNormalizer(), Mock.Of<Core.Application.IAuditService>(), queries: Service,
                directoryLookup: new ProtectedDirectoryIdentityLookup(transport, Options.Create(DirectoryIntegration), Options.Create(DirectoryLookup)));
        public async Task ApproveAsync(Guid id)
        {
            var approval = await Service.GetApprovalAsync("native-profile", "example", id);
            await Service.SetApprovalAsync("native-profile", "example", id, new(approval.Revision, true));
            Db.ChangeTracker.Clear();
        }
        public async Task<Guid> SeedAsync()
        {
            var person = new Person { Id = Guid.NewGuid(), EmployeeId = " 0001 " };
            var user = new ApplicationUser { Id = Guid.NewGuid(), PersonId = person.Id, EmployeeId = "different", SecurityStamp = "security" };
            Db.Persons.Add(person); Db.Users.Add(user); await Db.SaveChangesAsync();
            return user.Id;
        }
        public async Task<QueryConfigurationStateDto> ConfigureAsync(string attribute, string valueSource) =>
            await Service.SetConfigurationAsync(new((await Service.GetConfigurationAsync()).Revision,
                [new(_capability, "example", "neutral-v2", "example.provider", [new(attribute, valueSource)])]));
        private static IOptionsMonitor<T> Monitor<T>(T value) where T : class
        { var mock = new Mock<IOptionsMonitor<T>>(); mock.SetupGet(x => x.CurrentValue).Returns(value); return mock.Object; }
        public void Dispose() { Db.Dispose(); _connection.Dispose(); }
    }
}
