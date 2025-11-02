using Core.Application.DTOs;
using Core.Domain;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace Tests.Application.UnitTests;

public class JitProvisioningServiceTests
{
    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null, null, null, null, null, null, null, null);
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
