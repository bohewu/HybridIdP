using Core.Application;
using Core.Application.DTOs;
using Core.Domain;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Application.UnitTests;

public class RoleManagementServiceTests
{
    private static RoleManagementService CreateService(
        IEnumerable<ApplicationRole> roles,
        out Mock<RoleManager<ApplicationRole>> roleManagerMock,
        out Mock<UserManager<ApplicationUser>> userManagerMock)
    {
        // Mock RoleStore and support IQueryable
        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        var queryableRoleStore = roleStore.As<IQueryableRoleStore<ApplicationRole>>();
        // Provide simple in-memory queryable; service falls back to sync path in tests
        queryableRoleStore.Setup(s => s.Roles).Returns(roles.AsQueryable());

        roleManagerMock = new Mock<RoleManager<ApplicationRole>>(
            queryableRoleStore.Object,
            new IRoleValidator<ApplicationRole>[0],
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<ApplicationRole>>>().Object);
        // Ensure Roles returns our in-memory list
        roleManagerMock.Setup(x => x.Roles).Returns(roles.AsQueryable());

        // Mock UserManager
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            Options.Create(new IdentityOptions()),
            new Mock<IPasswordHasher<ApplicationUser>>().Object,
            new IUserValidator<ApplicationUser>[0],
            new IPasswordValidator<ApplicationUser>[0],
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<ApplicationUser>>>().Object);

        // Default: no users in role
        userManagerMock
            .Setup(x => x.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<ApplicationUser>());

        return new RoleManagementService(roleManagerMock.Object, userManagerMock.Object);
    }

    [Fact]
    public async Task GetRoles_Pagination_Works()
    {
        // Arrange
        var roles = new List<ApplicationRole>();
        for (int i = 1; i <= 30; i++)
        {
            roles.Add(new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = $"Role{i:00}",
                Description = i % 2 == 0 ? "Even role" : "Odd role",
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            });
        }

        var service = CreateService(roles, out _, out _);

        // Act
        var page = await service.GetRolesAsync(skip: 5, take: 10);

        // Assert
        Assert.Equal(30, page.TotalCount);
        Assert.Equal(10, page.Items.Count);
    Assert.Equal(5, page.Skip);
    Assert.Equal(10, page.Take);
    }

    [Fact]
    public async Task GetRoles_Search_FiltersByNameOrDescription()
    {
        // Arrange
        var roles = new List<ApplicationRole>
        {
            new() { Id = Guid.NewGuid(), Name = "Admins", Description = "System administrators", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Name = "Editors", Description = "Content team", CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Id = Guid.NewGuid(), Name = "Guests", Description = "Read only", CreatedAt = DateTime.UtcNow.AddDays(-3) }
        };

        var service = CreateService(roles, out _, out _);

        // Act
        var byName = await service.GetRolesAsync(skip: 0, take: 10, search: "admin");
        var byDesc = await service.GetRolesAsync(skip: 0, take: 10, search: "content");

        // Assert
        Assert.Single(byName.Items);
        Assert.Equal("Admins", byName.Items[0].Name);
        Assert.Single(byDesc.Items);
        Assert.Equal("Editors", byDesc.Items[0].Name);
    }

    [Fact]
    public async Task GetRoles_SortByCreatedAt_Descending_Works()
    {
        // Arrange
        var roles = new List<ApplicationRole>
        {
            new() { Id = Guid.NewGuid(), Name = "Zeta", CreatedAt = new DateTime(2024, 1, 1) },
            new() { Id = Guid.NewGuid(), Name = "Alpha", CreatedAt = new DateTime(2025, 1, 1) },
            new() { Id = Guid.NewGuid(), Name = "Beta",  CreatedAt = new DateTime(2023, 1, 1) }
        };

        var service = CreateService(roles, out _, out _);

        // Act
        var page = await service.GetRolesAsync(skip: 0, take: 10, search: null, sortBy: "createdat", sortDirection: "desc");

        // Assert
        Assert.Equal(3, page.Items.Count);
        Assert.Equal("Alpha", page.Items[0].Name); // newest first
    }
}
