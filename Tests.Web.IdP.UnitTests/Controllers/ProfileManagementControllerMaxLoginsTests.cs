using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Web.IdP.Controllers.Api;
using Web.IdP.Options;
using Xunit;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class ProfileManagementControllerMaxLoginsTests : IDisposable
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
    private readonly Mock<IPasskeyService> _mockPasskeyService;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<ILogger<ProfileManagementController>> _mockLogger;
    private readonly ApplicationUser _testUser;

    public ProfileManagementControllerMaxLoginsTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        // Mock UserManager
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null, null, null, null, null, null, null, null);

        // Mock SignInManager
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _mockUserManager.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            null, null, null, null);

        // Mock services
        _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
        _mockPasskeyService = new Mock<IPasskeyService>();
        _mockAuditService = new Mock<IAuditService>();
        _mockLogger = new Mock<ILogger<ProfileManagementController>>();

        // Create test user
        _testUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com",
            EmailConfirmed = true
        };
    }

    [Fact]
    public async Task GetProfile_WhenProviderReachedLimit_ShouldNotIncludeInAvailableProviders()
    {
        // Arrange
        var externalLoginOptions = Options.Create(new ExternalLoginOptions
        {
            MaxLoginsPerProvider = 2 // Limit to 2
        });

        var controller = new ProfileManagementController(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _dbContext,
            _mockSecurityPolicyService.Object,
            _mockPasskeyService.Object,
            _mockAuditService.Object,
            _mockLogger.Object,
            externalLoginOptions);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _testUser.Id.ToString())
        }));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Setup: User has 2 Google accounts (reached limit)
        var existingLogins = new List<UserLoginInfo>
        {
            new UserLoginInfo("Google", "key1", "Google"),
            new UserLoginInfo("Google", "key2", "Google")
        };

        // Setup: Available schemes include Google and Microsoft
        var availableSchemes = new List<AuthenticationScheme>
        {
            new AuthenticationScheme("Google", "Google", typeof(IAuthenticationHandler)),
            new AuthenticationScheme("Microsoft", "Microsoft", typeof(IAuthenticationHandler))
        };

        _mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(x => x.HasPasswordAsync(_testUser))
            .ReturnsAsync(true);
        _mockUserManager.Setup(x => x.GetLoginsAsync(_testUser))
            .ReturnsAsync(existingLogins);
        _mockSignInManager.Setup(x => x.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(availableSchemes);
        _mockSecurityPolicyService.Setup(x => x.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicyDto { AllowSelfPasswordChange = true });
        _mockPasskeyService.Setup(x => x.GetUserPasskeysAsync(_testUser.Id))
            .ReturnsAsync(new List<PasskeyDto>());

        // Act
        var result = await controller.GetProfile();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);

        // Google should NOT be in available providers (reached limit of 2)
        Assert.DoesNotContain(profile.AvailableProviders, p => p.Scheme == "Google");

        // Microsoft should be in available providers (0 < 2)
        Assert.Contains(profile.AvailableProviders, p => p.Scheme == "Microsoft");
    }

    [Fact]
    public async Task GetProfile_WhenUnderLimit_ShouldIncludeInAvailableProviders()
    {
        // Arrange
        var externalLoginOptions = Options.Create(new ExternalLoginOptions
        {
            MaxLoginsPerProvider = 2 // Limit to 2
        });

        var controller = new ProfileManagementController(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _dbContext,
            _mockSecurityPolicyService.Object,
            _mockPasskeyService.Object,
            _mockAuditService.Object,
            _mockLogger.Object,
            externalLoginOptions);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _testUser.Id.ToString())
        }));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Setup: User has 1 Google account (under limit)
        var existingLogins = new List<UserLoginInfo>
        {
            new UserLoginInfo("Google", "key1", "Google")
        };

        var availableSchemes = new List<AuthenticationScheme>
        {
            new AuthenticationScheme("Google", "Google", typeof(IAuthenticationHandler)),
            new AuthenticationScheme("Microsoft", "Microsoft", typeof(IAuthenticationHandler))
        };

        _mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(x => x.HasPasswordAsync(_testUser))
            .ReturnsAsync(true);
        _mockUserManager.Setup(x => x.GetLoginsAsync(_testUser))
            .ReturnsAsync(existingLogins);
        _mockSignInManager.Setup(x => x.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(availableSchemes);
        _mockSecurityPolicyService.Setup(x => x.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicyDto { AllowSelfPasswordChange = true });
        _mockPasskeyService.Setup(x => x.GetUserPasskeysAsync(_testUser.Id))
            .ReturnsAsync(new List<PasskeyDto>());

        // Act
        var result = await controller.GetProfile();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);

        // Both Google (1 < 2) and Microsoft (0 < 2) should be available
        Assert.Contains(profile.AvailableProviders, p => p.Scheme == "Google");
        Assert.Contains(profile.AvailableProviders, p => p.Scheme == "Microsoft");
    }

    [Fact]
    public async Task GetProfile_WhenMaxLoginsPerProviderIsZero_ShouldAllowUnlimited()
    {
        // Arrange
        var externalLoginOptions = Options.Create(new ExternalLoginOptions
        {
            MaxLoginsPerProvider = 0 // Unlimited
        });

        var controller = new ProfileManagementController(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _dbContext,
            _mockSecurityPolicyService.Object,
            _mockPasskeyService.Object,
            _mockAuditService.Object,
            _mockLogger.Object,
            externalLoginOptions);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _testUser.Id.ToString())
        }));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Setup: User has 5 Google accounts (would exceed normal limit)
        var existingLogins = new List<UserLoginInfo>
        {
            new UserLoginInfo("Google", "key1", "Google"),
            new UserLoginInfo("Google", "key2", "Google"),
            new UserLoginInfo("Google", "key3", "Google"),
            new UserLoginInfo("Google", "key4", "Google"),
            new UserLoginInfo("Google", "key5", "Google")
        };

        var availableSchemes = new List<AuthenticationScheme>
        {
            new AuthenticationScheme("Google", "Google", typeof(IAuthenticationHandler))
        };

        _mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(x => x.HasPasswordAsync(_testUser))
            .ReturnsAsync(true);
        _mockUserManager.Setup(x => x.GetLoginsAsync(_testUser))
            .ReturnsAsync(existingLogins);
        _mockSignInManager.Setup(x => x.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(availableSchemes);
        _mockSecurityPolicyService.Setup(x => x.GetCurrentPolicyAsync())
            .ReturnsAsync(new SecurityPolicyDto { AllowSelfPasswordChange = true });
        _mockPasskeyService.Setup(x => x.GetUserPasskeysAsync(_testUser.Id))
            .ReturnsAsync(new List<PasskeyDto>());

        // Act
        var result = await controller.GetProfile();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);

        // Google should still be available (unlimited)
        Assert.Contains(profile.AvailableProviders, p => p.Scheme == "Google");
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}
