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
        }, "Test"));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object);

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
        }, "Test"));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object);

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
            new Claim("permission", Permissions.Scopes.Update)
        }, "Test"));

        var requirement = new PermissionRequirement(Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(roleManager.Object);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
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
        }, "Test"));

        var requirement = new HasAnyPermissionRequirement(Permissions.Clients.Update, Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new HasAnyPermissionAuthorizationHandler(roleManager.Object);

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
        }, "Test"));

        var requirement = new HasAnyPermissionRequirement(Permissions.Clients.Update, Permissions.Scopes.Update);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new HasAnyPermissionAuthorizationHandler(roleManager.Object);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }
}
