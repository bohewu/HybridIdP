using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// TDD tests for MfaService - write these FIRST, then implement the service.
/// </summary>
public class MfaServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<IBrandingService> _brandingServiceMock;
    private readonly MfaService _sut;

    public MfaServiceTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        
        _brandingServiceMock = new Mock<IBrandingService>();
        _brandingServiceMock.Setup(x => x.GetAppNameAsync()).ReturnsAsync("TestApp");
        
        _sut = new MfaService(_userManagerMock.Object, _brandingServiceMock.Object);
    }

    #region GetTotpSetupInfoAsync Tests

    [Fact]
    public async Task GetTotpSetupInfoAsync_ReturnsValidSetupInfo()
    {
        // Arrange
        var user = CreateTestUser();
        var secretKey = "JBSWY3DPEHPK3PXP"; // Base32 encoded
        
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync((string?)null);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync(secretKey);

        // Act
        var result = await _sut.GetTotpSetupInfoAsync(user);

        // Assert
        result.Should().NotBeNull();
        result.SharedKey.Should().NotBeNullOrEmpty();
        result.AuthenticatorUri.Should().Contain("otpauth://totp/");
        result.QrCodeDataUri.Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_ExistingKey_ReusesKey()
    {
        // Arrange
        var user = CreateTestUser();
        var existingKey = "EXISTINGKEY12345";
        
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync(existingKey);

        // Act
        var result = await _sut.GetTotpSetupInfoAsync(user);

        // Assert
        result.SharedKey.Should().Contain(existingKey.Replace(" ", "").ToLowerInvariant().Substring(0, 4));
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    #endregion

    #region VerifyAndEnableTotpAsync Tests

    [Fact]
    public async Task VerifyAndEnableTotpAsync_ValidCode_EnablesMfa()
    {
        // Arrange
        var user = CreateTestUser();
        var validCode = "123456";
        
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            user, 
            It.IsAny<string>(), 
            validCode))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.SetTwoFactorEnabledAsync(user, true))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.VerifyAndEnableTotpAsync(user, validCode);

        // Assert
        result.Should().BeTrue();
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(user, true), Times.Once);
    }

    [Fact]
    public async Task VerifyAndEnableTotpAsync_InvalidCode_ReturnsFalse()
    {
        // Arrange
        var user = CreateTestUser();
        var invalidCode = "000000";
        
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            user, 
            It.IsAny<string>(), 
            invalidCode))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.VerifyAndEnableTotpAsync(user, invalidCode);

        // Assert
        result.Should().BeFalse();
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>()), Times.Never);
    }

    #endregion

    #region ValidateTotpCodeAsync Tests

    [Fact]
    public async Task ValidateTotpCodeAsync_ValidCode_ReturnsTrue()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        var validCode = "123456";
        
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            user, 
            It.IsAny<string>(), 
            validCode))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.ValidateTotpCodeAsync(user, validCode);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTotpCodeAsync_InvalidCode_ReturnsFalse()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            user, 
            It.IsAny<string>(), 
            It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.ValidateTotpCodeAsync(user, "000000");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DisableMfaAsync Tests

    [Fact]
    public async Task DisableMfaAsync_DisablesTwoFactorAndResetsKey()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        
        _userManagerMock.Setup(x => x.SetTwoFactorEnabledAsync(user, false))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _sut.DisableMfaAsync(user);

        // Assert
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(user, false), Times.Once);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
    }

    #endregion

    #region GenerateRecoveryCodesAsync Tests

    [Fact]
    public async Task GenerateRecoveryCodesAsync_Returns10Codes()
    {
        // Arrange
        var user = CreateTestUser();
        var codes = new[] { "CODE1", "CODE2", "CODE3", "CODE4", "CODE5", 
                           "CODE6", "CODE7", "CODE8", "CODE9", "CODE10" };
        
        _userManagerMock.Setup(x => x.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))
            .ReturnsAsync(codes);

        // Act
        var result = await _sut.GenerateRecoveryCodesAsync(user);

        // Assert
        result.Should().HaveCount(10);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_CustomCount_ReturnsRequestedCount()
    {
        // Arrange
        var user = CreateTestUser();
        var codes = new[] { "CODE1", "CODE2", "CODE3", "CODE4", "CODE5" };
        
        _userManagerMock.Setup(x => x.GenerateNewTwoFactorRecoveryCodesAsync(user, 5))
            .ReturnsAsync(codes);

        // Act
        var result = await _sut.GenerateRecoveryCodesAsync(user, count: 5);

        // Assert
        result.Should().HaveCount(5);
    }

    #endregion

    #region ValidateRecoveryCodeAsync Tests

    [Fact]
    public async Task ValidateRecoveryCodeAsync_ValidCode_ReturnsTrue()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        
        _userManagerMock.Setup(x => x.RedeemTwoFactorRecoveryCodeAsync(user, "VALID-CODE"))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.ValidateRecoveryCodeAsync(user, "VALID-CODE");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRecoveryCodeAsync_InvalidCode_ReturnsFalse()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        
        _userManagerMock.Setup(x => x.RedeemTwoFactorRecoveryCodeAsync(user, "INVALID"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid code" }));

        // Act
        var result = await _sut.ValidateRecoveryCodeAsync(user, "INVALID");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helpers

    private static ApplicationUser CreateTestUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "testuser",
        Email = "test@example.com",
        EmailConfirmed = true
    };

    #endregion
}
