using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class ProfileSyncControllerTests
{
    [Theory]
    [InlineData(null, false, false, HttpStatusCode.Unauthorized)]
    [InlineData("users.update", false, true, HttpStatusCode.Forbidden)]
    [InlineData("persons.update", false, true, HttpStatusCode.Forbidden)]
    [InlineData("users.update,persons.update", true, true, HttpStatusCode.Forbidden)]
    [InlineData("users.update,persons.update", false, false, HttpStatusCode.BadRequest)]
    [InlineData("users.update,persons.update", false, true, HttpStatusCode.OK)]
    public async Task Endpoints_ShouldRequireBothPermissionsAndCookieCsrf(string? permissions, bool bearer, bool csrf,
        HttpStatusCode expected)
    {
        var service = new Mock<IAdminProfileSyncService>(MockBehavior.Strict);
        var id = Guid.NewGuid();
        string? actor = null;
        service.Setup(x => x.GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ProfileSyncStatusDto(true, null));
        service.Setup(x => x.PreviewAsync(It.IsAny<string>(), id, It.IsAny<CancellationToken>()))
            .Callback<string, Guid, CancellationToken>((value, _, _) => actor = value)
            .ReturnsAsync(new ProfileSyncPreviewDto(id, "fixture", null, null, "blocked", "person_missing", [], null));
        service.Setup(x => x.ApplyAsync(It.IsAny<string>(), id, "protected-token", It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, CancellationToken>((value, _, _, _) => actor = value)
            .ReturnsAsync(new ProfileSyncApplyResult("applied", ["FirstName"], []));
        await using var app = CreateApp(service.Object);
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            if (permissions is not null) client.DefaultRequestHeaders.Add("X-Fixture-Permissions", permissions);
            if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await client.GetStringAsync("/fixture-antiforgery"));
            using var preview = await client.PostAsync($"/api/admin/profile-sync/users/{id}/preview", null);
            Assert.Equal(expected, preview.StatusCode);
            using var apply = await client.PostAsJsonAsync($"/api/admin/profile-sync/users/{id}/apply", new { previewToken = "protected-token" });
            Assert.Equal(expected, apply.StatusCode);
            using var status = await client.GetAsync("/api/admin/profile-sync/status");
            Assert.Equal(expected == HttpStatusCode.BadRequest ? HttpStatusCode.OK : expected, status.StatusCode);
            if (expected == HttpStatusCode.OK)
            {
                Assert.Contains("11111111-1111-1111-1111-111111111111", actor);
                Assert.True(preview.Headers.CacheControl?.NoStore);
                Assert.True(apply.Headers.CacheControl?.NoStore);
                Assert.True(status.Headers.CacheControl?.NoStore);
            }
            else Assert.Null(actor);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData("preview_stale", 409)]
    [InlineData("source_unavailable", 503)]
    [InlineData("source_not_found", 404)]
    public async Task Apply_ShouldReturnMachineReadableFailure(string code, int status)
    {
        var service = new Mock<IAdminProfileSyncService>();
        service.Setup(x => x.ApplyAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProfileSyncException(code));
        var boundary = new Mock<IAdministrativeAuthorizationBoundary>();
        boundary.Setup(x => x.ResolveAsync()).ReturnsAsync(new AdministrativeAuthority(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "cookie")),
            false, new HashSet<string>()));
        var controller = new ProfileSyncController(service.Object, boundary.Object)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(
            await controller.ApplyAsync(Guid.NewGuid(), new("token"), CancellationToken.None));
        Assert.Equal(status, result.StatusCode);
        Assert.Contains(code, System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    private static WebApplication CreateApp(IAdminProfileSyncService service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1" });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProfileSyncController).Assembly);
        builder.Services.AddAntiforgery(x => x.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(IdentityConstants.ApplicationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(AdministrativeAuthorizationBoundary.BearerScheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var permission in new[] { Permissions.Users.Update, Permissions.Persons.Update })
                options.AddPolicy(permission, p => p.AddRequirements(new PermissionRequirement(permission)));
        });
        builder.Services.AddScoped<IAdministrativeAuthorizationBoundary, AdministrativeAuthorizationBoundary>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton(new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null).Object);
        builder.Services.AddSingleton(Mock.Of<IOpenIddictApplicationManager>());
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/fixture-antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            antiforgery.GetAndStoreTokens(context).RequestToken!);
        app.MapControllers();
        return app;
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Fixture-Permissions")) return Task.FromResult(AuthenticateResult.NoResult());
            var bearer = Request.Headers.Authorization.Count > 0;
            if (bearer != (Scheme.Name == AdministrativeAuthorizationBoundary.BearerScheme)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = Request.Headers["X-Fixture-Permissions"].ToString().Split(',')
                .Select(x => new Claim("permission", x)).Append(new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
