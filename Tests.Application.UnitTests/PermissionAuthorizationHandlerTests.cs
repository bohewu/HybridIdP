using Core.Domain;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Moq;
using System.Security.Claims;

namespace Tests.Application.UnitTests;

public class PermissionAuthorizationHandlerTests
{
    [Theory]
    [InlineData("scope", "users.read")]
    [InlineData("role", "Admin")]
    [InlineData("permission", "users.read")]
    [InlineData("active_role", "Admin")]
    public async Task PermissionHandler_ShouldRejectUnapprovedBearerAuthority(string claimType, string value)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, value)], "Bearer"));
        var requirement = new PermissionRequirement("users.read");
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new PermissionAuthorizationHandler(CreateRoleManagerMock().Object, CreateCookieBoundary(principal)).HandleAsync(context);
        Assert.False(context.HasSucceeded);
    }

    private static IAdministrativeAuthorizationBoundary CreateCookieBoundary(ClaimsPrincipal principal)
    {
        var boundary = new Mock<IAdministrativeAuthorizationBoundary>();
        var cookie = AuthorizationRoleClaimResolver.GetApplicationPrincipal(principal);
        boundary.Setup(b => b.ResolveAsync()).ReturnsAsync(cookie.Identity?.IsAuthenticated == true
            ? new AdministrativeAuthority(cookie, false, new HashSet<string>()) : null);
        return boundary.Object;
    }

    private static Mock<RoleManager<ApplicationRole>> CreateRoleManagerMock()
    {
        var store = new Mock<IRoleStore<ApplicationRole>>();
        return new Mock<RoleManager<ApplicationRole>>(store.Object, null, null, null, null);
    }

    [Fact]
    public async Task PermissionHandler_NoActiveRole_ShouldCheckAllRoleClaims()
    {
        // Arrange
        var roleManager = CreateRoleManagerMock();
        roleManager.Setup(m => m.FindByNameAsync("User"))
            .ReturnsAsync(new ApplicationRole { Name = "User", Permissions = "users.read" });
        roleManager.Setup(m => m.FindByNameAsync("ApplicationManager"))
            .ReturnsAsync(new ApplicationRole { Name = "ApplicationManager", Permissions = "scopes.update" });

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "User"),
            new Claim(ClaimTypes.Role, "ApplicationManager")
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task PermissionHandler_WithActiveRole_ShouldUseActiveRoleOnly()
    {
        // Arrange
        var roleManager = CreateRoleManagerMock();
        roleManager.Setup(m => m.FindByNameAsync("User"))
            .ReturnsAsync(new ApplicationRole { Name = "User", Permissions = "users.read" });
        roleManager.Setup(m => m.FindByNameAsync("ApplicationManager"))
            .ReturnsAsync(new ApplicationRole { Name = "ApplicationManager", Permissions = "scopes.update" });

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("active_role", "User"),
            new Claim(ClaimTypes.Role, "User"),
            new Claim(ClaimTypes.Role, "ApplicationManager")
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task PermissionHandler_ShouldHonorPermissionClaim()
    {
        // Arrange
        var roleManager = CreateRoleManagerMock();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("permission", Permissions.Scopes.Update),
            new Claim("app_role", AuthConstants.Roles.Admin),
            new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin)
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task PermissionHandler_AppRoleAdmin_ShouldNotGrantGlobalPermission()
    {
        // Arrange
        var roleManager = CreateRoleManagerMock();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("app_role", AuthConstants.Roles.Admin.ToLowerInvariant()),
            new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin)
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
        roleManager.Verify(m => m.FindByNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PermissionHandler_GlobalAndAppRoleAdmin_ShouldPreserveGlobalPermission()
    {
        var roleManager = CreateRoleManagerMock();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("app_role", AuthConstants.Roles.Admin),
            new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin),
            new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin)
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleManager.Verify(m => m.FindByNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HasAnyPermissionHandler_NoActiveRole_ShouldCheckAllRoleClaims()
    {
        // Arrange
        var roleManager = CreateRoleManagerMock();
        roleManager.Setup(m => m.FindByNameAsync("User"))
            .ReturnsAsync(new ApplicationRole { Name = "User", Permissions = "users.read" });
        roleManager.Setup(m => m.FindByNameAsync("ApplicationManager"))
            .ReturnsAsync(new ApplicationRole { Name = "ApplicationManager", Permissions = "scopes.update" });

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "User"),
            new Claim(ClaimTypes.Role, "ApplicationManager")
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new HasAnyPermissionRequirement(Permissions.Clients.Update, Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new HasAnyPermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HasAnyPermissionHandler_WithActiveRole_ShouldUseActiveRoleOnly()
    {
        // Arrange
        var roleManager = CreateRoleManagerMock();
        roleManager.Setup(m => m.FindByNameAsync("User"))
            .ReturnsAsync(new ApplicationRole { Name = "User", Permissions = "users.read" });
        roleManager.Setup(m => m.FindByNameAsync("ApplicationManager"))
            .ReturnsAsync(new ApplicationRole { Name = "ApplicationManager", Permissions = "scopes.update" });

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("active_role", "User"),
            new Claim(ClaimTypes.Role, "User"),
            new Claim(ClaimTypes.Role, "ApplicationManager")
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new HasAnyPermissionRequirement(Permissions.Clients.Update, Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new HasAnyPermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HasAnyPermissionHandler_AppRoleAdmin_ShouldNotGrantGlobalPermission()
    {
        // Arrange
        var roleManager = CreateRoleManagerMock();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("app_role", AuthConstants.Roles.Admin),
            new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin)
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new HasAnyPermissionRequirement(Permissions.Clients.Update, Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new HasAnyPermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
        roleManager.Verify(m => m.FindByNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HasAnyPermissionHandler_GlobalAndAppRoleAdmin_ShouldPreserveGlobalPermission()
    {
        var roleManager = CreateRoleManagerMock();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("app_role", AuthConstants.Roles.Admin),
            new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin),
            new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin)
        }, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));

        var requirement = new HasAnyPermissionRequirement(
            Permissions.Clients.Update,
            Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new HasAnyPermissionAuthorizationHandler(roleManager.Object, CreateCookieBoundary(principal));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleManager.Verify(m => m.FindByNameAsync(It.IsAny<string>()), Times.Never);
    }
}
