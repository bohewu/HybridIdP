using Core.Application.DTOs;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests.Application.UnitTests;

public class JitProvisioningService_LegacyTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly JitProvisioningService _service;
    private readonly ApplicationDbContext _context;

    public JitProvisioningService_LegacyTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(_options);

        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null, null, null, null, null, null, null, null);

        _service = new JitProvisioningService(_userManagerMock.Object, _context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task ProvisionExternalUser_LegacyProvider_ShouldAutoVerifyPerson()
    {
        // Arrange
        var externalAuth = new ExternalAuthResult
        {
            Provider = "Legacy",
            ProviderKey = "legacy_user",
            Email = "legacy@example.com",
            FirstName = "Legacy User",
            LastName = "", // Legacy often puts full name in first name
            NationalId = "A123456789"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync("Legacy", "legacy_user"))
            .ReturnsAsync((ApplicationUser?)null); // New user

        _userManagerMock.Setup(um => um.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
            
        _userManagerMock.Setup(um => um.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        var person = await _context.Persons.FirstOrDefaultAsync(p => p.Email == externalAuth.Email);
        Assert.NotNull(person);
        Assert.NotNull(person.IdentityVerifiedAt); // Should be auto-verified
        Assert.Equal("NationalId", person.IdentityDocumentType);
    }

    [Fact]
    public async Task ProvisionExternalUser_ExistingUserLogin_ShouldUpdateLinkedPerson()
    {
        // Arrange
        var personId = Guid.NewGuid();
        var existingPerson = new Person
        {
            Id = personId,
            Email = "user@example.com",
            FirstName = "OldName",
            IdentityVerifiedAt = null
        };
        _context.Persons.Add(existingPerson);
        await _context.SaveChangesAsync();

        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PersonId = personId,
            FirstName = "OldName"
        };

        var externalAuth = new ExternalAuthResult
        {
            Provider = "Legacy",
            ProviderKey = "user_123",
            Email = "user@example.com",
            FirstName = "NewName", // Name changed
            NationalId = "A123456789"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync("Legacy", "user_123"))
            .ReturnsAsync(existingUser); // Existing user found by login

        _userManagerMock.Setup(um => um.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        // 1. Verify ApplicationUser updated
        Assert.Equal("NewName", existingUser.FirstName);

        // 2. Verify Person updated (This was the missing logic)
        var updatedPerson = await _context.Persons.FindAsync(personId);
        Assert.Equal("NewName", updatedPerson.FirstName);
        Assert.NotNull(updatedPerson.IdentityVerifiedAt); // Should be auto-verified because it's Legacy
    }
}
