using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Authorization;
using Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Microsoft.Extensions.DependencyInjection;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Services;

public sealed class ClaimsPreviewServiceTests
{
    [Theory]
    [InlineData(null, "\u6e2c\u8a66\u59d3\u540d", "\u6e2c\u8a66\u59d3\u540d")]
    [InlineData("Given", "Source full name", "Given")]
    [InlineData(null, "", "fixture-user")]
    public async Task Preview_ShouldProjectEffectiveNativeNameAndPersonEmployeeId_WithoutWriting(
        string? firstName, string nickname, string expected)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var user = await fixture.Db.Users.SingleAsync();
        user.FirstName = firstName;
        user.Nickname = nickname;
        (await fixture.Db.Persons.SingleAsync()).EmployeeId = "ORG-104";
        fixture.Db.ScopeClaims.Add(new ScopeClaim { ScopeId = "academic", ScopeName = "academic",
            ClaimDefinition = new ClaimDefinition { Name = "employee_id", DisplayName = "Organization ID",
                ClaimType = "employee_id", DataType = "String", UserPropertyPath = "Person.EmployeeId" } });
        fixture.Db.ScopeClaims.Add(new ScopeClaim { ScopeId = "profile", ScopeName = "profile",
            ClaimDefinition = new ClaimDefinition { Name = "preferred_username", DisplayName = "Account",
                ClaimType = "preferred_username", DataType = "String", UserPropertyPath = "UserName" } });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var saves = fixture.Db.Saves;

        var result = await fixture.Service.PreviewAsync(fixture.Request("openid", "profile", "academic"));

        foreach (var payload in new[] { result.IdToken, result.AccessToken, result.UserInfo })
        {
            Assert.Equal(expected, payload["name"].GetString());
            Assert.Equal("ORG-104", payload["employee_id"].GetString());
            Assert.Equal(fixture.User.Id.ToString(), payload["sub"].GetString());
            Assert.Equal("fixture-user", payload["preferred_username"].GetString());
        }
        Assert.Equal(saves, fixture.Db.Saves);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        fixture.WritingProfiles.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("[\"one\"]", 1)]
    [InlineData("[\"two\",\"one\",\"one\"]", 2)]
    public async Task Preview_ShouldShareTypedProjectionAndLeaveStateUnchanged(string array, int length)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Properties["tags"] = JsonSerializer.Deserialize<JsonElement>(array);
        var before = fixture.Db.Saves;
        var result = await fixture.Service.PreviewAsync(fixture.Request("openid", "profile", "academic"));
        Assert.True(result.HypotheticalScopes);
        foreach (var payload in new[] { result.IdToken, result.AccessToken, result.UserInfo })
        {
            Assert.Equal(length, payload["tags"].GetArrayLength());
            Assert.Equal(JsonValueKind.False, payload["flag"].ValueKind);
            Assert.Equal(JsonValueKind.String, payload["code"].ValueKind);
            Assert.Equal("fixture-sensitive-code", payload["code"].GetString());
            Assert.False(payload.ContainsKey("missing"));
            Assert.False(payload.ContainsKey("amr"));
            Assert.False(payload.ContainsKey("auth_time"));
            Assert.False(payload.ContainsKey("jti"));
            Assert.False(payload.ContainsKey("permission"));
            Assert.Equal(fixture.User.Id.ToString(), payload["sub"].GetString());
        }
        Assert.Contains(result.Reasons, r => r.ClaimType == "missing" && r.Status == "unknown");
        Assert.DoesNotContain(fixture.Logs, line => line.Contains("fixture-sensitive-code") || line.Contains("fixture-sensitive-name"));
        Assert.Equal(before, fixture.Db.Saves);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        Assert.Equal("unchanged-snapshot", (await fixture.Db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
        Assert.Equal("unchanged-refresh-hash", (await fixture.Db.UserSessions.AsNoTracking().SingleAsync()).CurrentRefreshTokenHash);
        Assert.Equal(fixture.LoginDate, (await fixture.Db.Users.AsNoTracking().SingleAsync()).LastLoginDate);
        Assert.Empty(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Empty(await fixture.Db.LoginHistories.ToListAsync());
        fixture.WritingProfiles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Preview_ShouldExplainDeselectedAllowedMappingsWithoutReadingTheirSources()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var result = await fixture.Service.PreviewAsync(fixture.Request("openid"));
        Assert.False(result.IdToken.ContainsKey("name"));
        Assert.False(result.AccessToken.ContainsKey("preferred_username"));
        Assert.False(result.UserInfo.ContainsKey("code"));
        Assert.Contains(result.Reasons, r => r.ClaimType == "code" && r.Code == "scope_not_selected");
        Assert.Contains(result.Reasons, r => r.ClaimType == "name" && r.Code == "scope_not_selected");
        fixture.Reader.VerifyNoOtherCalls();
        Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("4", true)]
    [InlineData("3", false)]
    public async Task Preview_ShouldEvaluateGraduateConditionWithoutPriorLoginOrSnapshot(string divis, bool expected)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Properties["divis"] = JsonSerializer.SerializeToElement(divis);
        (await fixture.Db.Persons.SingleAsync()).ProviderProfilesJson = null;
        (await fixture.Db.Users.SingleAsync()).LastLoginDate = null;
        fixture.Db.ScopeClaims.Add(new ScopeClaim { ScopeId = "academic", ScopeName = "academic", AlwaysInclude = true,
            ClaimDefinition = new ClaimDefinition { Name = "is_graduate_student", DisplayName = "Graduate student",
                ClaimType = "is_graduate_student", DataType = "Boolean", ProviderProfileSource = "source", UserPropertyPath = "divis",
                ConditionJson = JsonSerializer.Serialize(new ClaimCondition { Operator = "Equals", Property = "divis",
                    Value = JsonSerializer.SerializeToElement("4") }) } });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var saves = fixture.Db.Saves;

        var result = await fixture.Service.PreviewAsync(fixture.Request("openid", "academic"));

        foreach (var payload in new[] { result.IdToken, result.AccessToken, result.UserInfo })
            Assert.Equal(expected, payload["is_graduate_student"].GetBoolean());
        fixture.Reader.Verify(r => r.ReadForPreviewAsync(It.IsAny<ApplicationUser>(), "source", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(saves, fixture.Db.Saves);
        Assert.Null((await fixture.Db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
        Assert.Null((await fixture.Db.Users.AsNoTracking().SingleAsync()).LastLoginDate);
        fixture.WritingProfiles.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("disabled")]
    [InlineData("link_missing_or_ambiguous")]
    public async Task Preview_ShouldOmitUnknownProfileClaimsEvenWithAlwaysInclude(string status)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.SourceStatus = status;
        fixture.Properties.Clear();
        var result = await fixture.Service.PreviewAsync(fixture.Request("openid", "academic"));
        Assert.All(new[] { result.IdToken, result.AccessToken, result.UserInfo }, p => Assert.False(p.ContainsKey("flag")));
        Assert.Contains(result.Reasons, r => r.ClaimType == "flag" && r.Code == "source_" + status && r.Status == "unknown");
    }

    [Theory]
    [InlineData("m2m", "unsupported_client")]
    [InlineData("scope", "scope_not_allowed")]
    [InlineData("inactive", "account_ineligible")]
    [InlineData("person", "account_ineligible")]
    [InlineData("migration", "account_ineligible")]
    [InlineData("lifecycle", "account_ineligible")]
    public async Task Preview_ShouldDenyIneligibleRequestsBeforeProjection(string scenario, string code)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        switch (scenario)
        {
            case "m2m": fixture.Permissions = [OpenIddictConstants.Permissions.GrantTypes.ClientCredentials]; break;
            case "scope": fixture.ScopeAllowed = false; break;
            case "inactive": (await fixture.Db.Users.SingleAsync()).IsActive = false; break;
            case "person": (await fixture.Db.Persons.SingleAsync()).IsDeleted = true; break;
            case "migration": fixture.MigrationAllowed = false; break;
            case "lifecycle": fixture.LifecycleAllowed = false; break;
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var before = fixture.Db.Saves;
        var error = await Assert.ThrowsAsync<ClaimsPreviewException>(() => fixture.Service.PreviewAsync(fixture.Request("openid", "academic")));
        Assert.Equal(code, error.Code);
        Assert.Equal(before, fixture.Db.Saves);
        fixture.Reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Lookups_ShouldBoundAccountsAndReturnOnlySelectionFields()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        for (var i = 0; i < 110; i++) fixture.Db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = $"search-{i:000}", PasswordHash = "never-expose" });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var result = await fixture.Service.GetAccountsAsync("search-", 5, 1000);
        Assert.Equal(100, result.Count);
        Assert.Equal("search-005", result[0].DisplayName);
        Assert.DoesNotContain("never-expose", JsonSerializer.Serialize(result));
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Lookups_ShouldSearchAndPageClientsInTheStoreWithoutDisclosingProperties()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()).UseOpenIddict<Guid>());
        services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>().ReplaceDefaultEntities<Guid>());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var i = 0; i < 110; i++)
            db.Add(new OpenIddictEntityFrameworkCoreApplication<Guid> { ClientId = $"search-{i:000}", DisplayName = $"Example {i:000}",
                Permissions = JsonSerializer.Serialize(new[] { i == 109 ? "gt:client_credentials" : "gt:authorization_code" }),
                Properties = "{\"internal_value\":\"never-expose\"}" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var fixture = new Fixture(scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>());
        var page = await fixture.Service.GetClientsAsync(" search- ", 5, 1000);
        Assert.Equal(100, page.Count);
        Assert.Equal("search-005", page[0].ClientId);
        Assert.DoesNotContain("never-expose", JsonSerializer.Serialize(page));
        var named = Assert.Single(await fixture.Service.GetClientsAsync("Example 109", 0, 50));
        Assert.Equal("search-109", named.ClientId);
        Assert.False(named.Supported);
        Assert.Empty(db.ChangeTracker.Entries());
        var error = await Assert.ThrowsAsync<ClaimsPreviewException>(() => fixture.Service.GetClientsAsync(new string('x', 201), 0, 50));
        Assert.Equal("invalid_request", error.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public CountingDb Db { get; } = new();
        public ApplicationUser User { get; } = new() { Id = Guid.NewGuid(), UserName = "fixture-user", FirstName = "fixture-sensitive-name" };
        public DateTime LoginDate { get; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public Dictionary<string, JsonElement> Properties { get; } = new()
        {
            ["flag"] = JsonSerializer.SerializeToElement(false), ["code"] = JsonSerializer.SerializeToElement("fixture-sensitive-code")
        };
        public List<string> Logs { get; } = [];
        public Mock<IProviderProfileService> WritingProfiles { get; } = new(MockBehavior.Strict);
        public Mock<IProviderProfilePreviewReader> Reader { get; } = new(MockBehavior.Strict);
        public ImmutableArray<string> Permissions { get; set; } = [OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            "scp:openid", "scp:profile", "scp:academic"];
        public bool ScopeAllowed { get; set; } = true;
        public bool MigrationAllowed { get; set; } = true;
        public bool LifecycleAllowed { get; set; } = true;
        public string SourceStatus { get; set; } = "confirmed";
        public ClaimsPreviewService Service { get; }
        public Fixture(IOpenIddictApplicationManager? applicationManager = null)
        {
            var applications = new Mock<IOpenIddictApplicationManager>();
            var app = new object();
            applications.Setup(m => m.FindByClientIdAsync("fixture-client", It.IsAny<CancellationToken>())).ReturnsAsync(app);
            applications.Setup(m => m.GetPermissionsAsync(app, It.IsAny<CancellationToken>())).ReturnsAsync(() => Permissions);
            var usage = new Mock<IApiScopeUsagePolicy>();
            usage.Setup(p => p.CanUseScopesAsync(app, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => ScopeAllowed);
            Reader.Setup(r => r.ReadForPreviewAsync(It.IsAny<ApplicationUser>(), "source", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ProviderProfileSnapshotResult(SourceStatus, Properties));
            var options = Microsoft.Extensions.Options.Options.Create(new ProviderProfileOptions { Enabled = true, Sources = new()
            { ["source"] = new() { AllowedProperties = new() { ["tags"] = "StringArray", ["flag"] = "Boolean", ["code"] = "String", ["missing"] = "String", ["divis"] = "String" } } } });
            var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
            var roles = new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null);
            var enrichment = new ClaimsEnrichmentService(users.Object, roles.Object, Db, new CaptureLogger<ClaimsEnrichmentService>(Logs), WritingProfiles.Object, options);
            var migration = new Mock<IMigrationIssuanceGuard>();
            migration.Setup(m => m.CanIssueAsync(User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => MigrationAllowed);
            var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
            lifecycle.Setup(m => m.IsEligibleAsync(User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => LifecycleAllowed);
            var resources = new Mock<IApiResourceService>();
            resources.Setup(r => r.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(["fixture-api"]);
            Service = new(Db, applicationManager ?? applications.Object, usage.Object, Reader.Object, enrichment,
                new UserInfoService(Db, new CaptureLogger<UserInfoService>(Logs)), lifecycle.Object, migration.Object, resources.Object);
        }
        public async Task SeedAsync()
        {
            var person = new Person { Id = Guid.NewGuid(), ProviderProfilesJson = "unchanged-snapshot" };
            User.PersonId = person.Id;
            User.LastLoginDate = LoginDate;
            Db.Persons.Add(person); Db.Users.Add(User);
            Db.UserSessions.Add(new UserSession { UserId = User.Id, AuthorizationId = "unchanged-grant", CurrentRefreshTokenHash = "unchanged-refresh-hash" });
            foreach (var (name, type) in new[] { ("tags", "StringArray"), ("flag", "Boolean"), ("code", "String"), ("missing", "String"), ("permission", "String") })
                Db.ScopeClaims.Add(new ScopeClaim { ScopeId = "academic", ScopeName = "academic", AlwaysInclude = true,
                    ClaimDefinition = new ClaimDefinition { Name = name, DisplayName = name, ClaimType = name,
                        DataType = type, ProviderProfileSource = "source", UserPropertyPath = name } });
            Db.ScopeClaims.Add(new ScopeClaim { ScopeId = "profile", ScopeName = "profile",
                ClaimDefinition = new ClaimDefinition { Name = "name", DisplayName = "name", ClaimType = "name", DataType = "String", UserPropertyPath = "FirstName" } });
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }
        public ClaimsPreviewRequest Request(params string[] scopes) => new() { ClientId = "fixture-client", UserId = User.Id, Scopes = scopes };
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class CountingDb() : ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
    {
        public int Saves { get; private set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        { Saves++; return base.SaveChangesAsync(cancellationToken); }
    }
    private sealed class CaptureLogger<T>(List<string> logs) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => logs.Add(formatter(state, exception));
    }
}
