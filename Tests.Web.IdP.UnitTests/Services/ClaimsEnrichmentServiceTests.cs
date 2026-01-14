using System.Security.Claims;
using Core.Domain; // Added for ApplicationRole and ApplicationUser
using Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP.Services;
using Xunit;
using Core.Application;

namespace Tests.Web.IdP.UnitTests.Services;

public class ClaimsEnrichmentServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
    private readonly Mock<IApplicationDbContext> _mockDbContext;
    private readonly Mock<ILogger<ClaimsEnrichmentService>> _mockLogger;
    private readonly ClaimsEnrichmentService _service;

    public ClaimsEnrichmentServiceTests()
    {
        // Mock UserManager
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null, null, null, null, null, null, null, null);

        // Mock RoleManager
        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        var roleValidators = new List<IRoleValidator<ApplicationRole>>();
        _mockRoleManager = new Mock<RoleManager<ApplicationRole>>(
            roleStore.Object, roleValidators, new Mock<ILookupNormalizer>().Object, 
            new IdentityErrorDescriber(), new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);

        _mockDbContext = new Mock<IApplicationDbContext>();
        _mockLogger = new Mock<ILogger<ClaimsEnrichmentService>>();

        _service = new ClaimsEnrichmentService(
            _mockUserManager.Object,
            _mockRoleManager.Object,
            _mockDbContext.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task AddPermissionClaimsAsync_WithPrivilegedClient_AddsPermissions()
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin" };
        var identity = new ClaimsIdentity();
        var clientId = "testclient-admin"; // Privileged

        _mockUserManager.Setup(u => u.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin" });

        _mockRoleManager.Setup(r => r.FindByNameAsync("Admin"))
            .ReturnsAsync(new ApplicationRole { Name = "Admin", Permissions = "users.read,roles.manage" });

        // Act
        await _service.AddPermissionClaimsAsync(identity, user, clientId);

        // Assert
        Assert.True(identity.HasClaim(c => c.Type == "permission" && c.Value == "users.read"));
        Assert.True(identity.HasClaim(c => c.Type == "permission" && c.Value == "roles.manage"));
    }

    [Fact]
    public async Task AddPermissionClaimsAsync_WithPublicClient_DoesNotAddPermissions()
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin" };
        var identity = new ClaimsIdentity();
        var clientId = "testclient-public"; // Not Privileged

        // Setup roles just in case logic is wrong and it tries to fetch them (it shouldn't if it returns early)
        _mockUserManager.Setup(u => u.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin" });
        _mockRoleManager.Setup(r => r.FindByNameAsync("Admin"))
            .ReturnsAsync(new ApplicationRole { Name = "Admin", Permissions = "users.read" });

        // Act
        await _service.AddPermissionClaimsAsync(identity, user, clientId);

        // Assert
        Assert.False(identity.HasClaim(c => c.Type == "permission"), "Should not have permission claims");
    }

    [Fact]
    public async Task AddPermissionClaimsAsync_WithNullClient_AddsPermissions()
    {
        // Arrange
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "admin" };
        var identity = new ClaimsIdentity();
        string? clientId = null; // Internal / Legacy

        _mockUserManager.Setup(u => u.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin" });

        _mockRoleManager.Setup(r => r.FindByNameAsync("Admin"))
            .ReturnsAsync(new ApplicationRole { Name = "Admin", Permissions = "users.read" });

        // Act
        await _service.AddPermissionClaimsAsync(identity, user, clientId);

        // Assert
        Assert.True(identity.HasClaim(c => c.Type == "permission" && c.Value == "users.read"));
    }
}
