using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Core.Application;
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

public sealed class NativeFieldPoliciesControllerTests
{
    [Theory]
    [InlineData(null, false, false, HttpStatusCode.Unauthorized)]
    [InlineData("settings.update", false, true, HttpStatusCode.Forbidden)]
    [InlineData("users.read", false, true, HttpStatusCode.Forbidden)]
    [InlineData("users.update", true, true, HttpStatusCode.Forbidden)]
    [InlineData("users.update", false, false, HttpStatusCode.BadRequest)]
    [InlineData("users.update", false, true, HttpStatusCode.OK)]
    public async Task Mutation_ShouldEnforceAdministrativeAuthorityAndCsrfAndAuditMetadataOnly(
        string? permission, bool bearer, bool csrf, HttpStatusCode expected)
    {
        var policies = new Mock<INativeFieldPolicyService>(MockBehavior.Strict);
        var audit = new Mock<IAuditService>(MockBehavior.Strict);
        var id = Guid.NewGuid();
        policies.Setup(x => x.UpdateAsync(NativeFieldTarget.User, id, NativeField.Nickname, It.IsAny<NativeFieldUpdateRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        policies.Setup(x => x.GetTargetAsync(NativeFieldTarget.User, id, It.IsAny<CancellationToken>())).ReturnsAsync(new NativeFieldTargetDto(NativeFieldTarget.User, id, Guid.NewGuid(), [], []));
        string? details = null;
        audit.Setup(x => x.LogAdministrativeEventAsync("NativeFieldPolicyChanged", "User", id.ToString(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, CancellationToken>((_, _, _, text, _) => details = text).Returns(Task.CompletedTask);
        await using var app = CreateApp(policies.Object, audit.Object);
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            if (permission is not null) client.DefaultRequestHeaders.Add("X-Fixture-Permission", permission);
            if (bearer) client.DefaultRequestHeaders.Add("Authorization", "Bearer fixture-only");
            if (csrf) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await client.GetStringAsync("/fixture-antiforgery"));
            using var response = await client.PutAsJsonAsync($"/api/admin/native-field-policies/users/{id}/Nickname", new NativeFieldUpdateRequest
            {
                Operation = NativeFieldOperation.Manual, ManualValue = "never-audit-native-value",
                SourceIdentity = new(NativeFieldSource.Proof, id, "example.provider", "never-audit-subject")
            });
            Assert.True(expected == response.StatusCode, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            if (expected == HttpStatusCode.OK)
            {
                Assert.Contains("Nickname", details);
                Assert.Contains("Manual", details);
                Assert.DoesNotContain("never-audit", details);
                policies.Verify(x => x.UpdateAsync(NativeFieldTarget.User, id, NativeField.Nickname, It.IsAny<NativeFieldUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            else
            {
                Assert.Null(details);
                policies.Verify(x => x.UpdateAsync(It.IsAny<NativeFieldTarget>(), It.IsAny<Guid>(), It.IsAny<NativeField>(), It.IsAny<NativeFieldUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData(nameof(NativeFieldPoliciesController.GetSources), Permissions.Settings.Read)]
    [InlineData(nameof(NativeFieldPoliciesController.SetPaused), Permissions.Settings.Update)]
    [InlineData(nameof(NativeFieldPoliciesController.GetUser), Permissions.Users.Read)]
    [InlineData(nameof(NativeFieldPoliciesController.GetPerson), Permissions.Persons.Read)]
    [InlineData(nameof(NativeFieldPoliciesController.UpdateUser), Permissions.Users.Update)]
    [InlineData(nameof(NativeFieldPoliciesController.UpdatePerson), Permissions.Persons.Update)]
    public void Endpoints_ShouldUseTargetSpecificLeastPrivilege(string action, string permission)
    {
        var attributes = typeof(NativeFieldPoliciesController).GetMethod(action)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>();
        Assert.Equal(permission, Assert.Single(attributes).Policy);
    }

    private static WebApplication CreateApp(INativeFieldPolicyService policies, IAuditService audit)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "127.0.0.1" });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(NativeFieldPoliciesController).Assembly);
        builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(IdentityConstants.ApplicationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(AdministrativeAuthorizationBoundary.BearerScheme, _ => { });
        builder.Services.AddAuthorization(o =>
        {
            foreach (var permission in new[] { Permissions.Settings.Read, Permissions.Settings.Update, Permissions.Users.Read, Permissions.Users.Update, Permissions.Persons.Read, Permissions.Persons.Update })
                o.AddPolicy(permission, p => p.AddRequirements(new PermissionRequirement(permission)));
        });
        builder.Services.AddScoped<IAdministrativeAuthorizationBoundary, AdministrativeAuthorizationBoundary>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton(new Mock<RoleManager<ApplicationRole>>(Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null).Object);
        builder.Services.AddSingleton(Mock.Of<IOpenIddictApplicationManager>());
        builder.Services.AddSingleton(policies);
        builder.Services.AddSingleton(audit);
        var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/fixture-antiforgery", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
        app.MapControllers();
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
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
                new Claim("permission", Request.Headers["X-Fixture-Permission"].ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
