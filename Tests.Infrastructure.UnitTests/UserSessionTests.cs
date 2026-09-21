using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// Unit tests for UserSession entity with ActiveRole support (Phase 11.1)
/// ActiveRoleId is optional until a role is explicitly resolved for the session.
/// </summary>
public class UserSessionTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public UserSessionTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    public void Dispose()
    {
        using var context = new ApplicationDbContext(_options);
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task CreateSession_WithActiveRole_ShouldStoreActiveRoleId()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        
        var role = new ApplicationRole 
        { 
            Id = roleId, 
            Name = "Developer",
            NormalizedName = "DEVELOPER"
        };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        
        var session = new UserSession
        {
            UserId = userId,
            AuthorizationId = "auth_123",
            ActiveRoleId = roleId,
            LastRoleSwitchUtc = DateTime.UtcNow
        };
        
        // Act
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        
        // Assert
        var retrieved = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == session.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(roleId, retrieved.ActiveRoleId);
        Assert.NotNull(retrieved.LastRoleSwitchUtc);
    }

    [Fact]
    public async Task CreateSession_WithoutActiveRole_ShouldStoreNull()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var userId = Guid.NewGuid();
        
        var session = new UserSession
        {
            UserId = userId,
            AuthorizationId = "auth_456",
            LastRoleSwitchUtc = null
        };
        
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        
        var retrieved = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == session.Id);
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.ActiveRoleId);
        Assert.True(context.Model.FindEntityType(typeof(UserSession))!
            .FindProperty(nameof(UserSession.ActiveRoleId))!.IsNullable);
    }

    [Fact]
    public async Task UpdateSession_ShouldUpdateActiveRole()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var userId = Guid.NewGuid();
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        
        var role1 = new ApplicationRole { Id = roleId1, Name = "Developer", NormalizedName = "DEVELOPER" };
        var role2 = new ApplicationRole { Id = roleId2, Name = "Manager", NormalizedName = "MANAGER" };
        context.Roles.AddRange(role1, role2);
        await context.SaveChangesAsync();
        
        var session = new UserSession
        {
            UserId = userId,
            AuthorizationId = "auth_789",
            ActiveRoleId = roleId1,
            LastRoleSwitchUtc = DateTime.UtcNow.AddHours(-1)
        };
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        
        // Act
        var updateTime = DateTime.UtcNow;
        session.ActiveRoleId = roleId2;
        session.LastRoleSwitchUtc = updateTime;
        await context.SaveChangesAsync();
        
        // Assert
        var retrieved = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == session.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(roleId2, retrieved.ActiveRoleId);
        Assert.NotNull(retrieved.LastRoleSwitchUtc);
        Assert.True((updateTime - retrieved.LastRoleSwitchUtc!.Value).TotalSeconds < 1);
    }

    [Fact]
    public async Task SwitchBackToOriginalRole_ShouldUpdateSuccessfully()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var userId = Guid.NewGuid();
        var developerRoleId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();
        
        var devRole = new ApplicationRole { Id = developerRoleId, Name = "Developer", NormalizedName = "DEVELOPER" };
        var adminRole = new ApplicationRole { Id = adminRoleId, Name = "Admin", NormalizedName = "ADMIN" };
        context.Roles.AddRange(devRole, adminRole);
        await context.SaveChangesAsync();
        
        var session = new UserSession
        {
            UserId = userId,
            AuthorizationId = "auth_switch",
            ActiveRoleId = developerRoleId,
            LastRoleSwitchUtc = DateTime.UtcNow.AddHours(-2)
        };
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        
        // Act - Switch to Admin
        session.ActiveRoleId = adminRoleId;
        session.LastRoleSwitchUtc = DateTime.UtcNow.AddHours(-1);
        await context.SaveChangesAsync();
        
        // Act - Switch back to Developer
        var switchBackTime = DateTime.UtcNow;
        session.ActiveRoleId = developerRoleId;
        session.LastRoleSwitchUtc = switchBackTime;
        await context.SaveChangesAsync();
        
        // Assert
        var retrieved = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == session.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(developerRoleId, retrieved.ActiveRoleId);
        Assert.True((switchBackTime - retrieved.LastRoleSwitchUtc!.Value).TotalSeconds < 1);
    }

    [Fact]
    public async Task QueryWithInclude_ShouldLoadActiveRoleNavigation()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        
        var role = new ApplicationRole 
        { 
            Id = roleId, 
            Name = "Admin",
            NormalizedName = "ADMIN",
            Permissions = "users.read,users.write"
        };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        
        var session = new UserSession
        {
            UserId = userId,
            AuthorizationId = "auth_include",
            ActiveRoleId = roleId
        };
        context.UserSessions.Add(session);
        await context.SaveChangesAsync();
        
        // Act
        var retrieved = await context.UserSessions
            .Include(s => s.ActiveRole)
            .FirstOrDefaultAsync(s => s.Id == session.Id);
        
        // Assert
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.ActiveRole);
        Assert.Equal("Admin", retrieved.ActiveRole.Name);
        Assert.Equal("users.read,users.write", retrieved.ActiveRole.Permissions);
    }
}
