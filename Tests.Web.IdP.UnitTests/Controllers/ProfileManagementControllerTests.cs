using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP.Controllers;
using Web.IdP.Controllers.Api;
using Xunit;
using Microsoft.Extensions.Options;
using Web.IdP.Options;
using Microsoft.AspNetCore.Authentication;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class ProfileManagementControllerTests : IDisposable
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
    private readonly Mock<IPasskeyService> _mockPasskeyService;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<ILogger<ProfileManagementController>> _mockLogger;
    private readonly ProfileManagementController _controller;
    private readonly ApplicationUser _testUser;

    public ProfileManagementControllerTests()
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
            _mockUserManager.Object, contextAccessor.Object, claimsFactory.Object,
            null, null, null, null);

        // Mock other services
        _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
        _mockPasskeyService = new Mock<IPasskeyService>();
        _mockPasskeyService.Setup(x => x.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserCredentialDto>()); // Default: no passkeys
        _mockAuditService = new Mock<IAuditService>();
        _mockLogger = new Mock<ILogger<ProfileManagementController>>();

        // Create test user
        _testUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser@example.com",
            Email = "testuser@example.com",
            EmailConfirmed = true
        };

        // Setup controller with mocked User context
        var externalLoginOptions = Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions
        {
            MaxLoginsPerProvider = 2 // Default for tests
        });

        _controller = new ProfileManagementController(
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
            new Claim(ClaimTypes.NameIdentifier, _testUser.Id.ToString()),
            new Claim(ClaimTypes.Name, _testUser.UserName)
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    #region GetProfile Tests

    [Fact]
    public async Task GetProfile_UserNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser)null);

        // Act
        var result = await _controller.GetProfile();

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Contains("User not found", notFoundResult.Value.ToString());
    }

    [Fact]
    public async Task GetProfile_WithoutPerson_ReturnsBasicProfile()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.HasPasswordAsync(_testUser))
            .ReturnsAsync(true);
        _mockUserManager.Setup(m => m.GetLoginsAsync(_testUser))
            .ReturnsAsync(new List<UserLoginInfo>());

        var policy = new SecurityPolicy { AllowSelfPasswordChange = true };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        // Act
        var result = await _controller.GetProfile();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);
        Assert.Equal(_testUser.Id, profile.UserId);
        Assert.Equal(_testUser.UserName, profile.UserName);
        Assert.True(profile.HasLocalPassword);
        Assert.True(profile.AllowPasswordChange);
        Assert.Null(profile.Person);
    }

    [Fact]
    public async Task GetProfile_WithPerson_ReturnsFullProfile()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "+886-912-345-678",
            Locale = "zh-TW",
            TimeZone = "Asia/Taipei",
            EmployeeId = "EMP001",
            Department = "IT",
            JobTitle = "Developer"
        };
        _dbContext.Persons.Add(person);
        await _dbContext.SaveChangesAsync();

        _testUser.PersonId = person.Id;
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.HasPasswordAsync(_testUser))
            .ReturnsAsync(true);
        _mockUserManager.Setup(m => m.GetLoginsAsync(_testUser))
            .ReturnsAsync(new List<UserLoginInfo>());

        var policy = new SecurityPolicy { AllowSelfPasswordChange = true };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        // Act
        var result = await _controller.GetProfile();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);
        Assert.NotNull(profile.Person);
        Assert.Equal("Test User", profile.Person.FullName);
        Assert.Equal("+886-912-345-678", profile.Person.PhoneNumber);
    }

    [Fact]
    public async Task GetProfile_ExternalLogin_DisallowsPasswordChange()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.HasPasswordAsync(_testUser))
            .ReturnsAsync(false); // External login has no local password
        _mockUserManager.Setup(m => m.GetLoginsAsync(_testUser))
            .ReturnsAsync(new List<UserLoginInfo>
            {
                new UserLoginInfo("Google", "google-id", "Google")
            });

        var policy = new SecurityPolicy { AllowSelfPasswordChange = true };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        // Act
        var result = await _controller.GetProfile();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);
        Assert.False(profile.HasLocalPassword);
        Assert.False(profile.AllowPasswordChange); // Even though policy allows, no local password
        Assert.Single(profile.ExternalLogins);
    }

    [Fact]
    public async Task GetProfile_WithExternalLogin_ReturnsAvailableProviders()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.HasPasswordAsync(_testUser))
            .ReturnsAsync(false);
        _mockUserManager.Setup(m => m.GetLoginsAsync(_testUser))
            .ReturnsAsync(new List<UserLoginInfo>
            {
                new UserLoginInfo("Google", "google-id", "Google")
            });

        // Mock available authentication schemes
        var schemes = new List<Microsoft.AspNetCore.Authentication.AuthenticationScheme>
        {
            new Microsoft.AspNetCore.Authentication.AuthenticationScheme("Google", "Google", typeof(Microsoft.AspNetCore.Authentication.IAuthenticationHandler)),
            new Microsoft.AspNetCore.Authentication.AuthenticationScheme("Microsoft", "Microsoft", typeof(Microsoft.AspNetCore.Authentication.IAuthenticationHandler)),
            new Microsoft.AspNetCore.Authentication.AuthenticationScheme(Core.Domain.Constants.AuthConstants.Providers.Legacy, "Legacy", typeof(Microsoft.AspNetCore.Authentication.IAuthenticationHandler))
        };
        _mockSignInManager.Setup(s => s.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(schemes);

        var policy = new SecurityPolicy { AllowSelfPasswordChange = true };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        // Act
        var result = await _controller.GetProfile();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);
        
        // Should have 1 available provider (Microsoft)
        // Google is excluded because already linked
        // Legacy is excluded because it's the Legacy provider
        Assert.NotNull(profile.AvailableProviders);
        Assert.Single(profile.AvailableProviders);
        Assert.Equal("Microsoft", profile.AvailableProviders[0].Scheme);
    }

    #endregion

    #region UpdateProfile Tests

    [Fact]
    public async Task UpdateProfile_UserNotLinkedToPerson_ReturnsOk()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser); // PersonId is null

        var request = new UpdateProfileRequest
        {
            PhoneNumber = "+886-987-654-321"
        };

        _mockAuditService.Setup(a => a.LogEventAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.UpdateProfile(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("updated successfully", okResult.Value.ToString());
    }

    [Fact]
    public async Task UpdateProfile_ValidRequest_UpdatesPersonAndReturnsOk()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "+886-912-345-678",
            Locale = "zh-TW",
            TimeZone = "Asia/Taipei"
        };
        _dbContext.Persons.Add(person);
        await _dbContext.SaveChangesAsync();

        _testUser.PersonId = person.Id;
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);

        var request = new UpdateProfileRequest
        {
            PhoneNumber = "+886-987-654-321",
            Locale = "en-US",
            TimeZone = "America/Los_Angeles"
        };

        _mockAuditService.Setup(a => a.LogEventAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.UpdateProfile(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        
        // Verify person was updated
        var updatedPerson = await _dbContext.Persons.FindAsync(person.Id);
        Assert.Equal("+886-987-654-321", updatedPerson.PhoneNumber);
        Assert.Equal("en-US", updatedPerson.Locale);
        Assert.Equal("America/Los_Angeles", updatedPerson.TimeZone);
        Assert.NotNull(updatedPerson.ModifiedAt);
        Assert.Equal(_testUser.Id, updatedPerson.ModifiedBy);

        // Verify audit log was called
        _mockAuditService.Verify(a => a.LogEventAsync(
            "Profile.Update",
            _testUser.Id.ToString(),
            It.IsAny<string>(),
            null,
            null), Times.Once);
    }

    #endregion

    #region ChangePassword Tests

    [Fact]
    public async Task ChangePassword_PolicyDisabled_ReturnsForbidden()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);

        var policy = new SecurityPolicy { AllowSelfPasswordChange = false };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "Old123!",
            NewPassword = "New123!",
            ConfirmPassword = "New123!"
        };

        // Act
        var result = await _controller.ChangePassword(request);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
        Assert.Contains("disabled by system policy", statusResult.Value.ToString());
    }

    [Fact]
    public async Task ChangePassword_ExternalLoginUser_ReturnsBadRequest()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.HasPasswordAsync(_testUser))
            .ReturnsAsync(false); // No local password

        var policy = new SecurityPolicy { AllowSelfPasswordChange = true };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "Old123!",
            NewPassword = "New123!",
            ConfirmPassword = "New123!"
        };

        // Act
        var result = await _controller.ChangePassword(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("external login accounts", badRequestResult.Value.ToString());
    }

    [Fact]
    public async Task ChangePassword_IncorrectCurrentPassword_ReturnsBadRequest()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.HasPasswordAsync(_testUser))
            .ReturnsAsync(true);
        _mockUserManager.Setup(m => m.ChangePasswordAsync(_testUser, "WrongOld123!", "New123!"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordMismatch",
                Description = "Incorrect password"
            }));

        var policy = new SecurityPolicy { AllowSelfPasswordChange = true };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongOld123!",
            NewPassword = "New123!",
            ConfirmPassword = "New123!"
        };

        // Act
        var result = await _controller.ChangePassword(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        // Check that errors are returned
        Assert.Contains("errors", badRequestResult.Value.ToString().ToLower());
    }

    [Fact]
    public async Task ChangePassword_ValidRequest_ChangesPasswordAndReturnsOk()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.HasPasswordAsync(_testUser))
            .ReturnsAsync(true);
        _mockUserManager.Setup(m => m.ChangePasswordAsync(_testUser, "Old123!", "New123!"))
            .ReturnsAsync(IdentityResult.Success);

        var policy = new SecurityPolicy { AllowSelfPasswordChange = true };
        _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(policy);

        _mockAuditService.Setup(a => a.LogEventAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "Old123!",
            NewPassword = "New123!",
            ConfirmPassword = "New123!"
        };

        // Act
        var result = await _controller.ChangePassword(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully", okResult.Value.ToString().ToLower());

        // Verify audit log was called
        _mockAuditService.Verify(a => a.LogEventAsync(
            "Profile.ChangePassword",
            _testUser.Id.ToString(),
            It.IsAny<string>(),
            null,
            null), Times.Once);
    }

    #endregion

    #region RemoveLogin Tests

    [Fact]
    public async Task RemoveLogin_ValidRequest_RemovesLoginAndReturnsOk()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.RemoveLoginAsync(_testUser, "Google", "google-id"))
            .ReturnsAsync(IdentityResult.Success);
        _mockSignInManager.Setup(s => s.RefreshSignInAsync(_testUser))
            .Returns(Task.CompletedTask);

        _mockAuditService.Setup(a => a.LogEventAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var request = new RemoveLoginRequest
        {
            LoginProvider = "Google",
            ProviderKey = "google-id"
        };

        // Act
        var result = await _controller.RemoveLogin(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully", okResult.Value.ToString().ToLower());

        // Verify RemoveLoginAsync was called
        _mockUserManager.Verify(m => m.RemoveLoginAsync(_testUser, "Google", "google-id"), Times.Once);
        
        // Verify RefreshSignInAsync was called
        _mockSignInManager.Verify(s => s.RefreshSignInAsync(_testUser), Times.Once);

        // Verify audit log was called
        _mockAuditService.Verify(a => a.LogEventAsync(
            "Profile.RemoveLogin",
            _testUser.Id.ToString(),
            It.IsAny<string>(),
            null,
            null), Times.Once);
    }

    [Fact]
    public async Task RemoveLogin_FailedToRemove_ReturnsBadRequest()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(_testUser);
        _mockUserManager.Setup(m => m.RemoveLoginAsync(_testUser, "Google", "google-id"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "RemoveFailed",
                Description = "Failed to remove login"
            }));

        var request = new RemoveLoginRequest
        {
            LoginProvider = "Google",
            ProviderKey = "google-id"
        };

        // Act
        var result = await _controller.RemoveLogin(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Failed to remove login", badRequestResult.Value.ToString());

        // Verify RefreshSignInAsync was NOT called
        _mockSignInManager.Verify(s => s.RefreshSignInAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    #endregion

    #region MaxLoginsPerProvider Tests

    [Fact]
    public async Task GetProfile_WhenProviderReachedLimit_ShouldNotIncludeInAvailableProviders()
    {
        // Arrange: User has 2 Google accounts (reached limit)
        var existingLogins = new List<UserLoginInfo>
        {
            new UserLoginInfo("Google", "key1", "Google"),
            new UserLoginInfo("Google", "key2", "Google")
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
            .ReturnsAsync(new SecurityPolicy { AllowSelfPasswordChange = true });

        // Act
        var result = await _controller.GetProfile();

        // Assert
        if (result.Result is not OkObjectResult)
        {
             var error = result.Result as ObjectResult;
             throw new Exception($"Expected OkObjectResult but got {result.Result?.GetType().Name}. Status: {error?.StatusCode}, Value: {error?.Value}");
        }

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);

        // Google should NOT be in available providers (reached limit of 2)
        Assert.DoesNotContain(profile.AvailableProviders, p => p.Scheme == "Google");

        // Microsoft should be in available providers (0 < 2)
        Assert.Contains(profile.AvailableProviders, p => p.Scheme == "Microsoft");
    }

    [Fact]
    public async Task GetProfile_WhenMaxLoginsPerProviderIsZero_ShouldAllowUnlimited()
    {
        // Arrange: Create controller with unlimited setting
        var unlimitedOptions = Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions
        {
            MaxLoginsPerProvider = 0 // Unlimited
        });

        // Re-create dependencies locally to ensure clean state if needed, but reusing mocks is fine mostly
        // Just recreate controller
        var controller = new ProfileManagementController(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _dbContext,
            _mockSecurityPolicyService.Object,
            _mockPasskeyService.Object,
            _mockAuditService.Object,
            _mockLogger.Object,
            unlimitedOptions);

        // Explicitly set context
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _testUser.Id.ToString()),
            new Claim(ClaimTypes.Name, _testUser.UserName)
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "mock"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // User has 5 Google accounts
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
            .ReturnsAsync(new SecurityPolicy { AllowSelfPasswordChange = true });

        // Act
        var result = await controller.GetProfile();

        // Assert
        if (result.Result is not OkObjectResult)
        {
             var error = result.Result as ObjectResult;
             throw new Exception($"Expected OkObjectResult but got {result.Result?.GetType().Name}. Status: {error?.StatusCode}, Value: {error?.Value}");
        }

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<ProfileDto>(okResult.Value);

        // Google should still be available (unlimited)
        Assert.Contains(profile.AvailableProviders, p => p.Scheme == "Google");
    }

    #endregion

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}
