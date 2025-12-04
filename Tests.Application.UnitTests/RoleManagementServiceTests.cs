using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Events;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Application.UnitTests;

public class RoleManagementServiceTests
{
    private static RoleManagementService CreateService(
        IEnumerable<ApplicationRole> roles,
        out Mock<RoleManager<ApplicationRole>> roleManagerMock,
        out Mock<UserManager<ApplicationUser>> userManagerMock,
        out Mock<IDomainEventPublisher> eventPublisherMock)
    {
        // Mock RoleStore and support IQueryable
        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        var queryableRoleStore = roleStore.As<IQueryableRoleStore<ApplicationRole>>();
        // Provide simple in-memory queryable; service falls back to sync path in tests
        queryableRoleStore.Setup(s => s.Roles).Returns(roles.AsQueryable());

        roleManagerMock = new Mock<RoleManager<ApplicationRole>>(
            queryableRoleStore.Object,
            new IRoleValidator<ApplicationRole>[0],
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);
        // Ensure Roles returns our in-memory list
        roleManagerMock.Setup(x => x.Roles).Returns(roles.AsQueryable());

        // Mock UserManager
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            Options.Create(new IdentityOptions()),
            new Mock<IPasswordHasher<ApplicationUser>>().Object,
            new IUserValidator<ApplicationUser>[0],
            new IPasswordValidator<ApplicationUser>[0],
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<ApplicationUser>>>().Object);

        // Default: no users in role
        userManagerMock
            .Setup(x => x.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<ApplicationUser>());

        // Mock event publisher
        eventPublisherMock = new Mock<IDomainEventPublisher>();

        return new RoleManagementService(roleManagerMock.Object, userManagerMock.Object, eventPublisherMock.Object);
    }

    [Fact]
    public async Task GetRoleById_NotFound_ReturnsNull()
    {
        // Arrange
        var service = CreateService(Array.Empty<ApplicationRole>(), out var roleMgr, out _, out _);
        var missingId = Guid.NewGuid();
        roleMgr.Setup(x => x.FindByIdAsync(missingId.ToString())).ReturnsAsync((ApplicationRole?)null);

        // Act
        var result = await service.GetRoleByIdAsync(missingId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRoleById_ReturnsDetail_WithUsersAndPermissions()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = new ApplicationRole
        {
            Id = roleId,
            Name = "Editors",
            NormalizedName = "EDITORS",
            Description = "Content editors",
            Permissions = "users.read,roles.read",
            IsSystem = false,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        var service = CreateService(new[] { role }, out var roleMgr, out var userMgr, out _);

        roleMgr.Setup(x => x.FindByIdAsync(roleId.ToString())).ReturnsAsync(role);
        userMgr.Setup(x => x.GetUsersInRoleAsync("Editors")).ReturnsAsync(new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "alice@example.com", UserName = "alice" },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "bob@example.com", UserName = "bob" }
        });

        // Act
        var detail = await service.GetRoleByIdAsync(roleId);

        // Assert
        Assert.NotNull(detail);
        Assert.Equal(roleId, detail!.Id);
        Assert.Equal("Editors", detail.Name);
        Assert.Equal("EDITORS", detail.NormalizedName);
        Assert.Equal("Content editors", detail.Description);
        Assert.Contains("users.read", detail.Permissions);
        Assert.Contains("roles.read", detail.Permissions);
        Assert.Equal(2, detail.UserCount);
        Assert.Equal(2, detail.Users.Count);
        Assert.All(detail.Users, u => Assert.Contains("Editors", u.Roles));
    }

    [Fact]
    public async Task CreateRole_DuplicateName_ShouldFail()
    {
        // Arrange
        var existing = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admins" };
        var service = CreateService(new[] { existing }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByNameAsync("Admins")).ReturnsAsync(existing);

        var dto = new CreateRoleDto
        {
            Name = "Admins",
            Description = "Duplicate",
            Permissions = new List<string> { Core.Domain.Constants.Permissions.Users.Read }
        };

        // Act
        var (success, _, errors) = await service.CreateRoleAsync(dto);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateRole_InvalidPermission_ShouldFail()
    {
        // Arrange
        var service = CreateService(Array.Empty<ApplicationRole>(), out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((ApplicationRole?)null);
        var dto = new CreateRoleDto
        {
            Name = "ContentEditor",
            Description = "",
            Permissions = new List<string> { "users.read", "invalid.permission" }
        };

        // Act
        var (success, _, errors) = await service.CreateRoleAsync(dto);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Invalid permissions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateRole_Valid_ShouldSucceed()
    {
        // Arrange
        var service = CreateService(Array.Empty<ApplicationRole>(), out var roleMgr, out _, out var eventPublisher);
        roleMgr.Setup(x => x.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((ApplicationRole?)null);
        roleMgr.Setup(x => x.CreateAsync(It.IsAny<ApplicationRole>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<ApplicationRole>(r => {
                r.Id = Guid.NewGuid();
                r.Name = "ContentEditor"; // Ensure name is set for event
            });

        var dto = new CreateRoleDto
        {
            Name = "ContentEditor",
            Description = "",
            Permissions = new List<string>
            {
                Core.Domain.Constants.Permissions.Users.Read,
                Core.Domain.Constants.Permissions.Roles.Read
            }
        };

        // Act
        var (success, roleId, errors) = await service.CreateRoleAsync(dto);

        // Assert
        Assert.True(success);
        Assert.NotNull(roleId);
        Assert.Empty(errors);
        eventPublisher.Verify(x => x.PublishAsync(It.Is<RoleCreatedEvent>(e => e.RoleName == "ContentEditor")), Times.Once);
    }

    [Fact]
    public async Task UpdateRole_RenameToExisting_ShouldFail()
    {
        // Arrange
        var existing = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admins" };
        var target = new ApplicationRole { Id = Guid.NewGuid(), Name = "Editors" };
        var service = CreateService(new[] { existing, target }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);
        roleMgr.Setup(x => x.FindByNameAsync("Admins")).ReturnsAsync(existing);

        var dto = new UpdateRoleDto
        {
            Name = "Admins",
            Description = target.Description ?? "",
            Permissions = new List<string> { Core.Domain.Constants.Permissions.Users.Read }
        };

        // Act
        var (success, errors) = await service.UpdateRoleAsync(target.Id, dto);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateRole_SystemRoleRename_ShouldFail()
    {
        // Arrange
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admin", IsSystem = true };
        var service = CreateService(new[] { systemRole }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByIdAsync(systemRole.Id.ToString())).ReturnsAsync(systemRole);

        var dto = new UpdateRoleDto
        {
            Name = "SuperAdmin",
            Description = systemRole.Description ?? "",
            Permissions = new List<string> { Core.Domain.Constants.Permissions.Users.Read }
        };

        // Act
        var (success, errors) = await service.UpdateRoleAsync(systemRole.Id, dto);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Cannot rename system roles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateRole_InvalidPermissions_ShouldFail()
    {
        // Arrange
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = "Editors" };
        var service = CreateService(new[] { role }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByIdAsync(role.Id.ToString())).ReturnsAsync(role);

        var dto = new UpdateRoleDto
        {
            Name = "Editors",
            Description = role.Description ?? "",
            Permissions = new List<string> { "invalid.permission" }
        };

        // Act
        var (success, errors) = await service.UpdateRoleAsync(role.Id, dto);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Invalid permissions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteRole_SystemRole_ShouldFail()
    {
        // Arrange
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admin", IsSystem = true };
        var service = CreateService(new[] { systemRole }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByIdAsync(systemRole.Id.ToString())).ReturnsAsync(systemRole);

        // Act
        var (success, errors) = await service.DeleteRoleAsync(systemRole.Id);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Cannot delete system roles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteRole_WithUsersAssigned_ShouldFail()
    {
        // Arrange
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = "Editors" };
        var service = CreateService(new[] { role }, out var roleMgr, out var userMgr, out _);
        roleMgr.Setup(x => x.FindByIdAsync(role.Id.ToString())).ReturnsAsync(role);
        userMgr.Setup(x => x.GetUsersInRoleAsync("Editors")).ReturnsAsync(new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "alice@example.com" }
        });

        // Act
        var (success, errors) = await service.DeleteRoleAsync(role.Id);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Remove users from role first", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteRole_Valid_ShouldSucceed()
    {
        // Arrange
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = "Editors" };
        var service = CreateService(new[] { role }, out var roleMgr, out var userMgr, out var eventPublisher);
        roleMgr.Setup(x => x.FindByIdAsync(role.Id.ToString())).ReturnsAsync(role);
        userMgr.Setup(x => x.GetUsersInRoleAsync("Editors")).ReturnsAsync(new List<ApplicationUser>());
        roleMgr.Setup(x => x.DeleteAsync(role)).ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await service.DeleteRoleAsync(role.Id);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        eventPublisher.Verify(x => x.PublishAsync(It.Is<RoleDeletedEvent>(e => e.RoleName == "Editors")), Times.Once);
    }

    [Fact]
    public async Task GetRoles_Pagination_Works()
    {
        // Arrange
        var roles = new List<ApplicationRole>();
        for (int i = 1; i <= 30; i++)
        {
            roles.Add(new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = $"Role{i:00}",
                Description = i % 2 == 0 ? "Even role" : "Odd role",
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            });
        }

        var service = CreateService(roles, out _, out _, out _);

        // Act
        var page = await service.GetRolesAsync(skip: 5, take: 10);

        // Assert
        Assert.Equal(30, page.TotalCount);
        Assert.Equal(10, page.Items.Count);
    Assert.Equal(5, page.Skip);
    Assert.Equal(10, page.Take);
    }

    [Fact]
    public async Task GetRoles_Search_FiltersByNameOrDescription()
    {
        // Arrange
        var roles = new List<ApplicationRole>
        {
            new() { Id = Guid.NewGuid(), Name = "Admins", Description = "System administrators", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Name = "Editors", Description = "Content team", CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Id = Guid.NewGuid(), Name = "Guests", Description = "Read only", CreatedAt = DateTime.UtcNow.AddDays(-3) }
        };

        var service = CreateService(roles, out _, out _, out _);

        // Act
        var byName = await service.GetRolesAsync(skip: 0, take: 10, search: "admin");
        var byDesc = await service.GetRolesAsync(skip: 0, take: 10, search: "content");

        // Assert
        Assert.Single(byName.Items);
        Assert.Equal("Admins", byName.Items[0].Name);
        Assert.Single(byDesc.Items);
        Assert.Equal("Editors", byDesc.Items[0].Name);
    }

    [Fact]
    public async Task GetRoles_SortByCreatedAt_Descending_Works()
    {
        // Arrange
        var roles = new List<ApplicationRole>
        {
            new() { Id = Guid.NewGuid(), Name = "Zeta", CreatedAt = new DateTime(2024, 1, 1) },
            new() { Id = Guid.NewGuid(), Name = "Alpha", CreatedAt = new DateTime(2025, 1, 1) },
            new() { Id = Guid.NewGuid(), Name = "Beta",  CreatedAt = new DateTime(2023, 1, 1) }
        };

        var service = CreateService(roles, out _, out _, out _);

        // Act
        var page = await service.GetRolesAsync(skip: 0, take: 10, search: null, sortBy: "createdat", sortDirection: "desc");

        // Assert
        Assert.Equal(3, page.Items.Count);
        Assert.Equal("Alpha", page.Items[0].Name); // newest first
    }

    [Fact]
    public async Task UpdateRole_Valid_ShouldPublishRoleUpdatedEvent()
    {
        // Arrange
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = "Editors", Permissions = "users.read" };
        var service = CreateService(new[] { role }, out var roleMgr, out _, out var eventPublisher);
        roleMgr.Setup(x => x.FindByIdAsync(role.Id.ToString())).ReturnsAsync(role);
        roleMgr.Setup(x => x.UpdateAsync(role)).ReturnsAsync(IdentityResult.Success);

        var dto = new UpdateRoleDto
        {
            Name = "Editors",
            Description = "Updated description",
            Permissions = new List<string> { Core.Domain.Constants.Permissions.Users.Read }
        };

        // Act
        var (success, errors) = await service.UpdateRoleAsync(role.Id, dto);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        eventPublisher.Verify(x => x.PublishAsync(It.Is<RoleUpdatedEvent>(e => e.RoleName == "Editors")), Times.Once);
    }

    [Fact]
    public async Task UpdateRole_PermissionsChanged_ShouldPublishRolePermissionChangedEvent()
    {
        // Arrange
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = "Editors", Permissions = "users.read" };
        var service = CreateService(new[] { role }, out var roleMgr, out _, out var eventPublisher);
        roleMgr.Setup(x => x.FindByIdAsync(role.Id.ToString())).ReturnsAsync(role);
        roleMgr.Setup(x => x.UpdateAsync(role)).ReturnsAsync(IdentityResult.Success);

        var dto = new UpdateRoleDto
        {
            Name = "Editors",
            Description = "Updated description",
            Permissions = new List<string> { Core.Domain.Constants.Permissions.Users.Read, Core.Domain.Constants.Permissions.Roles.Read }
        };

        // Act
        var (success, errors) = await service.UpdateRoleAsync(role.Id, dto);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        eventPublisher.Verify(x => x.PublishAsync(It.Is<RoleUpdatedEvent>(e => e.RoleName == "Editors")), Times.Once);
        eventPublisher.Verify(x => x.PublishAsync(It.Is<RolePermissionChangedEvent>(e => 
            e.RoleName == "Editors" && 
            e.PermissionChanges.Contains("users.read") && 
            e.PermissionChanges.Contains("roles.read"))), Times.Once);
    }

    [Fact]
    public async Task DeleteRole_ApplicationManagerSystemRole_ShouldFail()
    {
        // Arrange
        var appManagerRole = new ApplicationRole 
        { 
            Id = Guid.NewGuid(), 
            Name = Core.Domain.Constants.AuthConstants.Roles.ApplicationManager, 
            IsSystem = true 
        };
        var service = CreateService(new[] { appManagerRole }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByIdAsync(appManagerRole.Id.ToString())).ReturnsAsync(appManagerRole);

        // Act
        var (success, errors) = await service.DeleteRoleAsync(appManagerRole.Id);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Cannot delete system roles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteRole_AdminSystemRole_ShouldFail()
    {
        // Arrange
        var adminRole = new ApplicationRole 
        { 
            Id = Guid.NewGuid(), 
            Name = Core.Domain.Constants.AuthConstants.Roles.Admin, 
            IsSystem = true 
        };
        var service = CreateService(new[] { adminRole }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByIdAsync(adminRole.Id.ToString())).ReturnsAsync(adminRole);

        // Act
        var (success, errors) = await service.DeleteRoleAsync(adminRole.Id);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Cannot delete system roles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteRole_UserSystemRole_ShouldFail()
    {
        // Arrange
        var userRole = new ApplicationRole 
        { 
            Id = Guid.NewGuid(), 
            Name = Core.Domain.Constants.AuthConstants.Roles.User, 
            IsSystem = true 
        };
        var service = CreateService(new[] { userRole }, out var roleMgr, out _, out _);
        roleMgr.Setup(x => x.FindByIdAsync(userRole.Id.ToString())).ReturnsAsync(userRole);

        // Act
        var (success, errors) = await service.DeleteRoleAsync(userRole.Id);

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("Cannot delete system roles", StringComparison.OrdinalIgnoreCase));
    }
}
