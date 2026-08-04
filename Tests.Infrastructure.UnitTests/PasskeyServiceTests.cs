using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Application.DTOs;
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
    private readonly Mock<ILogger<PasskeyService>> _loggerMock;
    private readonly PasskeyService _sut;

    public PasskeyServiceTests()
    {
        _fido2Mock = new Mock<IFido2>();
        
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null, null, null, null, null, null, null, null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
        
        _loggerMock = new Mock<ILogger<PasskeyService>>();
        
        _sut = new PasskeyService(
            _fido2Mock.Object,
            _userManagerMock.Object,
            _dbContext,
            _loggerMock.Object);
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

        _dbContext.UserCredentials.AddRange(cred1, cred2, otherCred);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetUserPasskeysAsync(userId, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == 2); // Ordered descending by RegDate
        Assert.Equal("Device 2", result[0].DeviceName);
        Assert.Equal("Device 1", result[1].DeviceName);
    }

    [Fact]
    public async Task DeletePasskeyAsync_ExistingPasskey_ReturnsTrueAndDeletes()
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
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeletePasskeyAsync(userId, 1, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.Empty(_dbContext.UserCredentials);
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
        var options = new CredentialCreateOptions 
        { 
            Challenge = new byte[] { 1, 2, 3 },
            User = new Fido2User { Id = Encoding.UTF8.GetBytes("testuser") },
            PubKeyCredParams = new List<PubKeyCredParam>(),
            Rp = new PublicKeyCredentialRpEntity("localhost", "HybridIdP")
        };
        
        _fido2Mock.Setup(x => x.RequestNewCredential(It.IsAny<RequestNewCredentialParams>()))
            .Returns(options);

        // Act
        var result = await _sut.GetRegistrationOptionsAsync(user, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(options.Challenge, result.Challenge);
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
