using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Tests.Application.UnitTests;

public class UserManagementTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher;

    public UserManagementTests()
    {
        // Setup UserManager mock
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            Mock.Of<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<ApplicationUser>>>());

        // Setup RoleManager mock
        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        _mockRoleManager = new Mock<RoleManager<ApplicationRole>>(
            roleStore.Object,
            Array.Empty<IRoleValidator<ApplicationRole>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<RoleManager<ApplicationRole>>>());

        // Setup EventPublisher mock
        _mockEventPublisher = new Mock<IDomainEventPublisher>();
    }

    // Helper method to create InMemory ApplicationDbContext for tests
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateUser_WithValidData_ShouldSucceed()
    {
        // Arrange
        var createDto = new CreateUserDto
        {
            Email = "test@example.com",
            UserName = "testuser",
            Password = "Test@123456",
            FirstName = "Test",
            LastName = "User",
            Department = "IT",
            IsActive = true,
            Roles = new List<string> { "User" }
        };

        ApplicationUser? createdUser = null;
        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((u, p) => createdUser = u)
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());
        var adminId = Guid.NewGuid();

        // Act
        var result = await sut.CreateUserAsync(createDto, adminId);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.UserId);
        Assert.Empty(result.Errors);
        Assert.NotNull(createdUser);
        Assert.Equal(createDto.Email, createdUser!.Email);
        Assert.Equal(createDto.UserName, createdUser.UserName);
        Assert.True(createdUser.IsActive);
        Assert.True(createdUser.EmailConfirmed);
        Assert.Equal(adminId, createdUser.CreatedBy);
        Assert.True((DateTime.UtcNow - createdUser.CreatedAt).TotalMinutes < 5);

        // Verify domain event was published
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserCreatedEvent>(e =>
            e.UserId == result.UserId.ToString() &&
            e.UserName == createDto.UserName &&
            e.Email == createDto.Email)), Times.Once);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_ShouldFail()
    {
        // Arrange
        var createDto = new CreateUserDto
        {
            Email = "existing@example.com",
            UserName = "existinguser",
            Password = "Test@123456"
        };

        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = "Email already exists"
            }));

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var result = await sut.CreateUserAsync(createDto);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.UserId);
        Assert.Contains("Email already exists", result.Errors);

        // Verify domain event was not published
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<UserCreatedEvent>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUser_WithValidData_ShouldSucceed()
    {
        // Arrange - Use real UserManager with InMemory database
        var context = CreateInMemoryContext();
        var (userManager, roleManager) = CreateRealUserAndRoleManager(context);
        
        var existingUser = new ApplicationUser
        {
            Email = "test@example.com",
            UserName = "testuser",
            FirstName = "Test",
            LastName = "User"
        };
        await userManager.CreateAsync(existingUser);
        
        await roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        await roleManager.CreateAsync(new ApplicationRole { Name = "Admin" });
        await userManager.AddToRoleAsync(existingUser, "User");

        var updateDto = new UpdateUserDto
        {
            Email = "test@example.com",
            UserName = "testuser",
            FirstName = "Updated",
            LastName = "User",
            Department = "Engineering",
            IsActive = true,
            Roles = new List<string> { "Admin" },
            EmailConfirmed = true
        };

        var sut = new UserManagementService(userManager, roleManager, _mockEventPublisher.Object, context);

        // Act
        var (success, errors) = await sut.UpdateUserAsync(existingUser.Id, updateDto);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        
        var updatedUser = await userManager.FindByIdAsync(existingUser.Id.ToString());
        Assert.Equal("Updated", updatedUser!.FirstName);
        
        var roles = await userManager.GetRolesAsync(updatedUser);
        Assert.DoesNotContain("User", roles);
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task UpdateUser_NonExistent_ShouldFail()
    {
        // Arrange - Use real UserManager with InMemory database
        var context = CreateInMemoryContext();
        var (userManager, roleManager) = CreateRealUserAndRoleManager(context);
        
        var nonExistentUserId = Guid.NewGuid();
        var updateDto = new UpdateUserDto
        {
            Email = "test@example.com",
            UserName = "testuser",
            Roles = new List<string>()
        };

        var sut = new UserManagementService(userManager, roleManager, _mockEventPublisher.Object, context);

        // Act
        var (success, errors) = await sut.UpdateUserAsync(nonExistentUserId, updateDto);

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
    }

    [Fact]
    public async Task GetUser_ById_ShouldReturnUserDetail()
    {
        // Arrange - Use real UserManager with InMemory database
        var context = CreateInMemoryContext();
        var (userManager, roleManager) = CreateRealUserAndRoleManager(context);
        
        var user = new ApplicationUser
        {
            Email = "test@example.com",
            UserName = "testuser",
            FirstName = "Test",
            LastName = "User",
            Department = "IT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await userManager.CreateAsync(user);
        
        await roleManager.CreateAsync(new ApplicationRole { Name = "User" });
        await userManager.AddToRoleAsync(user, "User");

        var sut = new UserManagementService(userManager, roleManager, _mockEventPublisher.Object, context);

        // Act
        var detail = await sut.GetUserByIdAsync(user.Id);

        // Assert
        Assert.NotNull(detail);
        Assert.Equal(user.Id, detail!.Id);
        Assert.Equal("test@example.com", detail.Email);
        Assert.Contains("User", detail.Roles);
    }

    [Fact]
    public async Task GetUsers_WithPagination_ShouldReturnPagedResults()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user1@example.com",
                UserName = "user1",
                FirstName = "User",
                LastName = "One",
                IsActive = true
            },
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user2@example.com",
                UserName = "user2",
                FirstName = "User",
                LastName = "Two",
                IsActive = true
            },
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user3@example.com",
                UserName = "user3",
                FirstName = "User",
                LastName = "Three",
                IsActive = true
            }
        };

    _mockUserManager.SetupGet(x => x.Users).Returns(users.AsQueryable());
        _mockUserManager
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string> { "User" });

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var page = await sut.GetUsersAsync(skip: 1, take: 1, sortBy: "email", sortDirection: "asc");

        // Assert
        Assert.Equal(3, page.TotalCount);
    Assert.Single(page.Items);
        Assert.Equal("user2@example.com", page.Items.Single().Email);
    }

    [Fact]
    public async Task GetUsers_WithRoleFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        var role = "Admin";
        var u1 = new ApplicationUser { Id = Guid.NewGuid(), Email = "a@example.com", UserName = "a", IsActive = true };
        var u2 = new ApplicationUser { Id = Guid.NewGuid(), Email = "b@example.com", UserName = "b", IsActive = true };
        var users = new List<ApplicationUser> { u1, u2 };
    _mockUserManager.SetupGet(x => x.Users).Returns(users.AsQueryable());
        _mockUserManager.Setup(x => x.GetRolesAsync(u1)).ReturnsAsync(new List<string> { role });
        _mockUserManager.Setup(x => x.GetRolesAsync(u2)).ReturnsAsync(new List<string> { "User" });

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var page = await sut.GetUsersAsync(role: role, take: 10);

        // Assert
        Assert.Single(page.Items);
        Assert.Equal(u1.Email, page.Items.Single().Email);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetUsers_WithSearchTerm_ShouldReturnMatchingUsers()
    {
        // Arrange
        var searchTerm = "john";
        var u1 = new ApplicationUser { Id = Guid.NewGuid(), Email = "john@example.com", UserName = "john", FirstName = "John", LastName = "Doe", IsActive = true };
        var u2 = new ApplicationUser { Id = Guid.NewGuid(), Email = "jane@example.com", UserName = "jane", FirstName = "Jane", LastName = "Roe", IsActive = true };
    _mockUserManager.SetupGet(x => x.Users).Returns(new List<ApplicationUser> { u1, u2 }.AsQueryable());
        _mockUserManager.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<string>());

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var page = await sut.GetUsersAsync(search: searchTerm, take: 10);

        // Assert
        Assert.Single(page.Items);
        Assert.Equal("john@example.com", page.Items.Single().Email);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task DeleteUser_ShouldDeactivateUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser",
            IsActive = true
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.DeactivateUserAsync(userId);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        Assert.False(user.IsActive);
        _mockUserManager.Verify(x => x.UpdateAsync(It.Is<ApplicationUser>(u => u.Id == userId && u.IsActive == false)), Times.Once);
    }

    [Fact]
    public async Task AssignRolesToUser_WithValidRoles_ShouldSucceed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };
        var roles = new List<string> { "Admin", "User" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _mockUserManager
            .Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, roles);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()), Times.Never);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Single() == "Admin")), Times.Once);
    }

    [Fact]
    public async Task AssignRolesAsync_UserNotFound_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, new[] { "Admin" });

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<UserRoleAssignedEvent>()), Times.Never);
    }

    [Fact]
    public async Task AssignRolesAsync_EmptyRolesList_ShouldRemoveAllRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin", "User" });

        _mockUserManager
            .Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, Array.Empty<string>());

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(
            r => r.Contains("Admin") && r.Contains("User") && r.Count() == 2)), Times.Once);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()), Times.Never);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.RoleName == "Admin" && e.IsAssigned == false)), Times.Once);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.RoleName == "User" && e.IsAssigned == false)), Times.Once);
    }

    [Fact]
    public async Task AssignRolesAsync_InvalidRoleName_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string>());

        _mockUserManager
            .Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidRole", Description = "Role 'InvalidRole' does not exist." }));

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, new[] { "InvalidRole" });

        // Assert
        Assert.False(success);
        Assert.Contains("Role 'InvalidRole' does not exist.", errors);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<UserRoleAssignedEvent>()), Times.Never);
    }

    [Fact]
    public async Task AssignRolesAsync_IdentityErrorOnRemove_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "OldRole" });

        _mockUserManager
            .Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "RemoveError", Description = "Failed to remove role." }));

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, new[] { "NewRole" });

        // Assert
        Assert.False(success);
        Assert.Contains("Failed to remove role.", errors);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<UserRoleAssignedEvent>()), Times.Never);
    }

    [Fact]
    public async Task AssignRolesAsync_SameRoles_ShouldNotModify()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };
        var roles = new List<string> { "Admin", "User" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(roles);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, roles);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()), Times.Never);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()), Times.Never);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<UserRoleAssignedEvent>()), Times.Never);
    }

    [Fact]
    public async Task AssignRolesAsync_AddAndRemoveRoles_ShouldPublishBothEvents()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "OldRole1", "OldRole2" });

        _mockUserManager
            .Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, new[] { "NewRole1", "NewRole2" });

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(
            r => r.Contains("OldRole1") && r.Contains("OldRole2"))), Times.Once);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(
            r => r.Contains("NewRole1") && r.Contains("NewRole2"))), Times.Once);
        
        // Verify events for added roles
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.UserId == userId.ToString() && e.RoleName == "NewRole1" && e.IsAssigned == true)), Times.Once);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.UserId == userId.ToString() && e.RoleName == "NewRole2" && e.IsAssigned == true)), Times.Once);
        
        // Verify events for removed roles
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.UserId == userId.ToString() && e.RoleName == "OldRole1" && e.IsAssigned == false)), Times.Once);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.UserId == userId.ToString() && e.RoleName == "OldRole2" && e.IsAssigned == false)), Times.Once);
    }

    [Fact]
    public async Task AssignRolesAsync_OnlyRemoveRoles_ShouldPublishRemovalEvents()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin", "User", "Manager" });

        _mockUserManager
            .Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act - Keep only "User" role, remove "Admin" and "Manager"
        var (success, errors) = await sut.AssignRolesAsync(userId, new[] { "User" });

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(
            r => r.Contains("Admin") && r.Contains("Manager") && r.Count() == 2)), Times.Once);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()), Times.Never);
        
        // Verify removal events
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.RoleName == "Admin" && e.IsAssigned == false)), Times.Once);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.RoleName == "Manager" && e.IsAssigned == false)), Times.Once);
        
        // Verify no addition events for "User" since it already exists
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.RoleName == "User")), Times.Never);
    }

    [Fact]
    public async Task AssignRolesByIdAsync_WithValidRoleIds_ShouldSucceed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId1 = Guid.NewGuid();
        var roleId2 = Guid.NewGuid();
        
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        var role1 = new ApplicationRole { Id = roleId1, Name = "Admin" };
        var role2 = new ApplicationRole { Id = roleId2, Name = "User" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string>());

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId1.ToString()))
            .ReturnsAsync(role1);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId2.ToString()))
            .ReturnsAsync(role2);

        _mockUserManager
            .Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesByIdAsync(userId, new[] { roleId1, roleId2 });

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(
            r => r.Contains("Admin") && r.Contains("User"))), Times.Once);
    }

    [Fact]
    public async Task AssignRolesByIdAsync_WithInvalidRoleId_ShouldReturnError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var validRoleId = Guid.NewGuid();
        var invalidRoleId = Guid.NewGuid();

        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        var validRole = new ApplicationRole { Id = validRoleId, Name = "Admin" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(validRoleId.ToString()))
            .ReturnsAsync(validRole);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(invalidRoleId.ToString()))
            .ReturnsAsync((ApplicationRole?)null);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesByIdAsync(userId, new[] { validRoleId, invalidRoleId });

        // Assert
        Assert.False(success);
        Assert.Contains(errors, e => e.Contains(invalidRoleId.ToString()));
        Assert.Contains(errors, e => e.Contains("not found"));
        _mockUserManager.Verify(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task AssignRolesByIdAsync_WithAllInvalidRoleIds_ShouldReturnAllErrors()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidRoleId1 = Guid.NewGuid();
        var invalidRoleId2 = Guid.NewGuid();

        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationRole?)null);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesByIdAsync(userId, new[] { invalidRoleId1, invalidRoleId2 });

        // Assert
        Assert.False(success);
        Assert.Equal(2, errors.Count());
        Assert.Contains(errors, e => e.Contains(invalidRoleId1.ToString()));
        Assert.Contains(errors, e => e.Contains(invalidRoleId2.ToString()));
        _mockUserManager.Verify(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task AssignRolesByIdAsync_DelegatesToAssignRolesAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser"
        };

        var role = new ApplicationRole { Id = roleId, Name = "Admin" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockUserManager
            .Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.AssignRolesByIdAsync(userId, new[] { roleId });

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        
        // Verify that it properly delegates to AssignRolesAsync by checking the underlying operations
        _mockUserManager.Verify(x => x.FindByIdAsync(userId.ToString()), Times.Once);
        _mockUserManager.Verify(x => x.GetRolesAsync(user), Times.Once);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Contains("User"))), Times.Once);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Contains("Admin"))), Times.Once);
        
        // Verify events were published
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.RoleName == "User" && e.IsAssigned == false)), Times.Once);
        _mockEventPublisher.Verify(x => x.PublishAsync(It.Is<UserRoleAssignedEvent>(
            e => e.RoleName == "Admin" && e.IsAssigned == true)), Times.Once);
    }

    [Fact]
    public async Task ValidatePasswordStrength_WithWeakPassword_ShouldFail()
    {
        // Arrange
        var createDto = new CreateUserDto
        {
            Email = "test@example.com",
            UserName = "testuser",
            Password = "123" // Weak password
        };

        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordTooShort", Description = "Password too short" }));

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var result = await sut.CreateUserAsync(createDto);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Password too short", result.Errors);
    }

    [Fact]
    public async Task UpdateUserLastLogin_ShouldUpdateTimestamp()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            LastLoginDate = null
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        await sut.UpdateLastLoginAsync(userId);

        // Assert
        Assert.NotNull(user.LastLoginDate);
        Assert.True((DateTime.UtcNow - user.LastLoginDate!.Value).TotalMinutes < 5);
        _mockUserManager.Verify(x => x.UpdateAsync(It.Is<ApplicationUser>(u => u.Id == userId && u.LastLoginDate != null)), Times.Once);
    }

    [Fact]
    public async Task CreateUser_WithAuditFields_ShouldSetCreatedBy()
    {
        // Arrange
        var adminUserId = Guid.NewGuid();
        var createDto = new CreateUserDto
        {
            Email = "test@example.com",
            UserName = "testuser",
            Password = "Test@123456"
        };

        ApplicationUser? createdUser = null;
        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((u, p) => createdUser = u)
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var result = await sut.CreateUserAsync(createDto, adminUserId);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(createdUser);
        Assert.Equal(adminUserId, createdUser!.CreatedBy);
        Assert.True((DateTime.UtcNow - createdUser.CreatedAt).TotalMinutes < 5);
    }

    [Fact]
    public async Task ReactivateUser_ShouldSetIsActiveTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = "u@example.com", IsActive = false };
        _mockUserManager.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _mockUserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object, _mockEventPublisher.Object, CreateInMemoryContext());

        // Act
        var (success, errors) = await sut.ReactivateUserAsync(userId);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        Assert.True(user.IsActive);
        _mockUserManager.Verify(x => x.UpdateAsync(It.Is<ApplicationUser>(u => u.Id == userId && u.IsActive == true)), Times.Once);
    }

    private (UserManager<ApplicationUser>, RoleManager<ApplicationRole>) CreateRealUserAndRoleManager(ApplicationDbContext context)
    {
        var userStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context);
        var roleStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.RoleStore<ApplicationRole, ApplicationDbContext, Guid>(context);

        var identityOptions = Microsoft.Extensions.Options.Options.Create(new IdentityOptions());
        var userManager = new UserManager<ApplicationUser>(
            userStore,
            identityOptions,
            new PasswordHasher<ApplicationUser>(),
            new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() },
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            new Mock<Microsoft.Extensions.Logging.ILogger<UserManager<ApplicationUser>>>().Object);

        var roleManager = new RoleManager<ApplicationRole>(
            roleStore,
            Array.Empty<IRoleValidator<ApplicationRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<Microsoft.Extensions.Logging.ILogger<RoleManager<ApplicationRole>>>().Object);

        return (userManager, roleManager);
    }
}

