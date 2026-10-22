using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Collections.Immutable;
using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Authorization;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class QueryConfigurationsControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsRead_ShouldOmitGlobalSourceDocumentsForDelegatedCookieAndApprovedBearer(bool bearer)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var internalKeys = new[] { SettingKeys.NeutralQueryConfiguration, SettingKeys.NativeFieldMappings };
        foreach (var key in internalKeys)
            db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = key, Value = "synthetic-internal-document" });
        db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = "system.visible-setting", Value = "visible-value" });
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        // A previously populated cache must not reopen the exact-key path.
        foreach (var key in internalKeys) cache.Set("settings:" + key, "synthetic-cached-document");
        var settings = new SettingsService(db, cache, new EphemeralDataProtectionProvider());
        await using var app = CreateApp(Mock.Of<INeutralQueryService>(), Mock.Of<IAuditService>(),
            bearer ? Permissions.Settings.Read : null, settings);
        await app.StartAsync();
        try
        {
            using var client = Client(app, Permissions.Settings.Read, bearer);
            foreach (var key in internalKeys)
            {
                using var exact = await client.GetAsync("/api/admin/settings/" + key);
                Assert.Equal(HttpStatusCode.NotFound, exact.StatusCode);
                Assert.DoesNotContain("synthetic-", await exact.Content.ReadAsStringAsync());
                using var prefix = await client.GetAsync("/api/admin/settings?prefix=" + key);
                Assert.Equal(HttpStatusCode.OK, prefix.StatusCode);
                Assert.Empty((await prefix.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
            }
            using var all = await client.GetAsync("/api/admin/settings?prefix=system.");
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
            var visible = Assert.Single((await all.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
            Assert.Equal("system.visible-setting", visible.GetProperty("key").GetString());
            Assert.Equal("visible-value", visible.GetProperty("value").GetString());
            using var unrelated = await client.GetAsync("/api/admin/settings/system.visible-setting");
            Assert.Equal(HttpStatusCode.OK, unrelated.StatusCode);
            Assert.Equal("visible-value", (await unrelated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetString());
            Assert.Equal(3, await db.Settings.CountAsync());
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData(false, false, false, "settings.read", "settings.read", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(false, true, false, "settings.read", "settings.read", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(false, true, true, "settings.read", "settings.read", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(false, true, false, "users.read", "settings.read", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(true, false, false, "settings.read", "settings.read", HttpStatusCode.OK, HttpStatusCode.Unauthorized)]
    [InlineData(true, true, false, "settings.read", "users.read", HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized)]
    public async Task GlobalReadAndPage_ShouldRequireLocalAdminOrProvisionedApiGrant(
        bool bearer, bool admin, bool appRole, string permissions, string ceiling,
        HttpStatusCode apiExpected, HttpStatusCode pageExpected)
    {
        var queries = new Mock<INeutralQueryService>(MockBehavior.Strict);
        queries.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryConfigurationStateDto(Guid.NewGuid(), [], []));
        await using var app = CreateApp(queries.Object, Mock.Of<IAuditService>(), ceiling);
        await app.StartAsync();
        try
        {
            using var client = Client(app, permissions, bearer);
            if (admin) client.DefaultRequestHeaders.Add("X-Fixture-Admin", "true");
            if (appRole) client.DefaultRequestHeaders.Add("X-Fixture-App-Role", "true");
            using var api = await client.GetAsync("/api/admin/query-configuration");
            using var page = await client.GetAsync("/fixture-data-sources");
            Assert.Equal(apiExpected, api.StatusCode);
            Assert.Equal(pageExpected, page.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData("settings.update", HttpStatusCode.OK)]
    [InlineData("settings.read", HttpStatusCode.Forbidden)]
    public async Task GlobalSave_ShouldRetainProvisionedBearerCeilingWithoutCookieCsrf(string ceiling, HttpStatusCode expected)
    {
        var queries = new Mock<INeutralQueryService>(MockBehavior.Strict);
        queries.Setup(x => x.SetConfigurationAsync(It.IsAny<QueryConfigurationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryConfigurationStateDto(Guid.NewGuid(), [], []));
        await using var app = CreateApp(queries.Object, Mock.Of<IAuditService>(), ceiling);
        await app.StartAsync();
        try
        {
            using var client = Client(app, Permissions.Settings.Update, true);
            using var response = await client.PutAsJsonAsync("/api/admin/query-configuration", new QueryConfigurationRequest(Guid.NewGuid(), []));
            Assert.Equal(expected, response.StatusCode);
            queries.Verify(x => x.SetConfigurationAsync(It.IsAny<QueryConfigurationRequest>(), It.IsAny<CancellationToken>()),
                expected == HttpStatusCode.OK ? Times.Once() : Times.Never());
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData(null, false, false, HttpStatusCode.Unauthorized, true)]
    [InlineData("settings.read", false, true, HttpStatusCode.Forbidden, true)]
    [InlineData("users.update", false, true, HttpStatusCode.Forbidden, true)]
    [InlineData("settings.update", true, true, HttpStatusCode.Forbidden, true)]
    [InlineData("settings.update", false, false, HttpStatusCode.BadRequest, true)]
    [InlineData("settings.update", false, true, HttpStatusCode.OK, true)]
    [InlineData("settings.update", false, true, HttpStatusCode.Forbidden, false)]
    public async Task Save_ShouldEnforcePermissionCsrfAndValueFreeAudit(string? permission, bool bearer, bool csrf, HttpStatusCode expected, bool admin)
    {
        var queries = new Mock<INeutralQueryService>(MockBehavior.Strict);
        var audit = new Mock<IAuditService>(MockBehavior.Strict);
        var request = new QueryConfigurationRequest(Guid.NewGuid(),
            [new("profile", "example", "neutral-v2", "example.provider", [new("employeeId", "person.employeeId")])]);
        queries.Setup(x => x.SetConfigurationAsync(It.IsAny<QueryConfigurationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryConfigurationStateDto(Guid.NewGuid(), request.Configurations, []));
        string? details = null;
        audit.Setup(x => x.LogAdministrativeEventAsync("QueryConfigurationChanged", "QueryConfiguration", "global", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, string?, CancellationToken>((_, _, _, text, _) => details = text).Returns(Task.CompletedTask);
        await using var app = CreateApp(queries.Object, audit.Object);
        await app.StartAsync();
        try
        {
            using var client = Client(app, permission, bearer);
            if (admin) client.DefaultRequestHeaders.Add("X-Fixture-Admin", "true");
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await client.GetStringAsync("/fixture-antiforgery"));
            using var response = await client.PutAsJsonAsync("/api/admin/query-configuration", request);
            Assert.Equal(expected, response.StatusCode);
            queries.Verify(x => x.SetConfigurationAsync(It.IsAny<QueryConfigurationRequest>(), It.IsAny<CancellationToken>()),
                expected == HttpStatusCode.OK ? Times.Once() : Times.Never());
            if (expected == HttpStatusCode.OK) Assert.Equal("{\"ConfigurationCount\":1}", details);
            else Assert.Null(details);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData(null, false, HttpStatusCode.Unauthorized)]
    [InlineData("users.read", true, HttpStatusCode.Forbidden)]
    [InlineData("settings.read", true, HttpStatusCode.OK)]
    [InlineData("settings.read", false, HttpStatusCode.Forbidden)]
    public async Task Read_ShouldRequireSettingsRead(string? permission, bool admin, HttpStatusCode expected)
    {
        var queries = new Mock<INeutralQueryService>(MockBehavior.Strict);
        queries.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new QueryConfigurationStateDto(Guid.NewGuid(), [], []));
        await using var app = CreateApp(queries.Object, Mock.Of<IAuditService>());
        await app.StartAsync();
        try
        {
            using var client = Client(app, permission, false);
            if (admin) client.DefaultRequestHeaders.Add("X-Fixture-Admin", "true");
            using var response = await client.GetAsync("/api/admin/query-configuration");
            Assert.Equal(expected, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData("settings.update", true, HttpStatusCode.Forbidden)]
    [InlineData("users.update", true, HttpStatusCode.Forbidden)]
    [InlineData("settings.update,users.update", false, HttpStatusCode.BadRequest)]
    [InlineData("settings.update,users.update", true, HttpStatusCode.OK)]
    public async Task Approval_ShouldRequireBothSettingsAndUserAuthorityAndCsrf(string permission, bool csrf, HttpStatusCode expected)
    {
        var id = Guid.NewGuid();
        var queries = new Mock<INeutralQueryService>(MockBehavior.Strict);
        queries.Setup(x => x.SetApprovalAsync("profile", "example", id, It.IsAny<QueryApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryApprovalStateDto(Guid.NewGuid(), true, ["person.employeeId"], false));
        var audit = new Mock<IAuditService>(MockBehavior.Strict);
        string? details = null;
        audit.Setup(x => x.LogAdministrativeEventAsync("QueryValueApprovalChanged", "QueryValueApproval", id.ToString(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, string?, CancellationToken>((_, _, _, text, _) => details = text).Returns(Task.CompletedTask);
        await using var app = CreateApp(queries.Object, audit.Object);
        await app.StartAsync();
        try
        {
            using var client = Client(app, permission, false);
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await client.GetStringAsync("/fixture-antiforgery"));
            using var response = await client.PutAsJsonAsync($"/api/admin/query-configuration/profile/example/users/{id}/approval", new QueryApprovalRequest(Guid.NewGuid(), true));
            Assert.Equal(expected, response.StatusCode);
            queries.Verify(x => x.SetApprovalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<QueryApprovalRequest>(), It.IsAny<CancellationToken>()),
                expected == HttpStatusCode.OK ? Times.Once() : Times.Never());
            if (expected == HttpStatusCode.OK) Assert.Equal("{\"Approved\":true}", details);
            else Assert.Null(details);
        }
        finally { await app.StopAsync(); }
    }

    private static HttpClient Client(WebApplication app, string? permission, bool bearer)
    {
        var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        if (permission is not null) client.DefaultRequestHeaders.Add("X-Fixture-Permission", permission);
        if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");
        return client;
    }

    private static WebApplication CreateApp(INeutralQueryService queries, IAuditService audit, string? bearerCeiling = null,
        ISettingsService? settings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1" });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(QueryConfigurationsController).Assembly);
        builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(IdentityConstants.ApplicationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(AdministrativeAuthorizationBoundary.BearerScheme, _ => { });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(DataSourceAdministrationRequirement.AdministrationPolicy,
                p => p.AddRequirements(new DataSourceAdministrationRequirement(true)));
            o.AddPolicy(DataSourceAdministrationRequirement.InteractivePolicy,
                p => p.AddRequirements(new DataSourceAdministrationRequirement(false)));
            foreach (var permission in new[] { Permissions.Settings.Read, Permissions.Settings.Update, Permissions.Users.Read, Permissions.Users.Update })
                o.AddPolicy(permission, p => p.AddRequirements(new PermissionRequirement(permission)));
        });
        builder.Services.AddScoped<IAdministrativeAuthorizationBoundary, AdministrativeAuthorizationBoundary>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, DataSourceAdministrationAuthorizationHandler>();
        builder.Services.AddSingleton(new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null).Object);
        var applications = new Mock<IOpenIddictApplicationManager>();
        if (bearerCeiling is not null)
        {
            var application = new object();
            applications.Setup(x => x.FindByIdAsync("fixture-application", It.IsAny<CancellationToken>())).ReturnsAsync(application);
            applications.Setup(x => x.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("fixture-client");
            applications.Setup(x => x.GetPropertiesAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(
                ImmutableDictionary<string, JsonElement>.Empty.Add(AdministrativeClientGrant.PermissionsProperty,
                    JsonSerializer.SerializeToElement(bearerCeiling.Split(','))));
        }
        builder.Services.AddSingleton(applications.Object);
        builder.Services.AddSingleton(queries);
        builder.Services.AddSingleton(audit);
        if (settings is not null)
        {
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton(Mock.Of<IEmailService>());
            builder.Services.Configure<EmailOptions>(_ => { });
        }
        var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/fixture-antiforgery", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
        app.MapControllers();
        app.MapGet("/fixture-data-sources", () => Results.Ok()).RequireAuthorization(
            typeof(global::Web.IdP.Pages.Admin.DataSourcesModel).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().ToArray());
        return app;
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Fixture-Permission")) return Task.FromResult(AuthenticateResult.NoResult());
            var bearer = Request.Headers.Authorization.Count > 0;
            if (bearer != (Scheme.Name == AdministrativeAuthorizationBoundary.BearerScheme)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = Request.Headers["X-Fixture-Permission"].ToString().Split(',').Select(x => new Claim("permission", x)).ToList();
            claims.Add(new(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"));
            claims.Add(new("active_role", "Delegated"));
            if (Request.Headers.ContainsKey("X-Fixture-Admin")) claims.Add(new(ClaimTypes.Role, AuthConstants.Roles.Admin));
            if (Request.Headers.ContainsKey("X-Fixture-App-Role")) claims.Add(new("app_role", AuthConstants.Roles.Admin));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            if (bearer)
            {
                identity.SetClaim(AdministrativeClientGrant.ApplicationClaim, "fixture-application");
                identity.SetClaim(OpenIddictConstants.Claims.Subject, "fixture-client");
                identity.SetPresenters("fixture-client");
                identity.SetScopes(Request.Headers["X-Fixture-Permission"].ToString().Split(','));
            }
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
