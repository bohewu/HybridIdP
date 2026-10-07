using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Application.DTOs;
using Core.Application;
using Core.Domain.Entities;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Core.Domain;
using Infrastructure;
using Microsoft.AspNetCore.WebUtilities;

namespace Tests.Infrastructure.UnitTests;

public class PasskeyServiceTests
{
    private readonly Mock<IFido2> _fido2Mock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly ApplicationDbContext _dbContext;
    private readonly DbContextOptions<ApplicationDbContext> _dbOptions;
    private readonly Mock<ILogger<PasskeyService>> _loggerMock;
    private readonly PasskeyService _sut;
    private readonly Mock<ISecurityPolicyService> _securityPolicy = new();

    public PasskeyServiceTests()
    {
        _fido2Mock = new Mock<IFido2>();
        
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null, null, null, null, null, null, null, null);

        _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(_dbOptions);
        
        _loggerMock = new Mock<ILogger<PasskeyService>>();
        _securityPolicy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        
        _sut = new PasskeyService(
            _fido2Mock.Object,
            _userManagerMock.Object,
            _dbContext,
            _loggerMock.Object,
            _securityPolicy.Object);
    }

    [Fact]
    public async Task GetUserPasskeysAsync_ReturnsCorrectPasskeys()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        
        var cred1 = new UserCredential 
        { 
            Id = 1, 
            UserId = userId, 
            DeviceName = "Device 1", 
            RegDate = DateTime.UtcNow.AddDays(-2),
            LastUsedAt = DateTime.UtcNow.AddDays(-1),
            CredentialId = new byte[] { 1, 2, 3 },
            PublicKey = new byte[] { 4, 5, 6 }
        };
        var cred2 = new UserCredential 
        { 
            Id = 2, 
            UserId = userId, 
            DeviceName = "Device 2", 
            RegDate = DateTime.UtcNow,
            LastUsedAt = null,
            CredentialId = new byte[] { 7, 8, 9 },
            PublicKey = new byte[] { 10, 11, 12 }
        };
        var otherCred = new UserCredential 
        { 
            Id = 3, 
            UserId = otherUserId, 
            DeviceName = "Other Device", 
            RegDate = DateTime.UtcNow,
            CredentialId = new byte[] { 13, 14, 15 },
            PublicKey = new byte[] { 16, 17, 18 }
        };

        var retiredCred = new UserCredential
        {
            Id = 4,
            UserId = userId,
            CredentialId = new byte[] { 19 },
            PublicKey = new byte[] { 20 },
            DisabledAtUtc = DateTime.UtcNow
        };
        _dbContext.UserCredentials.AddRange(cred1, cred2, otherCred, retiredCred);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetUserPasskeysAsync(userId, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == 2); // Ordered descending by RegDate
        Assert.Equal("Device 2", result[0].DeviceName);
        Assert.Equal("Device 1", result[1].DeviceName);
        Assert.Equal(4, await _dbContext.UserCredentials.CountAsync());
    }

    [Fact]
    public async Task DeletePasskeyAsync_ExistingPasskey_RetiresOnceAndPreservesHistory()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var cred = new UserCredential 
        { 
            Id = 1, 
            UserId = userId, 
            CredentialId = new byte[] { 1 }, 
            PublicKey = new byte[] { 2 } 
        };
        
        _dbContext.UserCredentials.Add(cred);
        _dbContext.Users.Add(new ApplicationUser { Id = userId, UserName = "key-owner" });
        _userManagerMock.Setup(manager => manager.UpdateAsync(It.IsAny<ApplicationUser>()))
            .Returns(async () => { await _dbContext.SaveChangesAsync(); return IdentityResult.Success; });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeletePasskeyAsync(userId, 1, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.NotNull((await _dbContext.UserCredentials.SingleAsync()).DisabledAtUtc);
        var retiredAt = cred.DisabledAtUtc;
        Assert.False(await _sut.DeletePasskeyAsync(userId, 1));
        Assert.Equal(retiredAt, (await _dbContext.UserCredentials.SingleAsync()).DisabledAtUtc);
    }

    [Fact]
    public async Task DeletePasskeyAsync_NonExistentPasskey_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        
        // Act
        var result = await _sut.DeletePasskeyAsync(userId, 999, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task DeletePasskeyAsync_ShouldPreserveKeyOnMandatoryPolicyOrFailedPersistence(bool mandatory, bool persistenceSucceeds)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "owner" };
        _dbContext.Users.Add(user);
        _dbContext.UserCredentials.Add(new UserCredential { Id = 7, UserId = user.Id, CredentialId = [7], PublicKey = [7] });
        await _dbContext.SaveChangesAsync();
        _securityPolicy.Setup(s => s.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy { EnforceMandatoryMfaEnrollment = mandatory });
        _userManagerMock.Setup(s => s.UpdateAsync(user)).Returns(async () =>
        {
            if (!persistenceSucceeds) return IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" });
            await _dbContext.SaveChangesAsync();
            return IdentityResult.Success;
        });
        Assert.Equal(!mandatory && persistenceSucceeds, await _sut.DeletePasskeyAsync(user.Id, 7));
        user.PhoneNumber = "test-later-save";
        await _dbContext.SaveChangesAsync();
        Assert.Equal(!mandatory && persistenceSucceeds, (await _dbContext.UserCredentials.SingleAsync()).DisabledAtUtc != null);
    }

    [Fact]
    public async Task DeletePasskeyAsync_PasskeyBelongsToAnotherUser_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var cred = new UserCredential 
        { 
            Id = 1, 
            UserId = otherUserId,
            CredentialId = new byte[] { 1 }, 
            PublicKey = new byte[] { 2 }
        };
        
        _dbContext.UserCredentials.Add(cred);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeletePasskeyAsync(userId, 1, CancellationToken.None);

        // Assert
        Assert.False(result);
        Assert.Single(_dbContext.UserCredentials);
    }

    [Fact]
    public async Task GetRegistrationOptionsAsync_ReturnsOptions()
    {
        // Arrange
        var user = new ApplicationUser { UserName = "testuser", Email = "test@example.com" };
        _dbContext.UserCredentials.AddRange(
            new UserCredential { UserId = user.Id, CredentialId = new byte[] { 1 }, PublicKey = new byte[] { 2 } },
            new UserCredential { UserId = user.Id, CredentialId = new byte[] { 3 }, PublicKey = new byte[] { 4 }, DisabledAtUtc = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();
        RequestNewCredentialParams? captured = null;
        var options = new CredentialCreateOptions 
        { 
            Challenge = new byte[] { 1, 2, 3 },
            User = new Fido2User { Id = Encoding.UTF8.GetBytes("testuser") },
            PubKeyCredParams = new List<PubKeyCredParam>(),
            Rp = new PublicKeyCredentialRpEntity("localhost", "HybridIdP")
        };
        
        _fido2Mock.Setup(x => x.RequestNewCredential(It.IsAny<RequestNewCredentialParams>()))
            .Callback<RequestNewCredentialParams>(parameters => captured = parameters)
            .Returns(options);

        // Act
        var result = await _sut.GetRegistrationOptionsAsync(user, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(options.Challenge, result.Challenge);
        Assert.NotNull(captured);
        Assert.Equal(new byte[] { 1 }, Assert.Single(captured.ExcludeCredentials).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAssertionOptionsAsync_ShouldExposeDescriptorsOnlyForAuthenticatedSubject(bool authenticated)
    {
        var userId = Guid.NewGuid();
        _dbContext.UserCredentials.AddRange(
            new UserCredential { UserId = userId, CredentialId = new byte[] { 1 }, PublicKey = new byte[] { 2 } },
            new UserCredential { UserId = userId, CredentialId = new byte[] { 5 }, PublicKey = new byte[] { 6 }, DisabledAtUtc = DateTime.UtcNow },
            new UserCredential { UserId = Guid.NewGuid(), CredentialId = new byte[] { 3 }, PublicKey = new byte[] { 4 } });
        await _dbContext.SaveChangesAsync();
        GetAssertionOptionsParams? captured = null;
        _fido2Mock.Setup(fido => fido.GetAssertionOptions(It.IsAny<GetAssertionOptionsParams>()))
            .Callback<GetAssertionOptionsParams>(parameters => captured = parameters)
            .Returns(AssertionOptions.FromJson("{\"challenge\":\"AQ\"}"));

        await _sut.GetAssertionOptionsAsync(authenticated ? userId : null);

        Assert.NotNull(captured);
        if (authenticated)
        {
            Assert.Equal(new byte[] { 1 }, Assert.Single(captured.AllowedCredentials).Id);
        }
        else
        {
            Assert.Empty(captured.AllowedCredentials);
        }
        _userManagerMock.Verify(manager => manager.FindByNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyAssertionAsync_ValidatedAuthenticatorDataWithUserVerification_ReturnsUserVerified()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "passkey-user"
        };
        var credentialId = new byte[] { 1, 2, 3 };
        _dbContext.Users.Add(user);
        _dbContext.UserCredentials.Add(new UserCredential
        {
            UserId = user.Id,
            CredentialId = new byte[] { 7, 8, 9 },
            PublicKey = new byte[] { 10 },
            DisabledAtUtc = DateTime.UtcNow
        });
        _dbContext.UserCredentials.Add(new UserCredential
        {
            UserId = user.Id,
            CredentialId = credentialId,
            PublicKey = new byte[] { 4, 5, 6 },
            SignatureCounter = 1
        });
        await _dbContext.SaveChangesAsync();

        _fido2Mock
            .Setup(fido2 => fido2.MakeAssertionAsync(
                It.IsAny<MakeAssertionParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerifyAssertionResult
            {
                CredentialId = credentialId,
                SignCount = 2
            });

        var authenticatorData = new byte[37];
        authenticatorData[32] = 0x05; // user present and user verified
        authenticatorData[36] = 2;
        var responseJson = $$"""
            {
              "id": "{{Base64UrlTextEncoder.Encode(credentialId)}}",
              "rawId": "{{Base64UrlTextEncoder.Encode(credentialId)}}",
              "type": "public-key",
              "response": {
                "authenticatorData": "{{Base64UrlTextEncoder.Encode(authenticatorData)}}",
                "signature": "AQ",
                "clientDataJSON": "e30"
              }
            }
            """;

        var result = await _sut.VerifyAssertionAsync(
            responseJson,
            "{\"challenge\":\"AQ\"}",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Same(user, result.User);
        Assert.True(result.UserVerified);
        Assert.Equal(2, await _dbContext.UserCredentials.CountAsync());
        Assert.Single(await _sut.GetUserPasskeysAsync(user.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifyAssertionAsync_ShouldRejectRetiredCredential_AlsoWhenDisabledDuringVerification(bool disableDuringVerification)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "retired-passkey-user" };
        var disabledAt = DateTime.UtcNow;
        var credential = new UserCredential
        {
            UserId = user.Id,
            CredentialId = new byte[] { 1, 2, 3 },
            PublicKey = new byte[] { 4, 5, 6 },
            SignatureCounter = 1,
            DisabledAtUtc = disableDuringVerification ? null : disabledAt
        };
        _dbContext.Users.Add(user);
        _dbContext.UserCredentials.Add(credential);
        await _dbContext.SaveChangesAsync();
        _fido2Mock.Setup(fido => fido.MakeAssertionAsync(It.IsAny<MakeAssertionParams>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var operatorContext = new ApplicationDbContext(_dbOptions);
                var stored = await operatorContext.UserCredentials.SingleAsync();
                stored.DisabledAtUtc = disabledAt;
                await operatorContext.SaveChangesAsync();
                return new VerifyAssertionResult { CredentialId = credential.CredentialId, SignCount = 2 };
            });
        var authenticatorData = new byte[37];
        authenticatorData[32] = 0x05;
        authenticatorData[36] = 2;
        var response = $$"""
            { "id": "AQID", "rawId": "AQID", "type": "public-key",
              "response": { "authenticatorData": "{{Base64UrlTextEncoder.Encode(authenticatorData)}}",
                "signature": "AQ", "clientDataJSON": "e30" } }
            """;

        var result = await _sut.VerifyAssertionAsync(response, "{\"challenge\":\"AQ\"}");

        Assert.False(result.Success);
        Assert.Null(result.User);
        Assert.False(result.UserVerified);
        _fido2Mock.Verify(fido => fido.MakeAssertionAsync(It.IsAny<MakeAssertionParams>(), It.IsAny<CancellationToken>()),
            disableDuringVerification ? Times.Once() : Times.Never());
        await using var inspectionContext = new ApplicationDbContext(_dbOptions);
        var retained = await inspectionContext.UserCredentials.SingleAsync();
        Assert.Equal(disabledAt, retained.DisabledAtUtc);
        Assert.Equal(1u, retained.SignatureCounter);
        Assert.Null(retained.LastUsedAt);
    }

    [Fact]
    public async Task VerifyAssertionAsync_ValidatedAuthenticatorDataWithoutUserVerification_ReturnsUserNotVerified()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "passkey-user"
        };
        var credentialId = new byte[] { 1, 2, 3 };
        _dbContext.Users.Add(user);
        _dbContext.UserCredentials.Add(new UserCredential
        {
            UserId = user.Id,
            CredentialId = credentialId,
            PublicKey = new byte[] { 4, 5, 6 },
            SignatureCounter = 1
        });
        await _dbContext.SaveChangesAsync();

        _fido2Mock
            .Setup(fido2 => fido2.MakeAssertionAsync(
                It.IsAny<MakeAssertionParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerifyAssertionResult
            {
                CredentialId = credentialId,
                SignCount = 2
            });

        var authenticatorData = new byte[37];
        authenticatorData[32] = 0x01; // user present without user verification
        authenticatorData[36] = 2;
        var responseJson = $$"""
            {
              "id": "{{Base64UrlTextEncoder.Encode(credentialId)}}",
              "rawId": "{{Base64UrlTextEncoder.Encode(credentialId)}}",
              "type": "public-key",
              "response": {
                "authenticatorData": "{{Base64UrlTextEncoder.Encode(authenticatorData)}}",
                "signature": "AQ",
                "clientDataJSON": "e30"
              }
            }
            """;

        var result = await _sut.VerifyAssertionAsync(
            responseJson,
            "{\"challenge\":\"AQ\"}",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Same(user, result.User);
        Assert.False(result.UserVerified);
    }
}
