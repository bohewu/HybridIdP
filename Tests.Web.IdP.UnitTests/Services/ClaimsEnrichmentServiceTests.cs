using System.Security.Claims;
using Core.Domain; // Added for ApplicationRole and ApplicationUser
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP.Services;
using Xunit;
using Core.Application;
using Infrastructure;
using IdentityModel;
using Microsoft.EntityFrameworkCore;

using UserAppRoleEntity = Core.Domain.Entities.UserAppRole;

namespace Tests.Web.IdP.UnitTests.Services;

public class ClaimsEnrichmentServiceTests
{
    [Theory]
    [InlineData(null, "\u6e2c\u8a66\u59d3\u540d", "\u6e2c\u8a66\u59d3\u540d")]
    [InlineData("Given", "Source full name", "Given")]
    [InlineData(null, "", "historical@example.org")]
    public async Task AddScopeMappedClaimsAsync_ShouldUseNativeNameAndExactPersonOrganizationId(
        string? firstName, string nickname, string expected)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new ApplicationUser { UserName = "historical@example.org", FirstName = firstName, Nickname = nickname,
            Person = new Person { Id = Guid.NewGuid(), EmployeeId = "ORG-104" } };
        foreach (var (claim, property) in new[] { ("name", "FirstName"), ("employee_id", "Person.EmployeeId") })
            db.ScopeClaims.Add(new ScopeClaim { ScopeId = "profile", ScopeName = "profile",
                ClaimDefinition = new ClaimDefinition { Name = claim, DisplayName = claim, ClaimType = claim,
                    DataType = "String", UserPropertyPath = property } });
        await db.SaveChangesAsync();
        var service = new ClaimsEnrichmentService(_mockUserManager.Object, _mockRoleManager.Object, db, _mockLogger.Object);
        var identity = new ClaimsIdentity();

        await service.AddScopeMappedClaimsAsync(identity, user, ["profile"]);

        Assert.Equal(expected, identity.FindFirst("name")?.Value);
        Assert.Equal("ORG-104", identity.FindFirst("employee_id")?.Value);
    }

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
    public async Task AddPermissionClaimsAsync_WithPrivilegedLookingClient_DoesNotAddIdpPermissions()
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
        Assert.False(identity.HasClaim(c => c.Type == "permission" && c.Value == "users.read"));
        Assert.False(identity.HasClaim(c => c.Type == "permission" && c.Value == "roles.manage"));
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
    public async Task AddPermissionClaimsAsync_WithNullClient_DoesNotAddIdpPermissions()
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
        Assert.False(identity.HasClaim(c => c.Type == "permission" && c.Value == "users.read"));
    }

    [Fact]
    public async Task AddAppSpecificRolesAsync_PreservesRoleCompatibility_WithoutGrantingGlobalPermission()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new ApplicationDbContext(options);
        
        // Seed data
        var userId = Guid.NewGuid();
        var clientAppId = "client-app";
        var user = new ApplicationUser { Id = userId, UserName = "user" };
        var role = new UserAppRoleEntity 
        { 
            UserId = userId, 
            ClientId = clientAppId, 
            RoleName = "Admin",
            CreatedAt = DateTime.UtcNow
        };
        context.UserAppRoles.Add(role);
        await context.SaveChangesAsync();

        // Create service with real context
        var service = new ClaimsEnrichmentService(
            _mockUserManager.Object,
            _mockRoleManager.Object,
            context, // Real context
            _mockLogger.Object);

        var identity = new ClaimsIdentity(
            authenticationType: "Test",
            nameType: JwtClaimTypes.Name,
            roleType: JwtClaimTypes.Role);

        // Act
        await service.AddAppSpecificRolesAsync(identity, user, clientAppId);

        // Assert
        Assert.True(identity.HasClaim(c => c.Type == "app_role" && c.Value == "Admin"));
        Assert.True(identity.HasClaim(c => c.Type == JwtClaimTypes.Role && c.Value == "Admin"));
        var principal = new ClaimsPrincipal(identity);
        Assert.True(principal.IsInRole("Admin"));

        var requirement = new PermissionRequirement(Permissions.Users.Read);
        var authorizationContext = new AuthorizationHandlerContext([requirement], principal, null);
        var boundary = new Mock<IAdministrativeAuthorizationBoundary>();
        boundary.Setup(b => b.ResolveAsync()).ReturnsAsync((AdministrativeAuthority?)null);
        var handler = new PermissionAuthorizationHandler(_mockRoleManager.Object, boundary.Object);

        await handler.HandleAsync(authorizationContext);

        Assert.False(authorizationContext.HasSucceeded);
    }

    [Fact]
    public async Task AddAppSpecificRolesAsync_DoesNotAddClaims_ForDifferentClient()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new ApplicationDbContext(options);
        
        // Seed data
        var userId = Guid.NewGuid();
        var clientAppId = "client-app";
        var otherClientId = "other-client";
        var user = new ApplicationUser { Id = userId, UserName = "user" };
        var role = new UserAppRoleEntity 
        { 
            UserId = userId, 
            ClientId = otherClientId, // Role for different client
            RoleName = "AppAdmin", 
            CreatedAt = DateTime.UtcNow
        };
        context.UserAppRoles.Add(role);
        await context.SaveChangesAsync();

        // Create service with real context
        var service = new ClaimsEnrichmentService(
            _mockUserManager.Object,
            _mockRoleManager.Object,
            context, // Real context
            _mockLogger.Object);

        var identity = new ClaimsIdentity();

        // Act
        await service.AddAppSpecificRolesAsync(identity, user, clientAppId);

        // Assert
        Assert.False(identity.HasClaim(c => c.Type == "app_role"));
    }
}
