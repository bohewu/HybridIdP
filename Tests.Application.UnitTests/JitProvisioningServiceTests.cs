using Core.Application.DTOs;
using Core.Domain;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace Tests.Application.UnitTests;

public class JitProvisioningServiceTests
{
    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var options = new Mock<IOptions<IdentityOptions>>();
        var hasher = new Mock<IPasswordHasher<ApplicationUser>>();
        var userValidators = new List<IUserValidator<ApplicationUser>>();
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>>();
        var normalizer = new Mock<ILookupNormalizer>();
        var errors = new IdentityErrorDescriber();
        var services = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<UserManager<ApplicationUser>>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, options.Object, hasher.Object, userValidators, passwordValidators, normalizer.Object, errors, services.Object, logger.Object);
    }

    [Fact]
    public async Task ProvisionUser_When_User_Is_New_Should_Call_CreateAsync()
    {
        // Arrange
        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(um => um.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        userManagerMock.Setup(um => um.AddClaimsAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<Claim>>()))
            .ReturnsAsync(IdentityResult.Success);

        var service = new JitProvisioningService(userManagerMock.Object);
        var dto = new LegacyUserDto
        {
            IsAuthenticated = true,
            FullName = "Jane Doe",
            Email = "jane@example.com",
            ExternalId = "legacy-123"
        };

        // Act
        try
        {
            await service.ProvisionUserAsync(dto);
        }
        catch (NotImplementedException)
        {
            // Expected in TDD Red phase; allow test to continue to Verify
        }

        // Assert
        userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionUser_When_User_Exists_Should_Call_UpdateAsync()
    {
        // Arrange
        var existing = new ApplicationUser { UserName = "jane@example.com", Email = "jane@example.com" };

        var userManagerMock = CreateUserManagerMock();
        userManagerMock.Setup(um => um.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync(existing);
        userManagerMock.Setup(um => um.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        userManagerMock.Setup(um => um.AddClaimsAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<Claim>>()))
            .ReturnsAsync(IdentityResult.Success);

        var service = new JitProvisioningService(userManagerMock.Object);
        var dto = new LegacyUserDto
        {
            IsAuthenticated = true,
            FullName = "Jane Doe",
            Email = "jane@example.com",
            ExternalId = "legacy-123"
        };

        // Act
        try
        {
            await service.ProvisionUserAsync(dto);
        }
        catch (NotImplementedException)
        {
            // Expected in TDD Red phase; allow test to continue to Verify
        }

        // Assert
        userManagerMock.Verify(um => um.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }
}
