using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Tasks;
using Xunit;
using Core.Application.Utilities;

namespace Tests.Application.UnitTests;

/// <summary>
/// Unit tests for LoginService
/// Phase 18: Added Person lifecycle validation tests
/// </summary>
public class LoginServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
    private readonly Mock<ILegacyAuthService> _mockLegacyAuthService;
    private readonly Mock<IJitProvisioningService> _mockJitProvisioningService;
    private readonly Mock<IApplicationDbContext> _mockDbContext;
    private readonly Mock<ILogger<LoginService>> _mockLogger;
    private readonly LoginService _loginService;
    private readonly List<Person> _persons;

    public LoginServiceTests()
    {
        // Mock UserManager
        var store = new Mock<IUserStore<ApplicationUser>>();
        var options = new Mock<IOptions<IdentityOptions>>();
        var hasher = new Mock<IPasswordHasher<ApplicationUser>>();
        var userValidators = new List<IUserValidator<ApplicationUser>>();
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>>();
        var normalizer = new Mock<ILookupNormalizer>();
        var errors = new IdentityErrorDescriber();
        var services = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<UserManager<ApplicationUser>>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, options.Object, hasher.Object, userValidators, passwordValidators, normalizer.Object, errors, services.Object, logger.Object);

        _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
        _mockLegacyAuthService = new Mock<ILegacyAuthService>();
        _mockJitProvisioningService = new Mock<IJitProvisioningService>();
        _mockLogger = new Mock<ILogger<LoginService>>();

        // Setup mock DbContext with Persons DbSet
        _mockDbContext = new Mock<IApplicationDbContext>();
        _persons = new List<Person>();
        var mockPersonsDbSet = CreateMockDbSet(_persons);
        _mockDbContext.Setup(db => db.Persons).Returns(mockPersonsDbSet.Object);

        _loginService = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            _mockJitProvisioningService.Object,
            _mockDbContext.Object,
            _mockLogger.Object
        );
    }

    private static Mock<DbSet<T>> CreateMockDbSet<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mockSet = new Mock<DbSet<T>>();
        mockSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mockSet.As<IAsyncEnumerable<T>>().Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));
        return mockSet;
    }

    private void SetupDefaultPolicy(int maxAttempts = 5)
    {
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { MaxFailedAccessAttempts = maxAttempts, LockoutDurationMinutes = 15 });
    }

    #region Existing tests (no Person linked)

    [Fact]
    public async Task AuthenticateAsync_LocalUser_CorrectPassword_ReturnsSuccess()
    {
        // Arrange - user without PersonId
        var user = new ApplicationUser { UserName = "test", PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("test", "password");

        // Assert
        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(user, result.User);
        _mockUserManager.Verify(um => um.ResetAccessFailedCountAsync(user), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_IncorrectPassword_ReturnsInvalidCredentials()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test", AccessFailedCount = 1, PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);
        SetupDefaultPolicy(maxAttempts: 5);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "wrong");

        // Assert
        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(um => um.AccessFailedAsync(user), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_IncorrectPassword_TriggersLockout_ReturnsLockedOut()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test", AccessFailedCount = 4, PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.GetAccessFailedCountAsync(user)).ReturnsAsync(5);
        SetupDefaultPolicy(maxAttempts: 5);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "wrong");

        // Assert
        Assert.Equal(LoginStatus.LockedOut, result.Status);
        _mockUserManager.Verify(um => um.AccessFailedAsync(user), Times.Once);
        _mockUserManager.Verify(um => um.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_AlreadyLockedOut_ReturnsLockedOut()
    {
        // Arrange - user without PersonId bypasses Person check
        var user = new ApplicationUser { UserName = "test", PersonId = null };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(true);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "any_password");

        // Assert
        Assert.Equal(LoginStatus.LockedOut, result.Status);
        _mockUserManager.Verify(um => um.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_LegacyUser_Success_ReturnsLegacySuccess()
    {
        // Arrange
        var provisionedUser = new ApplicationUser 
        { 
            UserName = "legacy",
            PersonId = null // No Person linked
        };
        _mockUserManager.Setup(um => um.FindByEmailAsync("legacy")).ReturnsAsync((ApplicationUser?)null);
        _mockLegacyAuthService.Setup(las => las.ValidateAsync("legacy", "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegacyUserDto { IsAuthenticated = true });
        _mockJitProvisioningService.Setup(jps => jps.ProvisionExternalUserAsync(It.IsAny<ExternalAuthResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provisionedUser);

        // Act
        var result = await _loginService.AuthenticateAsync("legacy", "password");

        // Assert
        Assert.Equal(LoginStatus.LegacySuccess, result.Status);
        Assert.Equal(provisionedUser, result.User);
    }

    [Fact]
    public async Task AuthenticateAsync_NonExistentUser_ReturnsInvalidCredentials()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByEmailAsync("nobody")).ReturnsAsync((ApplicationUser?)null);
        _mockLegacyAuthService.Setup(las => las.ValidateAsync("nobody", "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegacyUserDto { IsAuthenticated = false });

        // Act
        var result = await _loginService.AuthenticateAsync("nobody", "password");

        // Assert
        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
    }

    #endregion

    #region Phase 18: Person Lifecycle Validation Tests

    [Fact]
    public async Task AuthenticateAsync_LocalUser_ActivePerson_ReturnsSuccess()
    {
        // Arrange - user linked to an active Person
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = false 
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "active.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("active.user")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("active.user", "password");

        // Assert
        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(user, result.User);
    }

    [Theory]
    [InlineData(PersonStatus.Pending, "Person status is Pending")]
    [InlineData(PersonStatus.Suspended, "Person status is Suspended")]
    [InlineData(PersonStatus.Resigned, "Person status is Resigned")]
    [InlineData(PersonStatus.Terminated, "Person status is Terminated")]
    public async Task AuthenticateAsync_LocalUser_InactivePerson_ReturnsPersonInactive(PersonStatus status, string expectedMessage)
    {
        // Arrange - user linked to an inactive Person
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = status, 
            IsDeleted = false 
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "inactive.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("inactive.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("inactive.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains(expectedMessage, result.Message);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_DeletedPerson_ReturnsPersonInactive()
    {
        // Arrange - user linked to a soft-deleted Person
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow 
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "deleted.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("deleted.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("deleted.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("deleted", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_FutureStartDate_ReturnsPersonInactive()
    {
        // Arrange - user linked to a Person with future start date
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = false,
            StartDate = DateTime.UtcNow.AddDays(7) // Starts next week
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "future.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("future.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("future.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("not started", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_PastEndDate_ReturnsPersonInactive()
    {
        // Arrange - user linked to a Person with past end date
        var personId = Guid.NewGuid();
        var person = new Person 
        { 
            Id = personId, 
            Status = PersonStatus.Active, 
            IsDeleted = false,
            EndDate = DateTime.UtcNow.AddDays(-1) // Ended yesterday
        };
        _persons.Add(person);

        var user = new ApplicationUser { UserName = "expired.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("expired.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("expired.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("ended", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_PersonNotFound_ReturnsPersonInactive()
    {
        // Arrange - user linked to a deleted/non-existent Person
        var personId = Guid.NewGuid();
        // Don't add person to _persons list - simulating deleted or non-existent

        var user = new ApplicationUser { UserName = "orphan.user", PersonId = personId };
        _mockUserManager.Setup(um => um.FindByEmailAsync("orphan.user")).ReturnsAsync(user);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("orphan.user", "password");

        // Assert
        Assert.Equal(LoginStatus.PersonInactive, result.Status);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
