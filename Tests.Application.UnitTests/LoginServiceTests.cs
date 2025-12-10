using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Tasks;
using Xunit;
using Core.Application.Utilities;

namespace Tests.Application.UnitTests;

public class LoginServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
    private readonly Mock<ILegacyAuthService> _mockLegacyAuthService;
    private readonly Mock<IJitProvisioningService> _mockJitProvisioningService;
    private readonly Mock<ILogger<LoginService>> _mockLogger;
    private readonly LoginService _loginService;

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

        _loginService = new LoginService(
            _mockUserManager.Object,
            _mockSecurityPolicyService.Object,
            _mockLegacyAuthService.Object,
            _mockJitProvisioningService.Object,
            _mockLogger.Object
        );
    }

    private void SetupDefaultPolicy(int maxAttempts = 5)
    {
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicy { MaxFailedAccessAttempts = maxAttempts, LockoutDurationMinutes = 15 });
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_CorrectPassword_ReturnsSuccess()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test" };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        SetupDefaultPolicy();

        // Act
        var result = await _loginService.AuthenticateAsync("test", "password");

        // Assert
        Assert.Equal(Core.Application.DTOs.LoginStatus.Success, result.Status);
        Assert.Equal(user, result.User);
        _mockUserManager.Verify(um => um.ResetAccessFailedCountAsync(user), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_IncorrectPassword_ReturnsInvalidCredentials()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test", AccessFailedCount = 1 };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);
        SetupDefaultPolicy(maxAttempts: 5);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "wrong");

        // Assert
        Assert.Equal(Core.Application.DTOs.LoginStatus.InvalidCredentials, result.Status);
        _mockUserManager.Verify(um => um.AccessFailedAsync(user), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_IncorrectPassword_TriggersLockout_ReturnsLockedOut()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test", AccessFailedCount = 4 };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);
        _mockUserManager.Setup(um => um.GetAccessFailedCountAsync(user)).ReturnsAsync(5); // After the failed attempt
        SetupDefaultPolicy(maxAttempts: 5);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "wrong");

        // Assert
        Assert.Equal(Core.Application.DTOs.LoginStatus.LockedOut, result.Status);
        _mockUserManager.Verify(um => um.AccessFailedAsync(user), Times.Once);
        _mockUserManager.Verify(um => um.SetLockoutEndDateAsync(user, It.IsAny<System.DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_LocalUser_AlreadyLockedOut_ReturnsLockedOut()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "test" };
        _mockUserManager.Setup(um => um.FindByEmailAsync("test")).ReturnsAsync(user);
        _mockUserManager.Setup(um => um.IsLockedOutAsync(user)).ReturnsAsync(true);

        // Act
        var result = await _loginService.AuthenticateAsync("test", "any_password");

        // Assert
        Assert.Equal(Core.Application.DTOs.LoginStatus.LockedOut, result.Status);
        _mockUserManager.Verify(um => um.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateAsync_LegacyUser_Success_ReturnsLegacySuccess()
    {
        // Arrange
        var provisionedUser = new ApplicationUser 
        { 
            UserName = "legacy",
            Person = new Person 
            { 
                NationalId = PidHasher.Hash("A123456789") 
            }
        };
        _mockUserManager.Setup(um => um.FindByEmailAsync("legacy")).ReturnsAsync((ApplicationUser)null);
        _mockLegacyAuthService.Setup(las => las.ValidateAsync("legacy", "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Application.DTOs.LegacyUserDto { IsAuthenticated = true });
        _mockJitProvisioningService.Setup(jps => jps.ProvisionExternalUserAsync(It.IsAny<Core.Application.DTOs.ExternalAuthResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provisionedUser);

        // Act
        var result = await _loginService.AuthenticateAsync("legacy", "password");

        // Assert
        Assert.Equal(Core.Application.DTOs.LoginStatus.LegacySuccess, result.Status);
        Assert.Equal(provisionedUser, result.User);
    }

    [Fact]
    public async Task AuthenticateAsync_NonExistentUser_ReturnsInvalidCredentials()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByEmailAsync("nobody")).ReturnsAsync((ApplicationUser)null);
        _mockLegacyAuthService.Setup(las => las.ValidateAsync("nobody", "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Application.DTOs.LegacyUserDto { IsAuthenticated = false });

        // Act
        var result = await _loginService.AuthenticateAsync("nobody", "password");

        // Assert
        Assert.Equal(Core.Application.DTOs.LoginStatus.InvalidCredentials, result.Status);
    }
}
