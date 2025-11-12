using Core.Application.DTOs;
using Core.Domain;
using Infrastructure.Services;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Tests.Application.UnitTests;

public class UserManagementTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);
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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

        // Act
        var result = await sut.CreateUserAsync(createDto);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.UserId);
        Assert.Contains("Email already exists", result.Errors);
    }

    [Fact]
    public async Task UpdateUser_WithValidData_ShouldSucceed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingUser = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser",
            FirstName = "Test",
            LastName = "User"
        };

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

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(existingUser);

        _mockUserManager
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string> { "User" });

        _mockUserManager
            .Setup(x => x.RemoveFromRolesAsync(existingUser, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.AddToRolesAsync(existingUser, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

        // Act
        var (success, errors) = await sut.UpdateUserAsync(userId, updateDto);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        Assert.Equal("Updated", existingUser.FirstName);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(existingUser, It.Is<IEnumerable<string>>(r => r.Single() == "User")), Times.Once);
        _mockUserManager.Verify(x => x.AddToRolesAsync(existingUser, It.Is<IEnumerable<string>>(r => r.Single() == "Admin")), Times.Once);
    }

    [Fact]
    public async Task UpdateUser_NonExistent_ShouldFail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var updateDto = new UpdateUserDto
        {
            Email = "test@example.com",
            UserName = "testuser",
            Roles = new List<string>()
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

        // Act
        var (success, errors) = await sut.UpdateUserAsync(userId, updateDto);

        // Assert
        Assert.False(success);
        Assert.Contains("User not found", errors);
    }

    [Fact]
    public async Task GetUser_ById_ShouldReturnUserDetail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "testuser",
            FirstName = "Test",
            LastName = "User",
            Department = "IT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

        // Act
        var detail = await sut.GetUserByIdAsync(userId);

        // Assert
        Assert.NotNull(detail);
        Assert.Equal(userId, detail!.Id);
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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

        // Act
        var (success, errors) = await sut.AssignRolesAsync(userId, roles);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        _mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()), Times.Never);
        _mockUserManager.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Single() == "Admin")), Times.Once);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

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

        var sut = new UserManagementService(_mockUserManager.Object, _mockRoleManager.Object);

        // Act
        var (success, errors) = await sut.ReactivateUserAsync(userId);

        // Assert
        Assert.True(success);
        Assert.Empty(errors);
        Assert.True(user.IsActive);
        _mockUserManager.Verify(x => x.UpdateAsync(It.Is<ApplicationUser>(u => u.Id == userId && u.IsActive == true)), Times.Once);
    }
}
