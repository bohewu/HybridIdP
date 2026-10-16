using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;
using Web.IdP.Extensions;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class DashboardAuthorizationTests
{
    [Theory]
    [InlineData("User", false, HttpStatusCode.Forbidden)]
    [InlineData("ApplicationManager", false, HttpStatusCode.Forbidden)]
    [InlineData("Admin", true, HttpStatusCode.Forbidden)]
    [InlineData("Admin", false, HttpStatusCode.OK)]
    public async Task Stats_ShouldRequireMonitoringAuthority(string role, bool bearer, HttpStatusCode expected)
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = CreateClient(app);
        client.DefaultRequestHeaders.Add("X-Fixture-Role", role);
        if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");
        Assert.Equal(expected, (await client.GetAsync("/api/admin/dashboard/stats")).StatusCode);
        Mock.Get(app.Services.GetRequiredService<IOpenIddictApplicationManager>())
            .Verify(manager => manager.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
                expected == HttpStatusCode.OK ? Times.Once() : Times.Never());
        await app.StopAsync();
    }

    [Theory]
    [InlineData(null, false, HttpStatusCode.Unauthorized)]
    [InlineData("User", false, HttpStatusCode.Forbidden)]
    [InlineData("ApplicationManager", false, HttpStatusCode.Forbidden)]
    [InlineData("Admin", true, HttpStatusCode.Forbidden)]
    public async Task Health_ShouldRejectUnauthorizedRequestsBeforeCheckingDependencies(
        string? role, bool bearer, HttpStatusCode expected)
    {
        var calls = 0;
        await using var app = CreateApp(healthChecks => healthChecks.AddCheck("sqlserver", () =>
        {
            Interlocked.Increment(ref calls);
            return HealthCheckResult.Healthy();
        }));
        await app.StartAsync();
        using var client = CreateClient(app);
        if (role != null) client.DefaultRequestHeaders.Add("X-Fixture-Role", role);
        if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");

        using var response = await client.GetAsync("/api/admin/dashboard/health");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, calls);
        await app.StopAsync();
    }

    [Theory]
    [InlineData("sqlserver", "sqlserver", HealthStatus.Healthy, true)]
    [InlineData("sqlserver", "sqlserver", HealthStatus.Degraded, true)]
    [InlineData("sqlserver", "sqlserver", HealthStatus.Unhealthy, true)]
    [InlineData("Database (PostgreSQL)", "postgresql", HealthStatus.Healthy, false)]
    public async Task Health_ShouldReturnOnlySanitizedRegisteredDependencyStatus(
        string registeredName, string responseName, HealthStatus status, bool redisEnabled)
    {
        const string sensitive = "fixture-secret@internal-db:1433";
        var calls = 0;
        var unexpectedCalls = 0;
        await using var app = CreateApp(healthChecks =>
        {
            healthChecks.AddCheck(registeredName, () =>
            {
                Interlocked.Increment(ref calls);
                return new HealthCheckResult(status, sensitive, new Exception(sensitive),
                    new Dictionary<string, object> { ["connectionString"] = sensitive });
            });
            if (redisEnabled) healthChecks.AddCheck("redis", () => HealthCheckResult.Healthy(sensitive));
            healthChecks.AddCheck(sensitive, () =>
            {
                Interlocked.Increment(ref unexpectedCalls);
                return HealthCheckResult.Unhealthy(sensitive);
            });
        });
        await app.StartAsync();
        using var client = CreateClient(app);
        client.DefaultRequestHeaders.Add("X-Fixture-Role", "Admin");
        var before = DateTimeOffset.UtcNow;

        using var response = await client.GetAsync("/api/admin/dashboard/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain(sensitive, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        var report = document.RootElement;
        Assert.Equal(3, report.EnumerateObject().Count());
        Assert.Equal(status.ToString(), report.GetProperty("status").GetString());
        var checkedAt = report.GetProperty("checkedAtUtc").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, checkedAt.Offset);
        Assert.InRange(checkedAt, before, DateTimeOffset.UtcNow);
        var entries = report.GetProperty("entries");
        Assert.Equal(redisEnabled ? 2 : 1, entries.EnumerateObject().Count());
        Assert.Equal(status.ToString(), entries.GetProperty(responseName).GetProperty("status").GetString());
        Assert.Single(entries.GetProperty(responseName).EnumerateObject());
        if (redisEnabled)
        {
            Assert.Equal("Healthy", entries.GetProperty("redis").GetProperty("status").GetString());
            Assert.Single(entries.GetProperty("redis").EnumerateObject());
        }
        Assert.Equal(1, calls);
        Assert.Equal(0, unexpectedCalls);
        await app.StopAsync();
    }

    private static WebApplication CreateApp(Action<IHealthChecksBuilder>? configureHealthChecks = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["RateLimiting:Enabled"] = "false"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(DashboardController).Assembly);
        builder.Services.AddAntiforgery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(IdentityConstants.ApplicationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(AdministrativeAuthorizationBoundary.BearerScheme, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(Permissions.Monitoring.Read,
            policy => policy.AddRequirements(new PermissionRequirement(Permissions.Monitoring.Read))));
        builder.Services.AddScoped<IAdministrativeAuthorizationBoundary, AdministrativeAuthorizationBoundary>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddCustomRateLimiting(builder.Configuration);
        configureHealthChecks?.Invoke(builder.Services.AddHealthChecks());
        var roles = new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null);
        builder.Services.AddSingleton(roles.Object);
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.SetupGet(manager => manager.Users).Returns(Array.Empty<ApplicationUser>().AsQueryable());
        builder.Services.AddSingleton(users.Object);
        var applications = new Mock<IOpenIddictApplicationManager>();
        applications.Setup(manager => manager.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyApplications());
        builder.Services.AddSingleton(applications.Object);
        var scopes = new Mock<IOpenIddictScopeManager>();
        scopes.Setup(manager => manager.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyApplications());
        builder.Services.AddSingleton(scopes.Object);
        var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app) =>
        new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };

    private static async IAsyncEnumerable<object> EmptyApplications()
    {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Fixture-Role"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var bearer = Request.Headers.Authorization.Count > 0;
            if (bearer != (Scheme.Name == AdministrativeAuthorizationBoundary.BearerScheme))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, Request.Headers["X-Fixture-Role"].ToString()),
                new Claim("scope", Permissions.Monitoring.Read)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
