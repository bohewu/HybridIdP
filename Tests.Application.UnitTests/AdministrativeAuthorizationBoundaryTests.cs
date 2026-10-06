using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Core.Domain;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenIddict.Abstractions;

namespace Tests.Application.UnitTests;

public class AdministrativeAuthorizationBoundaryTests
{
    [Theory]
    [InlineData("ordinary")]
    [InlineData("name-only")]
    [InlineData("recreated")]
    [InlineData("wrong-presenter")]
    [InlineData("user-token")]
    [InlineData("removed-approval")]
    public async Task ResolveAsync_ShouldDenyUnapprovedBearerEvenWithAdminCookieAndClaims(string scenario)
    {
        var fixture = CreateFixture();
        fixture.Bearer.SetClaim("role", "Admin").SetClaim("active_role", "Admin")
            .SetClaim("permission", "users.read");
        if (scenario is "ordinary" or "name-only") fixture.Bearer.SetClaim(AdministrativeClientGrant.ApplicationClaim, (string?)null);
        if (scenario == "recreated") fixture.Applications.Setup(m => m.FindByIdAsync("record-1", It.IsAny<CancellationToken>())).ReturnsAsync((object?)null);
        if (scenario == "wrong-presenter") fixture.Bearer.SetPresenters("testclient-admin-recreated");
        if (scenario == "user-token") fixture.Bearer.SetClaim("sub", Guid.NewGuid().ToString());
        if (scenario == "removed-approval") fixture.Applications.Setup(m => m.GetPropertiesAsync(fixture.Application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableDictionary<string, JsonElement>.Empty);
        Assert.Null(await fixture.Boundary.ResolveAsync());
        var requirement = new PermissionRequirement("users.read");
        var mixed = new ClaimsPrincipal(fixture.Bearer.Identities.Concat(fixture.Cookie.Identities));
        var context = new AuthorizationHandlerContext([requirement], mixed, fixture.HttpContext);
        await new PermissionAuthorizationHandler(CreateRoleManager(), fixture.Boundary).HandleAsync(context);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handlers_ShouldHonorApprovedScopeCeilingWithoutUsingBearerRoles()
    {
        var fixture = CreateFixture();
        fixture.Bearer.SetClaim("role", "Admin").SetScopes("users.read", "users.delete");
        var read = new PermissionRequirement("users.read");
        var readContext = new AuthorizationHandlerContext([read], fixture.Bearer, fixture.HttpContext);
        await new PermissionAuthorizationHandler(CreateRoleManager(), fixture.Boundary).HandleAsync(readContext);
        Assert.True(readContext.HasSucceeded);
        var write = new HasAnyPermissionRequirement(["users.delete"]);
        var writeContext = new AuthorizationHandlerContext([write], fixture.Bearer, fixture.HttpContext);
        await new HasAnyPermissionAuthorizationHandler(CreateRoleManager(), fixture.Boundary).HandleAsync(writeContext);
        Assert.False(writeContext.HasSucceeded);
        var readAny = new HasAnyPermissionRequirement(["users.read", "users.delete"]);
        var anyContext = new AuthorizationHandlerContext([readAny], fixture.Bearer, fixture.HttpContext);
        await new HasAnyPermissionAuthorizationHandler(CreateRoleManager(), fixture.Boundary).HandleAsync(anyContext);
        Assert.True(anyContext.HasSucceeded);
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseOnlyAuthenticatedApplicationCookieForInteractiveAdministration()
    {
        var fixture = CreateFixture();
        fixture.Authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), AdministrativeAuthorizationBoundary.BearerScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());
        var requirement = new PermissionRequirement("users.read");
        var context = new AuthorizationHandlerContext([requirement], fixture.Bearer, fixture.HttpContext);
        await new PermissionAuthorizationHandler(CreateRoleManager(), fixture.Boundary).HandleAsync(context);
        Assert.True(context.HasSucceeded);
        fixture.HttpContext.Request.Headers.Authorization = "Bearer invalid";
        Assert.Null(await fixture.Boundary.ResolveAsync());
    }

    private static RoleManager<ApplicationRole> CreateRoleManager() => new Mock<RoleManager<ApplicationRole>>(
        Mock.Of<IRoleStore<ApplicationRole>>(), null, null, null, null).Object;

    private static Fixture CreateFixture()
    {
        var bearer = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"));
        bearer.SetClaim("sub", "testclient-admin").SetClaim(AdministrativeClientGrant.ApplicationClaim, "record-1")
            .SetPresenters("testclient-admin").SetScopes("users.read");
        var cookie = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], IdentityConstants.ApplicationScheme));
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), AdministrativeAuthorizationBoundary.BearerScheme))
            .ReturnsAsync(() => AuthenticateResult.Success(new AuthenticationTicket(bearer, AdministrativeAuthorizationBoundary.BearerScheme)));
        authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(cookie, IdentityConstants.ApplicationScheme)));
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider()
        };
        var application = new object();
        var applications = new Mock<IOpenIddictApplicationManager>();
        applications.Setup(m => m.FindByIdAsync("record-1", It.IsAny<CancellationToken>())).ReturnsAsync(application);
        applications.Setup(m => m.GetClientIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync("testclient-admin");
        applications.Setup(m => m.GetPropertiesAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(
            ImmutableDictionary<string, JsonElement>.Empty.Add(AdministrativeClientGrant.PermissionsProperty,
                JsonSerializer.SerializeToElement(new[] { "users.read" })));
        return new(new AdministrativeAuthorizationBoundary(new HttpContextAccessor { HttpContext = httpContext }, applications.Object),
            httpContext, bearer, cookie, applications, authentication, application);
    }

    private sealed record Fixture(AdministrativeAuthorizationBoundary Boundary, HttpContext HttpContext,
        ClaimsPrincipal Bearer, ClaimsPrincipal Cookie, Mock<IOpenIddictApplicationManager> Applications,
        Mock<IAuthenticationService> Authentication, object Application);
}
