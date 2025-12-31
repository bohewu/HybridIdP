using Core.Application;
using Core.Application.DTOs;
using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Tests.Application.UnitTests;

public class JitProvisioningServiceTests : IDisposable
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly ApplicationDbContext _context;
    private readonly JitProvisioningService _service;

    public JitProvisioningServiceTests()
    {
        _userManagerMock = CreateUserManagerMock();
        _context = CreateInMemoryDbContext();
        _service = new JitProvisioningService(_userManagerMock.Object, _context);
    }

    [Fact]
    public async Task ProvisionExternalUser_FirstTimeLogin_ShouldCreatePersonAndUser()
    {
        // Arrange
        var externalAuth = new ExternalAuthResult
        {
            Provider = "ActiveDirectory",
            ProviderKey = "john.doe@ad",
            Email = "john.doe@company.com",
            FirstName = "John",
            LastName = "Doe",
            EmployeeId = "EMP001",
            Department = "IT",
            JobTitle = "Developer"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            "ActiveDirectory",
            "john.doe@ad"
        )).ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("john.doe@company.com", result.Email);
        Assert.Equal("John", result.FirstName);
        Assert.Equal("Doe", result.LastName);
        Assert.NotNull(result.PersonId);
        
        _userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Once);
        _userManagerMock.Verify(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        ), Times.Once);
        
        // Verify Person was created
        var person = await _context.Persons.FirstOrDefaultAsync(p => p.Email == "john.doe@company.com");
        Assert.NotNull(person);
        Assert.Equal("John", person.FirstName);
        Assert.Equal("EMP001", person.EmployeeId);
    }

    [Fact]
    public async Task ProvisionExternalUser_ExistingLogin_ShouldUpdateUser()
    {
        // Arrange
        var existingUser = new ApplicationUser 
        { 
            Id = Guid.NewGuid(),
            UserName = "john.doe@company.com",
            Email = "john.doe@company.com",
            FirstName = "John",
            LastName = "Doe"
        };

        var externalAuth = new ExternalAuthResult
        {
            Provider = "ActiveDirectory",
            ProviderKey = "john.doe@ad",
            Email = "john.doe@newdomain.com",
            FirstName = "John",
            LastName = "Doe",
            Department = "Engineering"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            "ActiveDirectory",
            "john.doe@ad"
        )).ReturnsAsync(existingUser);

        _userManagerMock.Setup(um => um.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingUser.Id, result.Id);
        Assert.Equal("john.doe@newdomain.com", result.Email);
        Assert.Equal("Engineering", result.Department);
        
        _userManagerMock.Verify(um => um.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Once);
        _userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionExternalUser_SameEmailDifferentProvider_ShouldUseSamePerson()
    {
        // Arrange
        var existingPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = "john.doe@company.com",
            FirstName = "John",
            LastName = "Doe",
            CreatedAt = DateTime.UtcNow
        };
        await _context.Persons.AddAsync(existingPerson);
        await _context.SaveChangesAsync();

        var externalAuth = new ExternalAuthResult
        {
            Provider = "Google",
            ProviderKey = "google-id-123",
            Email = "john.doe@company.com",
            FirstName = "John",
            LastName = "Doe"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            "Google",
            "google-id-123"
        )).ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingPerson.Id, result.PersonId);
        
        _userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Once);
        
        // Verify no new Person was created
        var personCount = await _context.Persons.CountAsync();
        Assert.Equal(1, personCount);
    }

    [Fact]
    public async Task ProvisionExternalUser_MatchByNationalId_ShouldUseSamePerson()
    {
        // Arrange
        // The service now stores hashed NationalId, so we need to store the hash in test data
        var existingPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = "john@oldmail.com",
            FirstName = "John",
            LastName = "Doe",
            NationalId = PidHasher.Hash("A123456789"), // Store hashed value
            CreatedAt = DateTime.UtcNow
        };
        await _context.Persons.AddAsync(existingPerson);
        await _context.SaveChangesAsync();

        var externalAuth = new ExternalAuthResult
        {
            Provider = "ActiveDirectory",
            ProviderKey = "john.doe@ad",
            Email = "john.doe@company.com", // Different email
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789" // Same NationalId - should match!
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingPerson.Id, result.PersonId);
        
        // Verify no new Person was created (matched by NationalId despite different email)
        var personCount = await _context.Persons.CountAsync();
        Assert.Equal(1, personCount);
    }

    [Fact]
    public async Task ProvisionExternalUser_ExistingPerson_ShouldUpdateFields()
    {
        // Arrange - Create Person with minimal info
        var existingPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = "john@company.com",
            FirstName = "John",
            LastName = null, // Missing last name
            Department = null, // Missing department
            CreatedAt = DateTime.UtcNow
        };
        await _context.Persons.AddAsync(existingPerson);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear(); // Clear tracker to simulate new request

        var externalAuth = new ExternalAuthResult
        {
            Provider = "ActiveDirectory",
            ProviderKey = "john@ad",
            Email = "john@company.com",
            FirstName = "John",
            LastName = "Doe", // New data
            Department = "IT", // New data
            NationalId = "A123456789" // New PID data
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(um => um.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingPerson.Id, result.PersonId);
        
        // Verify Person was updated
        var updatedPerson = await _context.Persons.FirstOrDefaultAsync(p => p.Id == existingPerson.Id);
        Assert.NotNull(updatedPerson);
        Assert.Equal("Doe", updatedPerson.LastName); // Should be updated
        Assert.Equal("IT", updatedPerson.Department); // Should be updated
        Assert.NotNull(updatedPerson.NationalId); // Should be set (hashed)
        Assert.NotNull(updatedPerson.ModifiedAt); // Should have modification timestamp
    }

    [Fact]
    public async Task ProvisionExternalUser_ExistingPerson_ShouldNotOverwriteExistingPidFields()
    {
        // Arrange - Create Person with existing NationalId
        var existingPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = "john@company.com",
            FirstName = "John",
            NationalId = PidHasher.Hash("A123456789"), // Already has NationalId
            CreatedAt = DateTime.UtcNow
        };
        await _context.Persons.AddAsync(existingPerson);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var externalAuth = new ExternalAuthResult
        {
            Provider = "ActiveDirectory",
            ProviderKey = "john@ad",
            Email = "john@company.com",
            FirstName = "John",
            NationalId = "B987654321" // Different NationalId - should NOT overwrite!
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(um => um.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert - NationalId should NOT be overwritten
        var updatedPerson = await _context.Persons.FirstOrDefaultAsync(p => p.Id == existingPerson.Id);
        Assert.NotNull(updatedPerson);
        Assert.Equal(PidHasher.Hash("A123456789"), updatedPerson.NationalId); // Original value preserved
    }

    [Fact]
    public async Task ProvisionExternalUser_NoEmail_ShouldUseProviderKeyAsUsername()
    {
        // Arrange
        var externalAuth = new ExternalAuthResult
        {
            Provider = "CustomProvider",
            ProviderKey = "custom-user-123",
            Email = null, // No email
            FirstName = "Anonymous",
            LastName = "User"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<ApplicationUser>(user =>
            {
                // Verify username format
                Assert.Equal("CustomProvider_custom-user-123", user.UserName);
                Assert.Null(user.Email);
                Assert.False(user.EmailConfirmed);
            });

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        // Assert
        Assert.NotNull(result);
        _userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionUserAsync_LegacyMethod_ShouldStillWork()
    {
        // Arrange
        var legacyDto = new LegacyUserDto
        {
            IsAuthenticated = true,
            Email = "legacy@example.com",
            FullName = "Legacy User",
            ExternalId = "legacy-123",
            Department = "Sales"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(
            It.IsAny<string>(),
            It.IsAny<string>()
        )).ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.AddLoginAsync(
            It.IsAny<ApplicationUser>(),
            It.IsAny<UserLoginInfo>()
        )).ReturnsAsync(IdentityResult.Success);

        // Act
#pragma warning disable CS0618 // Type or member is obsolete
        var result = await _service.ProvisionUserAsync(legacyDto);
#pragma warning restore CS0618

        // Assert
        Assert.NotNull(result);
        Assert.Equal("legacy@example.com", result.Email);
        Assert.Equal("Legacy User", result.FirstName);
        
        _userManagerMock.Verify(um => um.CreateAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionExternalUser_NullProvider_ShouldThrowArgumentException()
    {
        // Arrange
        var externalAuth = new ExternalAuthResult
        {
            Provider = null!,
            ProviderKey = "key-123",
            Email = "test@example.com"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _service.ProvisionExternalUserAsync(externalAuth)
        );
    }

    [Fact]
    public async Task ProvisionExternalUser_EmptyProviderKey_ShouldThrowArgumentException()
    {
        // Arrange
        var externalAuth = new ExternalAuthResult
        {
            Provider = "Google",
            ProviderKey = "",
            Email = "test@example.com"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _service.ProvisionExternalUserAsync(externalAuth)
        );
    }

    // Helper methods
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
            store.Object, options.Object, hasher.Object, 
            userValidators, passwordValidators, normalizer.Object, 
            errors, services.Object, logger.Object
        );
    }

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        
        return new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
