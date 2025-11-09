using Xunit;
using Moq;
using Microsoft.AspNetCore.Identity;
using Core.Domain;
using System.Threading.Tasks;
using HybridIdP.Infrastructure.Identity; // Assuming DynamicPasswordValidator will be here
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using Core.Application; // For ISecurityPolicyService
using Core.Domain.Entities; // For SecurityPolicy

namespace Tests.Application.UnitTests
{
    public class DynamicPasswordValidatorTests
    {
        private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
        private readonly Mock<ILogger<DynamicPasswordValidator>> _mockLogger;
        private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;

        public DynamicPasswordValidatorTests()
        {
            // Mock UserManager
            var store = new Mock<IUserStore<ApplicationUser>>();
            _mockUserManager = new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);

            // Mock ILogger
            _mockLogger = new Mock<ILogger<DynamicPasswordValidator>>();

            // Mock ISecurityPolicyService
            _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
            // Setup a default policy for tests
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy
                {
                    MinPasswordLength = 6,
                    RequireUppercase = true,
                    RequireLowercase = true,
                    RequireDigit = true,
                    RequireNonAlphanumeric = true
                });
        }

        private DynamicPasswordValidator CreateValidator()
        {
            return new DynamicPasswordValidator(_mockSecurityPolicyService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task ValidateAsync_PasswordTooShort_ReturnsFailed()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { MinPasswordLength = 10 });

            var validator = CreateValidator();
            var user = new ApplicationUser { UserName = "testuser" };
            var password = "short"; // Length 5

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordTooShort));
        }

        [Fact]
        public async Task ValidateAsync_PasswordRequiresNonAlphanumeric_ReturnsFailed()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { RequireNonAlphanumeric = true, MinPasswordLength = 1 });

            var validator = CreateValidator();
            var user = new ApplicationUser { UserName = "testuser" };
            var password = "Password123"; // No non-alphanumeric

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric));
        }

        [Fact]
        public async Task ValidateAsync_PasswordRequiresDigit_ReturnsFailed()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { RequireDigit = true, MinPasswordLength = 1 });

            var validator = CreateValidator();
            var user = new ApplicationUser { UserName = "testuser" };
            var password = "Password!"; // No digit

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordRequiresDigit));
        }

        [Fact]
        public async Task ValidateAsync_PasswordRequiresLower_ReturnsFailed()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { RequireLowercase = true, MinPasswordLength = 1 });

            var validator = CreateValidator();
            var user = new ApplicationUser { UserName = "testuser" };
            var password = "PASSWORD123!"; // No lowercase

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordRequiresLower));
        }

        [Fact]
        public async Task ValidateAsync_PasswordRequiresUpper_ReturnsFailed()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { RequireUppercase = true, MinPasswordLength = 1 });

            var validator = CreateValidator();
            var user = new ApplicationUser { UserName = "testuser" };
            var password = "password123!"; // No uppercase

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordRequiresUpper));
        }

        [Fact]
        public async Task ValidateAsync_ValidPassword_ReturnsSucceeded()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy
                {
                    MinPasswordLength = 8,
                    RequireNonAlphanumeric = true,
                    RequireDigit = true,
                    RequireLowercase = true,
                    RequireUppercase = true
                });

            var validator = CreateValidator();
            var user = new ApplicationUser { UserName = "testuser" };
            var password = "ValidPassword1!";

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task ValidateAsync_PasswordReuse_ReturnsFailed()
        {
            // Arrange
            var oldPassword = "OldPassword1!";
            var newPassword = "OldPassword1!"; // Attempt to reuse

            var passwordHasher = new PasswordHasher<ApplicationUser>();
            var hashedOldPassword = passwordHasher.HashPassword(new ApplicationUser(), oldPassword);

            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { PasswordHistoryCount = 1, MinPasswordLength = 1 });

            var user = new ApplicationUser
            {
                UserName = "testuser",
                PasswordHash = hashedOldPassword, // Simulate current password
                PasswordHistory = System.Text.Json.JsonSerializer.Serialize(new List<string> { /* no history yet */ })
            };

            var validator = CreateValidator();

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, newPassword);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == "PasswordReuse");
        }

        [Fact]
        public async Task ValidateAsync_PasswordExpired_ReturnsFailed()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy { PasswordExpirationDays = 30, MinPasswordLength = 1 });

            var validator = CreateValidator();
            var user = new ApplicationUser
            {
                UserName = "testuser",
                LastPasswordChangeDate = DateTime.UtcNow.AddDays(-31) // Password expired 1 day ago
            };
            var password = "NewValidPassword1!";

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == "PasswordExpired");
        }

        [Fact]
        public async Task ValidateAsync_PasswordNotExpired_ReturnsSucceeded()
        {
            // Arrange
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy
                {
                    PasswordExpirationDays = 30,
                    MinPasswordLength = 8,
                    RequireNonAlphanumeric = true,
                    RequireDigit = true,
                    RequireLowercase = true,
                    RequireUppercase = true
                });

            var validator = CreateValidator();
            var user = new ApplicationUser
            {
                UserName = "testuser",
                LastPasswordChangeDate = DateTime.UtcNow.AddDays(-15) // Not expired
            };
            var password = "NewValidPassword1!";

            // Act
            var result = await validator.ValidateAsync(_mockUserManager.Object, user, password);

            // Assert
            Assert.True(result.Succeeded);
        }
    }
}
