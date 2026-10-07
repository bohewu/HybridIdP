using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain;
using FluentAssertions;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// TDD tests for MfaService - write these FIRST, then implement the service.
/// </summary>
public class MfaServiceTests : IDisposable
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<IBrandingService> _brandingServiceMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IEmailTemplateService> _emailTemplateServiceMock;
    private readonly Mock<IPasswordHasher<ApplicationUser>> _passwordHasherMock;
    private readonly Mock<IDistributedCache> _distributedCacheMock;
    private readonly Mock<ISecurityPolicyService> _securityPolicyServiceMock;
    private readonly Mock<IEmailMfaAttemptStore> _emailMfaAttemptStoreMock;
    private readonly Mock<ILogger<MfaService>> _loggerMock;
    private readonly MfaService _sut;
    private readonly ApplicationDbContext _dbContext = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MfaTestTimeProvider _time = new();
    private const string TotpKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    public MfaServiceTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        
        _brandingServiceMock = new Mock<IBrandingService>();
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(TotpKey);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        _brandingServiceMock.Setup(x => x.GetAppNameAsync()).ReturnsAsync("TestApp");
        
        _emailServiceMock = new Mock<IEmailService>();
        _emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        _emailTemplateServiceMock
            .Setup(x => x.RenderMfaCodeEmailAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(("Test Subject", "<html>Test Body</html>"));
        _passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        _distributedCacheMock = new Mock<IDistributedCache>();
        _securityPolicyServiceMock = new Mock<ISecurityPolicyService>();
        _emailMfaAttemptStoreMock = new Mock<IEmailMfaAttemptStore>();
        _emailMfaAttemptStoreMock
            .Setup(x => x.TryReserveAttemptAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailMfaAttemptReservation.Reserved);
        // Default policy: RequireMfaForPasskey = false to avoid DbContext dependency in tests
        _securityPolicyServiceMock.Setup(x => x.GetCurrentPolicyAsync())
            .ReturnsAsync(new Core.Domain.Entities.SecurityPolicy { RequireMfaForPasskey = false });
        _loggerMock = new Mock<ILogger<MfaService>>();
        
        _sut = new MfaService(
            _userManagerMock.Object, 
            _brandingServiceMock.Object,
            _emailServiceMock.Object,
            _emailTemplateServiceMock.Object,
            _passwordHasherMock.Object,
            _distributedCacheMock.Object,
            _securityPolicyServiceMock.Object,
            _emailMfaAttemptStoreMock.Object,
            _dbContext,
            _loggerMock.Object, _time);
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
        var validCode = TotpCode(_time.GetUtcNow());
        
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
        user.TwoFactorEnabled.Should().BeTrue();
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
        var validCode = TotpCode(_time.GetUtcNow());
        
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
        user.TwoFactorEnabled.Should().BeFalse();
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
    }

    #endregion

    #region GenerateRecoveryCodesAsync Tests

    [Fact]
    public async Task GenerateRecoveryCodesAsync_Returns10Codes()
    {
        // Arrange
        var user = CreateTestUser();
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

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
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.GenerateRecoveryCodesAsync(user, count: 5);

        // Assert
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GenerateRecoveryCodesAsync_PersistenceFailure_ThrowsAndRestoresPreviousState()
    {
        // Arrange
        var user = CreateTestUser();
        var previousRecoveryCodes =
            System.Text.Json.JsonSerializer.Serialize(new[] { Guid.NewGuid().ToString("N") });
        user.RecoveryCodes = previousRecoveryCodes;
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "PersistenceFailure"
            }));

        // Act
        Func<Task> act = async () =>
        {
            _ = await _sut.GenerateRecoveryCodesAsync(user);
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        user.RecoveryCodes.Should().Be(previousRecoveryCodes);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
    }

    #endregion

    #region ValidateRecoveryCodeAsync Tests

    [Fact]
    public async Task ValidateRecoveryCodeAsync_ValidCode_ReturnsTrue()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        // RecoveryCodes is a JSON array of hashed codes
        user.RecoveryCodes = "[\"HASHED_VALID_CODE\"]";
        
        _passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_VALID_CODE", "VALID-CODE"))
            .Returns(PasswordVerificationResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.ValidateRecoveryCodeAsync(user, "VALID-CODE");

        // Assert
        result.Should().BeTrue();
        // After validation, the code should be removed from list
        user.RecoveryCodes.Should().Be("[]");
    }

    [Fact]
    public async Task ValidateRecoveryCodeAsync_InvalidCode_ReturnsFalse()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        user.RecoveryCodes = "[\"HASHED_CODE\"]";
        
        _passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", "INVALID"))
            .Returns(PasswordVerificationResult.Failed);

        // Act
        var result = await _sut.ValidateRecoveryCodeAsync(user, "INVALID");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Replay Attack Prevention Tests

    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ValidateTotpCodeAsync_ShouldConsumeActualMatchedStepAndRejectReplayAcrossClockAdvance(int offset)
    {
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        var issued = _time.Now.AddSeconds(offset * 30);
        var code = TotpCode(issued);
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), code)).ReturnsAsync(true);

        Assert.True(await _sut.ValidateTotpCodeAsync(user, code));
        Assert.Equal(issued.ToUnixTimeSeconds() / 30, user.LastTotpValidatedWindow);
        Assert.False(await _sut.ValidateTotpCodeAsync(user, code));
        _time.Now = _time.Now.AddSeconds(30);
        Assert.False(await _sut.ValidateTotpCodeAsync(user, code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TotpConsumption_ShouldRejectFailedPersistenceAndRestoreState(bool enrollment)
    {
        var user = CreateTestUser();
        user.TwoFactorEnabled = !enrollment;
        var code = TotpCode(_time.Now);
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), code)).ReturnsAsync(true);
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));

        Assert.False(enrollment ? await _sut.VerifyAndEnableTotpAsync(user, code) : await _sut.ValidateTotpCodeAsync(user, code));
        Assert.Null(user.LastTotpValidatedWindow);
        Assert.Equal(!enrollment, user.TwoFactorEnabled);
    }

    [Fact]
    public async Task ValidateRecoveryCodeAsync_ShouldRejectFailedPersistenceAndMalformedStorage()
    {
        var user = CreateTestUser();
        user.RecoveryCodes = "[\"hashed-code\"]";
        _passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "hashed-code", "CODE")).Returns(PasswordVerificationResult.SuccessRehashNeeded);
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Failed(new IdentityError()));
        Assert.False(await _sut.ValidateRecoveryCodeAsync(user, "CODE"));
        Assert.Equal("[\"hashed-code\"]", user.RecoveryCodes);
        user.RecoveryCodes = "not-json";
        Assert.False(await _sut.ValidateRecoveryCodeAsync(user, "CODE"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisableFactor_ShouldPlanCascadeBeforeMutationAndPreserveRetiredHistory(bool totp, bool mandatory)
    {
        var user = CreateTestUser();
        user.TwoFactorEnabled = totp;
        user.EmailMfaEnabled = !totp;
        user.LastTotpValidatedWindow = 12;
        _dbContext.Users.Add(user);
        var historicalTime = DateTime.UtcNow.AddYears(-1);
        _dbContext.UserCredentials.AddRange(
            new Core.Domain.Entities.UserCredential { Id = 1, UserId = user.Id, CredentialId = [1], PublicKey = [1] },
            new Core.Domain.Entities.UserCredential { Id = 2, UserId = user.Id, CredentialId = [2], PublicKey = [2], DisabledAtUtc = historicalTime });
        await _dbContext.SaveChangesAsync();
        _securityPolicyServiceMock.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new Core.Domain.Entities.SecurityPolicy
            { RequireMfaForPasskey = true, EnforceMandatoryMfaEnrollment = mandatory });
        _userManagerMock.Setup(x => x.UpdateAsync(user)).Returns(async () => { await _dbContext.SaveChangesAsync(); return IdentityResult.Success; });
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user)).Returns(async () => { await _dbContext.SaveChangesAsync(); return IdentityResult.Success; });

        var result = totp ? await _sut.DisableMfaAsync(user) : await _sut.DisableEmailMfaAsync(user);

        Assert.Equal(mandatory ? MfaRemovalResult.MandatoryFactorRequired : MfaRemovalResult.Succeeded, result);
        Assert.Equal(mandatory && totp, user.TwoFactorEnabled);
        Assert.Equal(mandatory && !totp, user.EmailMfaEnabled);
        var keys = await _dbContext.UserCredentials.OrderBy(key => key.Id).ToListAsync();
        Assert.Equal(2, keys.Count);
        Assert.Equal(mandatory, keys[0].DisabledAtUtc == null);
        Assert.Equal(historicalTime, keys[1].DisabledAtUtc);
        if (mandatory) _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisableFactor_ShouldRequireAnEnabledRemainingMethod(bool otherMethodEnabled)
    {
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        user.EmailMfaEnabled = true;
        _securityPolicyServiceMock.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new Core.Domain.Entities.SecurityPolicy
            { EnforceMandatoryMfaEnrollment = true, EnableEmailMfa = otherMethodEnabled });
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user)).ReturnsAsync(IdentityResult.Success);
        var result = await _sut.DisableMfaAsync(user);
        Assert.Equal(otherMethodEnabled ? MfaRemovalResult.Succeeded : MfaRemovalResult.MandatoryFactorRequired, result);
    }

    [Fact]
    public async Task DisableEmailMfaAsync_ShouldNotLeakFailedFactorOrRetirementIntoLaterSave()
    {
        var user = CreateTestUser();
        user.EmailMfaEnabled = true;
        _dbContext.Users.Add(user);
        _dbContext.UserCredentials.Add(new Core.Domain.Entities.UserCredential
            { UserId = user.Id, CredentialId = [1], PublicKey = [1] });
        await _dbContext.SaveChangesAsync();
        _securityPolicyServiceMock.Setup(s => s.GetCurrentPolicyAsync())
            .ReturnsAsync(new Core.Domain.Entities.SecurityPolicy { RequireMfaForPasskey = true });
        _userManagerMock.Setup(s => s.UpdateAsync(user)).ReturnsAsync(IdentityResult.Failed(new IdentityError()));
        Assert.Equal(MfaRemovalResult.PersistenceFailed, await _sut.DisableEmailMfaAsync(user));
        user.PhoneNumber = "later-write";
        await _dbContext.SaveChangesAsync();
        Assert.True((await _dbContext.Users.SingleAsync()).EmailMfaEnabled);
        Assert.Null((await _dbContext.UserCredentials.SingleAsync()).DisabledAtUtc);
    }

    [Fact]
    public async Task ValidateTotpCodeAsync_FirstUse_ReturnsTrue()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        user.LastTotpValidatedWindow = null; // Never validated before
        
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            user, 
            It.IsAny<string>(), 
            TotpCode(_time.GetUtcNow())))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.ValidateTotpCodeAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeTrue();
        user.LastTotpValidatedWindow.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateTotpCodeAsync_SameCodeInSameWindow_ReturnsFalse()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        // Simulates the same 30-second window (current window number)
        var currentWindow = _time.GetUtcNow().ToUnixTimeSeconds() / 30;
        user.LastTotpValidatedWindow = currentWindow;
        
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            user, 
            It.IsAny<string>(), 
            TotpCode(_time.GetUtcNow())))
            .ReturnsAsync(true); // Code is valid by Identity

        // Act
        var result = await _sut.ValidateTotpCodeAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeFalse(); // But rejected due to replay attack prevention
    }

    [Fact]
    public async Task ValidateTotpCodeAsync_SameCodeInNewWindow_ReturnsTrue()
    {
        // Arrange
        var user = CreateTestUser();
        user.TwoFactorEnabled = true;
        // Previous window (more than 30 seconds ago)
        var previousWindow = (_time.GetUtcNow().ToUnixTimeSeconds() / 30) - 2;
        user.LastTotpValidatedWindow = previousWindow;
        
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            user, 
            It.IsAny<string>(), 
            TotpCode(_time.GetUtcNow())))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.ValidateTotpCodeAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Email MFA Tests (Phase 20.3)

    [Fact]
    public async Task SendEmailMfaCodeAsync_NoEmail_ThrowsException()
    {
        // Arrange
        var user = CreateTestUser();
        user.Email = null;
        
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        emailTemplateServiceMock.Setup(x => x.RenderMfaCodeEmailAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(("Subject", "Body"));
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            sut.SendEmailMfaCodeAsync(user));
    }

    [Fact]
    public async Task SendEmailMfaCodeAsync_ValidEmail_StoresHashedCodeAndQueuesEmail()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaVerificationAttempts = 3;
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        emailTemplateServiceMock.Setup(x => x.RenderMfaCodeEmailAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(("Subject", "Body"));
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        string? generatedCode = null;
        passwordHasherMock.Setup(x => x.HashPassword(user, It.IsAny<string>()))
            .Callback<ApplicationUser, string>((_, code) => generatedCode = code)
            .Returns("HASHED_CODE");
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act
        await sut.SendEmailMfaCodeAsync(user);

        // Assert
        user.EmailMfaCode.Should().Be("HASHED_CODE");
        user.EmailMfaCodeExpiry.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(5));
        user.EmailMfaVerificationAttempts.Should().Be(0);
        generatedCode.Should().MatchRegex("^[0-9]{6}$");
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
        emailServiceMock.Verify(x => x.SendEmailAsync(
            user.Email!, 
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            true, 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task VerifyEmailMfaCodeAsync_NoCodePending_ReturnsFalse()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = null;
        
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act
        var result = await sut.VerifyEmailMfaCodeAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyEmailMfaCodeAsync_ExpiredCode_ReturnsFalseAndClearsCode()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(-5); // Expired
        user.EmailMfaVerificationAttempts = 3;
        
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        
        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act
        var result = await sut.VerifyEmailMfaCodeAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeFalse();
        user.EmailMfaCode.Should().BeNull();
        user.EmailMfaCodeExpiry.Should().BeNull();
        user.EmailMfaVerificationAttempts.Should().Be(0);
    }

    [Fact]
    public async Task VerifyEmailMfaCodeAsync_ValidCode_ReturnsTrueAndClearsCode()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5); // Not expired
        
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", TotpCode(_time.GetUtcNow())))
            .Returns(PasswordVerificationResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        
        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act
        var result = await sut.VerifyEmailMfaCodeAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeTrue();
        user.EmailMfaCode.Should().BeNull();
        user.EmailMfaCodeExpiry.Should().BeNull();
        distributedCacheMock.Verify(
            x => x.RemoveAsync(
                $"EmailMfa_Cooldown_{user.Id}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyEmailMfaCodeAsync_InvalidCode_ReturnsFalse()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", "000000"))
            .Returns(PasswordVerificationResult.Failed);
        
        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act
        var result = await sut.VerifyEmailMfaCodeAsync(user, "000000");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyEmailMfaCodeAsync_FiveInvalidAttempts_InvalidatesPendingCode()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        passwordHasherMock
            .Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", It.IsAny<string>()))
            .Returns((ApplicationUser _, string _, string code) =>
                code == "123456"
                    ? PasswordVerificationResult.Success
                    : PasswordVerificationResult.Failed);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _emailMfaAttemptStoreMock
            .SetupSequence(x => x.TryReserveAttemptAsync(
                user.Id,
                "HASHED_CODE",
                It.IsAny<DateTime>(),
                5,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailMfaAttemptReservation.Reserved)
            .ReturnsAsync(EmailMfaAttemptReservation.Reserved)
            .ReturnsAsync(EmailMfaAttemptReservation.Reserved)
            .ReturnsAsync(EmailMfaAttemptReservation.Reserved)
            .ReturnsAsync(EmailMfaAttemptReservation.FinalAttempt)
            .ReturnsAsync(EmailMfaAttemptReservation.Rejected);
        var sut = CreateMfaService(passwordHasherMock: passwordHasherMock);

        // Act
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var invalidResult = await sut.VerifyEmailMfaCodeAsync(user, "000000");
            invalidResult.Should().BeFalse();
        }

        var resultAfterLimit = await sut.VerifyEmailMfaCodeAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        resultAfterLimit.Should().BeFalse();
        _emailMfaAttemptStoreMock.Verify(
            x => x.InvalidatePendingCodeAsync(
                user.Id,
                "HASHED_CODE",
                It.IsAny<CancellationToken>()),
            Times.Once);
        passwordHasherMock.Verify(
            x => x.VerifyHashedPassword(user, "HASHED_CODE", "123456"),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAndEnableEmailMfaAsync_ValidCode_ConsumesProofAndEnablesEmailMfa()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaEnabled = false;
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        user.EmailMfaVerificationAttempts = 4;
        
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", TotpCode(_time.GetUtcNow())))
            .Returns(PasswordVerificationResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _emailMfaAttemptStoreMock
            .Setup(x => x.TryReserveAttemptAsync(
                user.Id,
                "HASHED_CODE",
                It.IsAny<DateTime>(),
                5,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailMfaAttemptReservation.FinalAttempt);

        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act
        var result = await sut.VerifyAndEnableEmailMfaAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeTrue();
        user.EmailMfaEnabled.Should().BeTrue();
        user.EmailMfaCode.Should().BeNull();
        user.EmailMfaCodeExpiry.Should().BeNull();
        user.EmailMfaVerificationAttempts.Should().Be(0);
        _emailMfaAttemptStoreMock.Verify(
            x => x.InvalidatePendingCodeAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
        distributedCacheMock.Verify(
            x => x.RemoveAsync(
                $"EmailMfa_Cooldown_{user.Id}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndEnableEmailMfaAsync_CooldownCleanupFailure_StillReturnsTrue()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", TotpCode(_time.GetUtcNow())))
            .Returns(PasswordVerificationResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        var distributedCacheMock = new Mock<IDistributedCache>();
        distributedCacheMock
            .Setup(x => x.RemoveAsync(
                $"EmailMfa_Cooldown_{user.Id}",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test-only cache failure"));
        var sut = CreateMfaService(
            passwordHasherMock: passwordHasherMock,
            distributedCacheMock: distributedCacheMock);

        // Act
        var result = await sut.VerifyAndEnableEmailMfaAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeTrue();
        user.EmailMfaEnabled.Should().BeTrue();
        user.EmailMfaCode.Should().BeNull();
        distributedCacheMock.Verify(
            x => x.RemoveAsync(
                $"EmailMfa_Cooldown_{user.Id}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndEnableEmailMfaAsync_InvalidCode_DoesNotEnableEmailMfa()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5);
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", "000000"))
            .Returns(PasswordVerificationResult.Failed);
        var distributedCacheMock = new Mock<IDistributedCache>();
        var sut = CreateMfaService(
            passwordHasherMock: passwordHasherMock,
            distributedCacheMock: distributedCacheMock);

        // Act
        var result = await sut.VerifyAndEnableEmailMfaAsync(user, "000000");

        // Assert
        result.Should().BeFalse();
        distributedCacheMock.Verify(
            x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        user.EmailMfaEnabled.Should().BeFalse();
        user.EmailMfaCode.Should().Be("HASHED_CODE");
        _userManagerMock.Verify(
            x => x.UpdateAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAndEnableEmailMfaAsync_MissingExpiry_DoesNotEnableEmailMfa()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = null;
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var sut = CreateMfaService(passwordHasherMock: passwordHasherMock);

        // Act
        var result = await sut.VerifyAndEnableEmailMfaAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeFalse();
        user.EmailMfaEnabled.Should().BeFalse();
        user.EmailMfaCode.Should().BeNull();
        user.EmailMfaCodeExpiry.Should().BeNull();
        passwordHasherMock.Verify(
            x => x.VerifyHashedPassword(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task VerifyAndEnableEmailMfaAsync_PersistenceFailure_DoesNotPromoteInMemoryState()
    {
        // Arrange
        var user = CreateTestUser();
        var pendingExpiry = DateTime.UtcNow.AddMinutes(5);
        user.EmailMfaCode = "HASHED_CODE";
        user.EmailMfaCodeExpiry = pendingExpiry;
        user.EmailMfaVerificationAttempts = 2;
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        passwordHasherMock.Setup(x => x.VerifyHashedPassword(user, "HASHED_CODE", TotpCode(_time.GetUtcNow())))
            .Returns(PasswordVerificationResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "PersistenceFailed",
                Description = "Test-only persistence failure"
            }));
        var distributedCacheMock = new Mock<IDistributedCache>();
        var sut = CreateMfaService(
            passwordHasherMock: passwordHasherMock,
            distributedCacheMock: distributedCacheMock);

        // Act
        var result = await sut.VerifyAndEnableEmailMfaAsync(user, TotpCode(_time.GetUtcNow()));

        // Assert
        result.Should().BeFalse();
        user.EmailMfaEnabled.Should().BeFalse();
        user.EmailMfaCode.Should().Be("HASHED_CODE");
        user.EmailMfaCodeExpiry.Should().Be(pendingExpiry);
        user.EmailMfaVerificationAttempts.Should().Be(2);
        distributedCacheMock.Verify(
            x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisableEmailMfaAsync_ClearsEmailMfaFields()
    {
        // Arrange
        var user = CreateTestUser();
        user.EmailMfaEnabled = true;
        user.EmailMfaCode = "SOME_CODE";
        user.EmailMfaCodeExpiry = DateTime.UtcNow;
        user.EmailMfaVerificationAttempts = 3;
        
        var emailServiceMock = new Mock<IEmailService>();
        var emailTemplateServiceMock = new Mock<IEmailTemplateService>();
        var passwordHasherMock = new Mock<IPasswordHasher<ApplicationUser>>();
        var distributedCacheMock = new Mock<IDistributedCache>();
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        
        var sut = CreateMfaService(emailServiceMock, emailTemplateServiceMock, passwordHasherMock, distributedCacheMock);

        // Act
        await sut.DisableEmailMfaAsync(user);

        // Assert
        user.EmailMfaEnabled.Should().BeFalse();
        user.EmailMfaCode.Should().BeNull();
        user.EmailMfaCodeExpiry.Should().BeNull();
        user.EmailMfaVerificationAttempts.Should().Be(0);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
    }

    #endregion

    #region Helpers

    private static string TotpCode(DateTimeOffset time)
    {
        var step = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(time.ToUnixTimeSeconds() / 30));
        var hash = System.Security.Cryptography.HMACSHA1.HashData(System.Text.Encoding.ASCII.GetBytes("12345678901234567890"), step);
        var offset = hash[^1] & 15;
        var value = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1000000).ToString("D6");
    }

    private sealed class MfaTestTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ApplicationUser CreateTestUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "testuser",
        Email = "test@example.com",
        EmailConfirmed = true
    };

    public void Dispose() => _dbContext.Dispose();

    private MfaService CreateMfaService(
        Mock<IEmailService>? emailServiceMock = null,
        Mock<IEmailTemplateService>? emailTemplateServiceMock = null,
        Mock<IPasswordHasher<ApplicationUser>>? passwordHasherMock = null,
        Mock<IDistributedCache>? distributedCacheMock = null,
        Mock<IEmailMfaAttemptStore>? emailMfaAttemptStoreMock = null)
    {
        return new MfaService(
            _userManagerMock.Object,
            _brandingServiceMock.Object,
            emailServiceMock?.Object ?? _emailServiceMock.Object,
            emailTemplateServiceMock?.Object ?? _emailTemplateServiceMock.Object,
            passwordHasherMock?.Object ?? _passwordHasherMock.Object,
            distributedCacheMock?.Object ?? _distributedCacheMock.Object,
            _securityPolicyServiceMock.Object,
            emailMfaAttemptStoreMock?.Object ?? _emailMfaAttemptStoreMock.Object,
            _dbContext,
            _loggerMock.Object, _time);
    }

    #endregion
}
