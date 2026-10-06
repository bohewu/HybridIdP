using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;

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
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*" });
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
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Add("X-Fixture-Role", role);
        if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");
        Assert.Equal(expected, (await client.GetAsync("/api/admin/dashboard/stats")).StatusCode);
        applications.Verify(manager => manager.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            expected == HttpStatusCode.OK ? Times.Once() : Times.Never());
        await app.StopAsync();
    }

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
            var bearer = Request.Headers.Authorization.Count > 0;
            if (bearer != (Scheme.Name == AdministrativeAuthorizationBoundary.BearerScheme))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, Request.Headers["X-Fixture-Role"].ToString()),
                new Claim("scope", Permissions.Monitoring.Read)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
