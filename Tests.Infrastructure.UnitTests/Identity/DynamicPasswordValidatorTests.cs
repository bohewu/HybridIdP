using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using HybridIdP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Text.Json;

namespace Tests.Infrastructure.UnitTests
{
    public class DynamicPasswordValidatorTests
    {
        private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
        private readonly Mock<ILogger<DynamicPasswordValidator>> _loggerMock;
        private readonly DynamicPasswordValidator _validator;
        private readonly SecurityPolicy _defaultPolicy;

        public DynamicPasswordValidatorTests()
        {
            _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
            _loggerMock = new Mock<ILogger<DynamicPasswordValidator>>();
            _validator = new DynamicPasswordValidator(_securityPolicyServiceMock.Object, _loggerMock.Object);

            _defaultPolicy = new SecurityPolicy
            {
                MinPasswordLength = 8,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                PasswordHistoryCount = 0,
                PasswordExpirationDays = 0,
                MinPasswordAgeDays = 0,
                MaxFailedAccessAttempts = 5,
                LockoutDurationMinutes = 15,
                AbnormalLoginHistoryCount = 10,
                BlockAbnormalLogin = false
            };

            _securityPolicyServiceMock.Setup(s => s.GetCurrentPolicyAsync())
                .ReturnsAsync(_defaultPolicy);
        }

        [Fact]
        public async Task ValidateAsync_WithValidPassword_ShouldSucceed()
        {
            // Arrange
            var user = new ApplicationUser();
            var password = "ValidPassword1!";

            // Act
            var result = await _validator.ValidateAsync(null!, user, password);

            // Assert
            Assert.True(result.Succeeded);
        }

        [Theory]
        [InlineData("Short1!", "PasswordTooShort")] // Too Short
        [InlineData("nopassword1!", "PasswordRequiresUpper")] // No Upper
        [InlineData("NOLOWER1!", "PasswordRequiresLower")] // No Lower
        [InlineData("NoDigit!", "PasswordRequiresDigit")] // No Digit
        [InlineData("NoSpecialChar1", "PasswordRequiresNonAlphanumeric")] // No Special
        public async Task ValidateAsync_WithInvalidPassword_ShouldFailWithCorrectCode(string password, string expectedErrorCode)
        {
            // Arrange
            var user = new ApplicationUser();

            // Act
            var result = await _validator.ValidateAsync(null!, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == expectedErrorCode);
        }

        [Fact]
        public async Task ValidateAsync_WithPasswordInHistory_ShouldFail()
        {
            // Arrange
            _defaultPolicy.PasswordHistoryCount = 3;
            var hasher = new PasswordHasher<ApplicationUser>();
            var password = "RepeatedPassword1!";
            var user = new ApplicationUser { PasswordHash = hasher.HashPassword(null!, "OldPassword1!") };
            
            // History contains hashes of "HistoryPass1!", "RepeatedPassword1!", "HistoryPass2!"
            var history = new List<string> 
            { 
                hasher.HashPassword(null!, "HistoryPass1!"),
                hasher.HashPassword(null!, password),
                hasher.HashPassword(null!, "HistoryPass2!") 
            };
            user.PasswordHistory = JsonSerializer.Serialize(history);

            // Act
            var result = await _validator.ValidateAsync(null!, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == "PasswordReuse");
        }

        [Fact]
        public async Task ValidateAsync_WithExpiredPassword_ShouldFail()
        {
            // Arrange
            _defaultPolicy.PasswordExpirationDays = 30;
            var user = new ApplicationUser 
            { 
                LastPasswordChangeDate = DateTime.UtcNow.AddDays(-31) 
            };
            var password = "NewValidPassword1!";

            // Act
            var result = await _validator.ValidateAsync(null!, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == "PasswordExpired");
        }

        [Fact]
        public async Task ValidateAsync_WhenChangedTooSoon_ShouldFail()
        {
            // Arrange
            _defaultPolicy.MinPasswordAgeDays = 1;
            var user = new ApplicationUser 
            { 
                LastPasswordChangeDate = DateTime.UtcNow.AddHours(-12) // Changed 12 hours ago
            };
            var password = "NewValidPassword1!";

            // Act
            var result = await _validator.ValidateAsync(null!, user, password);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Code == "PasswordChangeTooSoon");
        }

        [Fact]
        public async Task ValidateAsync_WhenChangedAfterMinAge_ShouldSucceed()
        {
            // Arrange
            _defaultPolicy.MinPasswordAgeDays = 1;
            var user = new ApplicationUser 
            { 
                LastPasswordChangeDate = DateTime.UtcNow.AddDays(-2) // Changed 2 days ago
            };
            var password = "NewValidPassword1!";

            // Act
            var result = await _validator.ValidateAsync(null!, user, password);

            // Assert
            Assert.True(result.Succeeded);
        }
    }
}
