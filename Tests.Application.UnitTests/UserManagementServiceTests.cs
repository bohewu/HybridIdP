using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class UserManagementServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;  // Keep for simple tests
    private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;  // Keep for simple tests
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher;
    private readonly UserManagementService _service;

    public UserManagementServiceTests()
    {
        // Create InMemory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _context = new ApplicationDbContext(options);

        // Create real UserStore and RoleStore with InMemory database
        var userStore = new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(_context);
        var roleStore = new RoleStore<ApplicationRole, ApplicationDbContext, Guid>(_context);

        // Create real UserManager with validators to ensure uniqueness checks
        var identityOptions = Options.Create(new IdentityOptions());
        _userManager = new UserManager<ApplicationUser>(
            userStore,
            identityOptions,
            new PasswordHasher<ApplicationUser>(),
            new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() },  // Add validator for username uniqueness
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            new Mock<ILogger<UserManager<ApplicationUser>>>().Object);

        // Create real RoleManager
        _roleManager = new RoleManager<ApplicationRole>(
            roleStore,
            Array.Empty<IRoleValidator<ApplicationRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);

        // Also keep mock versions for tests that don't need real DB
        var mockUserStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            mockUserStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var mockRoleStore = new Mock<IRoleStore<ApplicationRole>>();
        _mockRoleManager = new Mock<RoleManager<ApplicationRole>>(
            mockRoleStore.Object, null!, null!, null!, null!);

        _mockEventPublisher = new Mock<IDomainEventPublisher>();

        _service = new UserManagementService(
            _userManager,
            _roleManager,
            _mockEventPublisher.Object,
            _context);
    }

    public void Dispose()
    {
        _userManager?.Dispose();
        _roleManager?.Dispose();
        _context?.Database.EnsureDeleted();
        _context?.Dispose();
    }

    #region GetUsersAsync Tests

    [Fact]
    public async Task GetUsersAsync_ShouldReturnPagedUsers_WhenUsersExist()
    {
        // Arrange - Create User role first
        await _roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        
        // Add users to InMemory database
        var user1 = new ApplicationUser { Email = "user1@test.com", UserName = "user1", IsActive = true, IsDeleted = false };
        var user2 = new ApplicationUser { Email = "user2@test.com", UserName = "user2", IsActive = true, IsDeleted = false };
        
        await _userManager.CreateAsync(user1);
        await _userManager.CreateAsync(user2);
        await _userManager.AddToRoleAsync(user1, "User");
        await _userManager.AddToRoleAsync(user2, "User");

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
        // Arrange - Add users to database
        var john = new ApplicationUser { Email = "john@test.com", UserName = "john", FirstName = "John", IsDeleted = false };
        var jane = new ApplicationUser { Email = "jane@test.com", UserName = "jane", FirstName = "Jane", IsDeleted = false };
        
        await _userManager.CreateAsync(john);
        await _userManager.CreateAsync(jane);

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
        // Arrange - Add users to database
        var active = new ApplicationUser { Email = "active@test.com", UserName = "active", IsActive = true, IsDeleted = false };
        var inactive = new ApplicationUser { Email = "inactive@test.com", UserName = "inactive", IsActive = false, IsDeleted = false };
        
        await _userManager.CreateAsync(active);
        await _userManager.CreateAsync(inactive);

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
        // Arrange - Add users to database
        var user1 = new ApplicationUser { Email = "user1@test.com", UserName = "user1", IsDeleted = false };
        var deleted = new ApplicationUser { Email = "deleted@test.com", UserName = "deleted", IsDeleted = true };
        
        await _userManager.CreateAsync(user1);
        await _userManager.CreateAsync(deleted);

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.DoesNotContain(result.Items, u => u.Email == "deleted@test.com");
    }

    [Fact]
    public async Task GetUsersAsync_ShouldSortByEmail_WhenSortByEmailProvided()
    {
        // Arrange - Create users in database
        var user1 = new ApplicationUser { Email = "z@test.com", UserName = "zuser", IsDeleted = false };
        var user2 = new ApplicationUser { Email = "a@test.com", UserName = "auser", IsDeleted = false };
        await _userManager.CreateAsync(user1);
        await _userManager.CreateAsync(user2);

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25, sortBy: "email", sortDirection: "asc");

        // Assert
        Assert.Equal("a@test.com", result.Items.First().Email);
        Assert.Equal("z@test.com", result.Items.Last().Email);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldSortDescending_WhenSortDirectionDescProvided()
    {
        // Arrange - Create users in database
        var user1 = new ApplicationUser { Email = "a@test.com", UserName = "auser", IsDeleted = false };
        var user2 = new ApplicationUser { Email = "z@test.com", UserName = "zuser", IsDeleted = false };
        await _userManager.CreateAsync(user1);
        await _userManager.CreateAsync(user2);

        // Act
        var result = await _service.GetUsersAsync(skip: 0, take: 25, sortBy: "email", sortDirection: "desc");

        // Assert
        Assert.Equal("z@test.com", result.Items.First().Email);
        Assert.Equal("a@test.com", result.Items.Last().Email);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldApplyPagination_WhenSkipAndTakeProvided()
    {
        // Arrange - Create users in database
        var user1 = new ApplicationUser { Email = "user1@test.com", UserName = "user1", IsDeleted = false };
        var user2 = new ApplicationUser { Email = "user2@test.com", UserName = "user2", IsDeleted = false };
        var user3 = new ApplicationUser { Email = "user3@test.com", UserName = "user3", IsDeleted = false };
        await _userManager.CreateAsync(user1);
        await _userManager.CreateAsync(user2);
        await _userManager.CreateAsync(user3);

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
        // Arrange - Add user to database
        var user = new ApplicationUser
        {
            Email = "test@test.com",
            UserName = "testuser",
            FirstName = "Test",
            LastName = "User"
        };

        await _userManager.CreateAsync(user);
        
        // Create Admin role and assign to user
        var adminRole = new ApplicationRole { Name = "Admin" };
        await _roleManager.CreateAsync(adminRole);
        await _userManager.AddToRoleAsync(user, "Admin");

        // Act
        var result = await _service.GetUserByIdAsync(user.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal("test@test.com", result.Email);
        Assert.Contains("Admin", result.Roles);
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnNull_WhenUserDoesNotExist()
    {
        // Arrange - Use non-existent ID
        var userId = Guid.NewGuid();

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

        // Act
        var (success, userId, errors) = await _service.CreateUserAsync(createDto);

        // Assert
        Assert.True(success);
        Assert.NotNull(userId);
        Assert.Empty(errors);
        _mockEventPublisher.Verify(m => m.PublishAsync(It.IsAny<UserCreatedEvent>()), Times.Once);
        
        // Verify user was actually created in database
        var createdUser = await _userManager.FindByIdAsync(userId.ToString()!);
        Assert.NotNull(createdUser);
        Assert.Equal("newuser@test.com", createdUser.Email);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldAssignRoles_WhenRolesProvided()
    {
        // Arrange - Create roles first
        await _roleManager.CreateAsync(new ApplicationRole { Name = "Admin" });
        await _roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        
        var createDto = new CreateUserDto
        {
            Email = "newuser@test.com",
            UserName = "newuser",
            Password = "Test@123",
            Roles = new List<string> { "Admin", "User" }
        };

        // Act
        var (success, userId, errors) = await _service.CreateUserAsync(createDto);

        // Assert
        Assert.True(success);
        var createdUser = await _userManager.FindByIdAsync(userId.ToString()!);
        var roles = await _userManager.GetRolesAsync(createdUser!);
        Assert.Contains("Admin", roles);
        Assert.Contains("User", roles);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldReturnErrors_WhenUserCreationFails()
    {
        // Arrange - Create a user with duplicate username to trigger error
        var existingUser = new ApplicationUser { UserName = "duplicateuser", Email = "existing@test.com" };
        await _userManager.CreateAsync(existingUser, "Password@123");
        
        var createDto = new CreateUserDto
        {
            Email = "newemail@test.com",
            UserName = "duplicateuser",  // Duplicate username - should fail
            Password = "Test@123",
            Roles = new List<string>()
        };

        // Act
        var (success, userId, errors) = await _service.CreateUserAsync(createDto);

        // Assert
        Assert.False(success);
        Assert.Null(userId);
        Assert.NotEmpty(errors);
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

        // Act
        var (success, userId, _) = await _service.CreateUserAsync(createDto);

        // Assert
        var createdUser = await _userManager.FindByIdAsync(userId.ToString()!);
        Assert.NotNull(createdUser);
        Assert.True(createdUser!.IsActive);
        Assert.True(createdUser.EmailConfirmed);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateUser_WhenValidDataProvided()
    {
        // Arrange - Create user and role in database
        var existingUser = new ApplicationUser { Email = "old@test.com", UserName = "olduser" };
        await _userManager.CreateAsync(existingUser);
        
        await _roleManager.CreateAsync(new ApplicationRole { Name = "User" });

        var updateDto = new UpdateUserDto
        {
            Email = "new@test.com",
            UserName = "newuser",
            FirstName = "Updated",
            Roles = new List<string> { "User" }
        };

        // Act
        var (success, errors) = await _service.UpdateUserAsync(existingUser.Id, updateDto);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        
        var updatedUser = await _userManager.FindByIdAsync(existingUser.Id.ToString());
        Assert.Equal("new@test.com", updatedUser!.Email);
        _mockEventPublisher.Verify(m => m.PublishAsync(It.IsAny<UserUpdatedEvent>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnError_WhenUserNotFound()
    {
        // Arrange - Use non-existent ID
        var userId = Guid.NewGuid();
        var updateDto = new UpdateUserDto { Email = "test@test.com", Roles = new List<string>() };

        // Act
        var (success, errors) = await _service.UpdateUserAsync(userId, updateDto);

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateRoles_WhenRolesChanged()
    {
        // Arrange - Create user with User role
        var existingUser = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(existingUser);
        
        await _roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        await _roleManager.CreateAsync(new ApplicationRole { Name = "Admin" });
        await _userManager.AddToRoleAsync(existingUser, "User");
        
        var updateDto = new UpdateUserDto
        {
            Email = "test@test.com",
            Roles = new List<string> { "Admin" }
        };

        // Act
        var (success, errors) = await _service.UpdateUserAsync(existingUser.Id, updateDto);

        // Assert
        Assert.True(success);
        
        var roles = await _userManager.GetRolesAsync(existingUser);
        Assert.Contains("Admin", roles);
        Assert.DoesNotContain("User", roles);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnError_WhenAddToRolesFails()
    {
        // Arrange - Create user but DON'T create the role (will cause failure)
        var existingUser = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(existingUser);
        
        var updateDto = new UpdateUserDto { Email = "test@test.com", Roles = new List<string> { "NonExistentRole" } };

        // Act & Assert - Expect exception for non-existent role
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.UpdateUserAsync(existingUser.Id, updateDto);
        });
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnError_WhenRemoveFromRolesFails()
    {
        // Arrange - Create user with a role, then try to assign non-existent role
        var existingUser = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(existingUser);
        
        await _roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        await _userManager.AddToRoleAsync(existingUser, "User");
        
        // Try to update with non-existent role (this will throw exception)
        var updateDto = new UpdateUserDto { Email = "test@test.com", Roles = new List<string> { "NonExistentRole" } };

        // Act & Assert - Expect exception for non-existent role
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.UpdateUserAsync(existingUser.Id, updateDto);
        });
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnError_WhenRolesContainNonExistentRoleName()
    {
        // Arrange - Create user without creating the role
        var existingUser = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(existingUser);
        
        var updateDto = new UpdateUserDto { Email = "test@test.com", Roles = new List<string> { "NoSuchRole" } };

        // Act & Assert - Expect exception for non-existent role
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.UpdateUserAsync(existingUser.Id, updateDto);
        });
    }

    #endregion

    #region DeactivateUserAsync Tests

    [Fact]
    public async Task DeactivateUserAsync_ShouldDeactivateUser_WhenUserExists()
    {
        // Arrange
        var user = new ApplicationUser 
        { 
            Email = "test@test.com", 
            UserName = "testuser", 
            IsActive = true 
        };
        await _userManager.CreateAsync(user);

        // Act
        var (success, errors) = await _service.DeactivateUserAsync(user.Id);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        var deactivatedUser = await _userManager.FindByIdAsync(user.Id.ToString());
        Assert.False(deactivatedUser!.IsActive);
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
        var user = new ApplicationUser 
        { 
            Email = "test@test.com", 
            UserName = "testuser", 
            IsActive = false 
        };
        await _userManager.CreateAsync(user);

        // Act
        var (success, errors) = await _service.ReactivateUserAsync(user.Id);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        var reactivatedUser = await _userManager.FindByIdAsync(user.Id.ToString());
        Assert.True(reactivatedUser!.IsActive);
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
        var user = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(user);
        
        await _roleManager.CreateAsync(new ApplicationRole { Name = "Admin" });
        await _roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        var roles = new List<string> { "Admin", "User" };

        // Act
        var (success, errors) = await _service.AssignRolesAsync(user.Id, roles);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        var assignedRoles = await _userManager.GetRolesAsync(user);
        Assert.Contains("Admin", assignedRoles);
        Assert.Contains("User", assignedRoles);
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
        var user = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(user);
        
        await _roleManager.CreateAsync(new ApplicationRole { Name = "OldRole" });
        await _roleManager.CreateAsync(new ApplicationRole { Name = "NewRole" });
        await _userManager.AddToRoleAsync(user, "OldRole");

        // Act
        var (success, errors) = await _service.AssignRolesAsync(user.Id, new List<string> { "NewRole" });

        // Assert
        Assert.True(success);
        var roles = await _userManager.GetRolesAsync(user);
        Assert.DoesNotContain("OldRole", roles);
        Assert.Contains("NewRole", roles);
    }

    #endregion

    #region AssignRolesByIdAsync Tests

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldResolveIdsToNames_WhenValidIdsProvided()
    {
        // Arrange
        var user = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(user);
        
        var role1 = new ApplicationRole { Name = "Admin" };
        var role2 = new ApplicationRole { Name = "User" };
        await _roleManager.CreateAsync(role1);
        await _roleManager.CreateAsync(role2);

        // Act
        var (success, errors) = await _service.AssignRolesByIdAsync(user.Id, new[] { role1.Id, role2.Id });

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        var assignedRoles = await _userManager.GetRolesAsync(user);
        Assert.Contains("Admin", assignedRoles);
        Assert.Contains("User", assignedRoles);
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
        var user = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(user);
        
        var role = new ApplicationRole { Name = "Admin" };
        await _roleManager.CreateAsync(role);

        // Act - pass duplicate role IDs
        var (success, errors) = await _service.AssignRolesByIdAsync(user.Id, new[] { role.Id, role.Id });

        // Assert - should succeed and only assign unique role once
        Assert.True(success);
        Assert.Empty(errors);
        var assignedRoles = await _userManager.GetRolesAsync(user);
        Assert.Single(assignedRoles);
        Assert.Contains("Admin", assignedRoles);
    }

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldReturnError_WhenAddToRolesFails()
    {
        // Arrange - Create user but try to assign with invalid role ID
        var user = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(user);
        
        var invalidRoleId = Guid.NewGuid();

        // Act
        var (success, errors) = await _service.AssignRolesByIdAsync(user.Id, new[] { invalidRoleId });

        // Assert
        Assert.False(success);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("not found"));
    }

    [Fact]
    public async Task AssignRolesByIdAsync_ShouldReturnError_WhenUserNotFound()
    {
        // Arrange - Create role but use non-existent user ID
        var role = new ApplicationRole { Name = "Admin" };
        await _roleManager.CreateAsync(role);
        
        var nonExistentUserId = Guid.NewGuid();

        // Act
        var (success, errors) = await _service.AssignRolesByIdAsync(nonExistentUserId, new[] { role.Id });

        // Assert
        Assert.False(success);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("User not found"));
    }

    #endregion

    #region UpdateLastLoginAsync Tests

    [Fact]
    public async Task UpdateLastLoginAsync_ShouldUpdateTimestamp_WhenUserExists()
    {
        // Arrange
        var user = new ApplicationUser { Email = "test@test.com", UserName = "testuser" };
        await _userManager.CreateAsync(user);

        // Act
        await _service.UpdateLastLoginAsync(user.Id);

        // Assert
        var updatedUser = await _userManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(updatedUser!.LastLoginDate);
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
