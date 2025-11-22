using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Events;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class UserManagementServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher;
    private readonly UserManagementService _service;

    public UserManagementServiceTests()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        _mockRoleManager = new Mock<RoleManager<ApplicationRole>>(
            roleStore.Object, null!, null!, null!, null!);

        _mockEventPublisher = new Mock<IDomainEventPublisher>();

        _service = new UserManagementService(
            _mockUserManager.Object,
            _mockRoleManager.Object,
            _mockEventPublisher.Object);
    }

    #region GetUsersAsync Tests

    [Fact]
    public async Task GetUsersAsync_ShouldReturnPagedUsers_WhenUsersExist()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "user1@test.com", UserName = "user1", IsActive = true, IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "user2@test.com", UserName = "user2", IsActive = true, IsDeleted = false }
        }.AsQueryable();

        _mockUserManager.Setup(m => m.Users).Returns(users);
        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, u => u.Email == "user1@test.com");
        Assert.Contains(result.Items, u => u.Email == "user2@test.com");
    }

    [Fact]
    public async Task GetUsersAsync_ShouldFilterBySearch_WhenSearchProvided()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "john@test.com", UserName = "john", FirstName = "John", IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "jane@test.com", UserName = "jane", FirstName = "Jane", IsDeleted = false }
        }.AsQueryable();

        _mockUserManager.Setup(m => m.Users).Returns(users);
        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25, search: "john");

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("john@test.com", result.Items.First().Email);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldFilterByIsActive_WhenIsActiveProvided()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "active@test.com", IsActive = true, IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "inactive@test.com", IsActive = false, IsDeleted = false }
        }.AsQueryable();

        _mockUserManager.Setup(m => m.Users).Returns(users);
        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25, isActive: true);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("active@test.com", result.Items.First().Email);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldExcludeDeletedUsers_Always()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "user1@test.com", IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "deleted@test.com", IsDeleted = true }
        }.AsQueryable();

        _mockUserManager.Setup(m => m.Users).Returns(users);
        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.DoesNotContain(result.Items, u => u.Email == "deleted@test.com");
    }

    [Fact]
    public async Task GetUsersAsync_ShouldSortByEmail_WhenSortByEmailProvided()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "z@test.com", IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "a@test.com", IsDeleted = false }
        }.AsQueryable();

        _mockUserManager.Setup(m => m.Users).Returns(users);
        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25, sortBy: "email", sortDirection: "asc");

        // Assert
        Assert.Equal("a@test.com", result.Items.First().Email);
        Assert.Equal("z@test.com", result.Items.Last().Email);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldSortDescending_WhenSortDirectionDescProvided()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "a@test.com", IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "z@test.com", IsDeleted = false }
        }.AsQueryable();

        _mockUserManager.Setup(m => m.Users).Returns(users);
        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25, sortBy: "email", sortDirection: "desc");

        // Assert
        Assert.Equal("z@test.com", result.Items.First().Email);
        Assert.Equal("a@test.com", result.Items.Last().Email);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldApplyPagination_WhenSkipAndTakeProvided()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = Guid.NewGuid(), Email = "user1@test.com", IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "user2@test.com", IsDeleted = false },
            new ApplicationUser { Id = Guid.NewGuid(), Email = "user3@test.com", IsDeleted = false }
        }.AsQueryable();

        _mockUserManager.Setup(m => m.Users).Returns(users);
        _mockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _service.GetUsersAsync(skip: 1, take: 1);

        // Assert
        Assert.Equal(3, result.TotalCount);
        Assert.Single(result.Items);
    }

    #endregion

    #region GetUserByIdAsync Tests

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnUser_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@test.com",
            UserName = "testuser",
            FirstName = "Test",
            LastName = "User"
        };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin" });

        // Act
        var result = await _service.GetUserByIdAsync(userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.Id);
        Assert.Equal("test@test.com", result.Email);
        Assert.Contains("Admin", result.Roles);
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnNull_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _service.GetUserByIdAsync(userId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_ShouldCreateUser_WhenValidDataProvided()
    {
        // Arrange
        var createDto = new CreateUserDto
        {
            Email = "newuser@test.com",
            UserName = "newuser",
            Password = "Test@123",
            FirstName = "New",
            LastName = "User",
            Roles = new List<string>()
        };

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, userId, errors) = await _service.CreateUserAsync(createDto);

        // Assert
        Assert.True(success);
        Assert.NotNull(userId);
        Assert.Empty(errors);
        _mockEventPublisher.Verify(m => m.PublishAsync(It.IsAny<UserCreatedEvent>()), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldAssignRoles_WhenRolesProvided()
    {
        // Arrange
        var createDto = new CreateUserDto
        {
            Email = "newuser@test.com",
            UserName = "newuser",
            Password = "Test@123",
            Roles = new List<string> { "Admin", "User" }
        };

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, userId, errors) = await _service.CreateUserAsync(createDto);

        // Assert
        Assert.True(success);
        _mockUserManager.Verify(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), 
            It.Is<IEnumerable<string>>(r => r.Contains("Admin") && r.Contains("User"))), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldReturnErrors_WhenUserCreationFails()
    {
        // Arrange
        var createDto = new CreateUserDto
        {
            Email = "invalid",
            UserName = "newuser",
            Password = "weak",
            Roles = new List<string>()
        };

        var identityErrors = new[]
        {
            new IdentityError { Description = "Invalid email format" },
            new IdentityError { Description = "Password too weak" }
        };

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(identityErrors));

        // Act
        var (success, userId, errors) = await _service.CreateUserAsync(createDto);

        // Assert
        Assert.False(success);
        Assert.Null(userId);
        Assert.Equal(2, errors.Count());
        Assert.Contains("Invalid email format", errors);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldSetDefaultProperties_WhenCreatingUser()
    {
        // Arrange
        var createDto = new CreateUserDto
        {
            Email = "newuser@test.com",
            UserName = "newuser",
            Password = "Test@123",
            Roles = new List<string>()
        };

        ApplicationUser? capturedUser = null;
        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((u, p) => capturedUser = u)
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _service.CreateUserAsync(createDto);

        // Assert
        Assert.NotNull(capturedUser);
        Assert.True(capturedUser.IsActive);
        Assert.True(capturedUser.EmailConfirmed);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateUser_WhenValidDataProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingUser = new ApplicationUser
        {
            Id = userId,
            Email = "old@test.com",
            UserName = "olduser"
        };

        var updateDto = new UpdateUserDto
        {
            Email = "new@test.com",
            UserName = "newuser",
            FirstName = "Updated",
            Roles = new List<string> { "User" }
        };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(existingUser);
        _mockUserManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string>());
        _mockUserManager.Setup(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await _service.UpdateUserAsync(userId, updateDto);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        Assert.Equal("new@test.com", existingUser.Email);
        _mockEventPublisher.Verify(m => m.PublishAsync(It.IsAny<UserUpdatedEvent>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnError_WhenUserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var updateDto = new UpdateUserDto { Email = "test@test.com", Roles = new List<string>() };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var (success, errors) = await _service.UpdateUserAsync(userId, updateDto);

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateRoles_WhenRolesChanged()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingUser = new ApplicationUser { Id = userId, Email = "test@test.com" };
        var updateDto = new UpdateUserDto
        {
            Email = "test@test.com",
            Roles = new List<string> { "Admin" }
        };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(existingUser);
        _mockUserManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string> { "User" });
        _mockUserManager.Setup(m => m.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await _service.UpdateUserAsync(userId, updateDto);

        // Assert
        Assert.True(success);
        _mockUserManager.Verify(m => m.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), 
            It.Is<IEnumerable<string>>(r => r.Contains("User"))), Times.Once);
        _mockUserManager.Verify(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), 
            It.Is<IEnumerable<string>>(r => r.Contains("Admin"))), Times.Once);
    }

    #endregion

    #region DeactivateUserAsync Tests

    [Fact]
    public async Task DeactivateUserAsync_ShouldDeactivateUser_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, IsActive = true };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await _service.DeactivateUserAsync(userId);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        Assert.False(user.IsActive);
        _mockEventPublisher.Verify(m => m.PublishAsync(It.IsAny<UserAccountStatusChangedEvent>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateUserAsync_ShouldReturnError_WhenUserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var (success, errors) = await _service.DeactivateUserAsync(userId);

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
    }

    #endregion

    #region ReactivateUserAsync Tests

    [Fact]
    public async Task ReactivateUserAsync_ShouldReactivateUser_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, IsActive = false };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await _service.ReactivateUserAsync(userId);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        Assert.True(user.IsActive);
        _mockEventPublisher.Verify(m => m.PublishAsync(It.IsAny<UserAccountStatusChangedEvent>()), Times.Once);
    }

    [Fact]
    public async Task ReactivateUserAsync_ShouldReturnError_WhenUserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var (success, errors) = await _service.ReactivateUserAsync(userId);

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
    }

    #endregion

    #region AssignRolesAsync Tests

    [Fact]
    public async Task AssignRolesAsync_ShouldAssignRoles_WhenValidRolesProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, UserName = "testuser" };
        var roles = new List<string> { "Admin", "User" };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string>());
        _mockUserManager.Setup(m => m.AddToRolesAsync(user, roles))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await _service.AssignRolesAsync(userId, roles);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(m => m.AddToRolesAsync(user, roles), Times.Once);
    }

    [Fact]
    public async Task AssignRolesAsync_ShouldReturnError_WhenUserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var (success, errors) = await _service.AssignRolesAsync(userId, new List<string> { "Admin" });

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
    }

    [Fact]
    public async Task AssignRolesAsync_ShouldRemoveOldRoles_WhenReplacingRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, UserName = "testuser" };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "OldRole" });
        _mockUserManager.Setup(m => m.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await _service.AssignRolesAsync(userId, new List<string> { "NewRole" });

        // Assert
        Assert.True(success);
        _mockUserManager.Verify(m => m.RemoveFromRolesAsync(user, 
            It.Is<IEnumerable<string>>(r => r.Contains("OldRole"))), Times.Once);
    }

    #endregion

    #region AssignRolesByIdAsync Tests

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldResolveIdsToNames_WhenValidIdsProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, UserName = "testuser" };
        var role1 = new ApplicationRole { Id = roleId1, Name = "Admin" };
        var role2 = new ApplicationRole { Id = roleId2, Name = "User" };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _mockRoleManager.Setup(m => m.FindByIdAsync(roleId1.ToString()))
            .ReturnsAsync(role1);
        _mockRoleManager.Setup(m => m.FindByIdAsync(roleId2.ToString()))
            .ReturnsAsync(role2);
        _mockUserManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string>());
        _mockUserManager.Setup(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var (success, errors) = await _service.AssignRolesByIdAsync(userId, new[] { roleId1, roleId2 });

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(m => m.AddToRolesAsync(user, 
            It.Is<IEnumerable<string>>(r => r.Contains("Admin") && r.Contains("User"))), Times.Once);
    }

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldReturnError_WhenRoleIdNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidRoleId = Guid.NewGuid();

        _mockRoleManager.Setup(m => m.FindByIdAsync(invalidRoleId.ToString()))
            .ReturnsAsync((ApplicationRole?)null);

        // Act
        var (success, errors) = await _service.AssignRolesByIdAsync(userId, new[] { invalidRoleId });

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("not found"));
    }

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldReturnAllErrors_WhenMultipleInvalidIds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidRoleId1 = Guid.NewGuid();
        var invalidRoleId2 = Guid.NewGuid();

        _mockRoleManager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationRole?)null);

        // Act
        var (success, errors) = await _service.AssignRolesByIdAsync(userId, new[] { invalidRoleId1, invalidRoleId2 });

        // Assert
        Assert.False(success);
        Assert.Equal(2, errors.Count());
    }

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldReturnError_WhenMixedValidAndInvalidIds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var validId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();
        var role = new ApplicationRole { Id = validId, Name = "Admin" };

        _mockRoleManager.Setup(m => m.FindByIdAsync(validId.ToString())).ReturnsAsync(role);
        _mockRoleManager.Setup(m => m.FindByIdAsync(invalidId.ToString())).ReturnsAsync((ApplicationRole?)null);

        // Act
        var (success, errors) = await _service.AssignRolesByIdAsync(userId, new[] { validId, invalidId });

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains("not found"));
    }

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldHandleDuplicateIds_WhenDuplicateIdsProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, UserName = "testuser" };
        var role = new ApplicationRole { Id = roleId, Name = "Admin" };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _mockRoleManager.Setup(m => m.FindByIdAsync(roleId.ToString())).ReturnsAsync(role);
        _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string>());
        _mockUserManager.Setup(m => m.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>() ))
            .ReturnsAsync(IdentityResult.Success);

        // Act - pass duplicate role IDs
        var (success, errors) = await _service.AssignRolesByIdAsync(userId, new[] { roleId, roleId });

        // Assert - should succeed and only call AddToRolesAsync with unique role name once
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(m => m.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Count() == 1 && r.Contains("Admin"))), Times.Once);
    }

    #endregion

    #region UpdateLastLoginAsync Tests

    [Fact]
    public async Task UpdateLastLoginAsync_ShouldUpdateTimestamp_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _service.UpdateLastLoginAsync(userId);

        // Assert
        Assert.NotNull(user.LastLoginDate);
        _mockUserManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task UpdateLastLoginAsync_ShouldDoNothing_WhenUserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        await _service.UpdateLastLoginAsync(userId);

        // Assert
        _mockUserManager.Verify(m => m.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    #endregion
}
