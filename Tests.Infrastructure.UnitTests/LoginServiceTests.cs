using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests
{
    public class LoginServiceTests : IDisposable
    {
        private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
        private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
        private readonly Mock<ILegacyAuthService> _legacyAuthServiceMock;
        private readonly Mock<IJitProvisioningService> _jitProvisioningServiceMock;
        private readonly Mock<ILogger<LoginService>> _loggerMock;
        private readonly Mock<IOptions<Core.Application.Options.ExternalLoginOptions>> _externalLoginOptionsMock;
        private readonly ApplicationDbContext _dbContext;
        private LoginService _service;

        public LoginServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new ApplicationDbContext(options);

            var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
            _userManagerMock = new Mock<UserManager<ApplicationUser>>(userStoreMock.Object, null, null, null, null, null, null, null, null);
            
            _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
            _legacyAuthServiceMock = new Mock<ILegacyAuthService>();
            _jitProvisioningServiceMock = new Mock<IJitProvisioningService>();
            _loggerMock = new Mock<ILogger<LoginService>>();
            _externalLoginOptionsMock = new Mock<IOptions<Core.Application.Options.ExternalLoginOptions>>();

            // Default Options
            _externalLoginOptionsMock.Setup(x => x.Value).Returns(new Core.Application.Options.ExternalLoginOptions());

            // Default Policy
            _securityPolicyServiceMock.Setup(x => x.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());

            _service = new LoginService(
                _userManagerMock.Object,
                _securityPolicyServiceMock.Object,
                _legacyAuthServiceMock.Object,
                _jitProvisioningServiceMock.Object,
                _dbContext,
                _loggerMock.Object,
                _externalLoginOptionsMock.Object
            );
        }

        public void Dispose()
        {
            _dbContext.Database.EnsureDeleted();
            _dbContext.Dispose();
        }

        [Fact]
        public async Task AuthenticateAsync_ShouldCopyPersonLocaleToUser_WhenLinkedAndActive()
        {
            // Arrange
            var personId = Guid.NewGuid();
            var user = new ApplicationUser 
            { 
                UserName = "testuser", 
                Email = "test@example.com",
                PersonId = personId,
                Locale = null // User doesn't have Locale set
            };
            
            var person = new Person 
            { 
                Id = personId, 
                Status = PersonStatus.Active, 
                Locale = "zh-TW" 
            };
            _dbContext.Persons.Add(person);
            await _dbContext.SaveChangesAsync();

            // Setup UserManager
            _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
            _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
            _userManagerMock.Setup(x => x.ResetAccessFailedCountAsync(user)).ReturnsAsync(IdentityResult.Success);

            // Act
            var result = await _service.AuthenticateAsync(user.Email, "password");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.User);
            // New behavior: Locale is copied to User instead of attaching Person (avoids tracking conflicts)
            Assert.Equal("zh-TW", result.User.Locale);
        }

        [Fact]
        public async Task AuthenticateAsync_ShouldReturnUserInactive_WhenUserIsDeactivated()
        {
            // Arrange
            var user = new ApplicationUser 
            { 
                UserName = "deactivated", 
                Email = "deactivated@example.com",
                IsActive = false // User is deactivated
            };

            // Setup UserManager
            _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);

            // Act
            var result = await _service.AuthenticateAsync(user.Email, "anypassword");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(Core.Application.DTOs.LoginStatus.UserInactive, result.Status);
            Assert.Null(result.User);
            
            // Verify password is never checked for inactive user
            _userManagerMock.Verify(x => x.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ValidateExternalUserSignInAsync_ShouldReturnUserInactive_WhenUserIsDeleted()
        {
            var user = new ApplicationUser
            {
                UserName = "deleted",
                IsActive = true,
                IsDeleted = true
            };

            var result = await _service.ValidateExternalUserSignInAsync(user);

            Assert.False(result.IsSuccess);
            Assert.Equal(Core.Application.DTOs.LoginStatus.UserInactive, result.Status);
            _userManagerMock.Verify(
                manager => manager.IsLockedOutAsync(It.IsAny<ApplicationUser>()),
                Times.Never);
        }

        [Fact]
        public async Task ValidateExternalUserSignInAsync_ShouldReturnPersonInactive_WhenPersonIsSuspended()
        {
            var person = new Person
            {
                Id = Guid.NewGuid(),
                Status = PersonStatus.Suspended
            };
            var user = new ApplicationUser
            {
                UserName = "suspended-person",
                IsActive = true,
                PersonId = person.Id
            };
            _dbContext.Persons.Add(person);
            await _dbContext.SaveChangesAsync();

            var result = await _service.ValidateExternalUserSignInAsync(user);

            Assert.False(result.IsSuccess);
            Assert.Equal(Core.Application.DTOs.LoginStatus.PersonInactive, result.Status);
            _userManagerMock.Verify(
                manager => manager.IsLockedOutAsync(It.IsAny<ApplicationUser>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_ShouldSucceed_WhenUserIsActive()
        {
            // Arrange
            var user = new ApplicationUser 
            { 
                UserName = "activeuser", 
                Email = "active@example.com",
                IsActive = true // User is active
            };

            // Setup UserManager
            _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
            _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
            _userManagerMock.Setup(x => x.ResetAccessFailedCountAsync(user)).ReturnsAsync(IdentityResult.Success);

            // Act
            var result = await _service.AuthenticateAsync(user.Email, "password");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.User);
        }

        [Fact]
        public async Task CanLinkExternalLoginAsync_ShouldReturnTrue_WhenLimitDisabled()
        {
            // Arrange
            var user = new ApplicationUser();
            _externalLoginOptionsMock.Setup(x => x.Value).Returns(new Core.Application.Options.ExternalLoginOptions { MaxLoginsPerProvider = 0 });
            
            // Recreate service to pick up new options
            _service = new LoginService(
                _userManagerMock.Object,
                _securityPolicyServiceMock.Object,
                _legacyAuthServiceMock.Object,
                _jitProvisioningServiceMock.Object,
                _dbContext,
                _loggerMock.Object,
                _externalLoginOptionsMock.Object
            );

            // Act
            var result = await _service.CanLinkExternalLoginAsync(user, "Google");

            // Assert
            Assert.True(result.Succeeded);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task CanLinkExternalLoginAsync_ShouldReturnTrue_WhenLimitNotReached()
        {
            // Arrange
            var user = new ApplicationUser();
            _externalLoginOptionsMock.Setup(x => x.Value).Returns(new Core.Application.Options.ExternalLoginOptions { MaxLoginsPerProvider = 2 });

            // Recreate service to pick up new options
            _service = new LoginService(
                _userManagerMock.Object,
                _securityPolicyServiceMock.Object,
                _legacyAuthServiceMock.Object,
                _jitProvisioningServiceMock.Object,
                _dbContext,
                _loggerMock.Object,
                _externalLoginOptionsMock.Object
            );

            var existingLogins = new List<UserLoginInfo>
            {
                new UserLoginInfo("Google", "key1", "Google")
            };
            _userManagerMock.Setup(x => x.GetLoginsAsync(user)).ReturnsAsync(existingLogins);

            // Act
            var result = await _service.CanLinkExternalLoginAsync(user, "Google");

            // Assert
            Assert.True(result.Succeeded);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task CanLinkExternalLoginAsync_ShouldReturnFalse_WhenLimitReached()
        {
            // Arrange
            var user = new ApplicationUser();
            _externalLoginOptionsMock.Setup(x => x.Value).Returns(new Core.Application.Options.ExternalLoginOptions { MaxLoginsPerProvider = 2 });

            // Recreate service to pick up new options
            _service = new LoginService(
                _userManagerMock.Object,
                _securityPolicyServiceMock.Object,
                _legacyAuthServiceMock.Object,
                _jitProvisioningServiceMock.Object,
                _dbContext,
                _loggerMock.Object,
                _externalLoginOptionsMock.Object
            );

            var existingLogins = new List<UserLoginInfo>
            {
                new UserLoginInfo("Google", "key1", "Google"),
                new UserLoginInfo("Google", "key2", "Google")
            };
            _userManagerMock.Setup(x => x.GetLoginsAsync(user)).ReturnsAsync(existingLogins);

            // Act
            var result = await _service.CanLinkExternalLoginAsync(user, "Google");

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains("maximum number of linked accounts", result.Error);
        }
    }
}
