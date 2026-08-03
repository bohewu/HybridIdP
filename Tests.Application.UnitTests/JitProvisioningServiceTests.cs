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

        _userManagerMock.Setup(um => um.IsInRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        _userManagerMock.Setup(um => um.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        _service = new JitProvisioningService(
            _userManagerMock.Object,
            _context,
            Options.Create(new Core.Application.Options.ExternalLoginOptions
            {
                AutoProvisionDefaultRole = Core.Domain.Constants.AuthConstants.Roles.User
            }));
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
            EmailVerified = true,
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
        Assert.True(result.EmailConfirmed);
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
    public async Task ProvisionExternalUser_FirstTimeLogin_ShouldAssignDefaultRole()
    {
        var externalAuth = new ExternalAuthResult
        {
            Provider = "Google",
            ProviderKey = "google-user-001",
            Email = "new.user@company.com",
            EmailVerified = true,
            FirstName = "New",
            LastName = "User"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(externalAuth.Provider, externalAuth.ProviderKey))
            .ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(um => um.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        _userManagerMock.Setup(um => um.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        await _service.ProvisionExternalUserAsync(externalAuth);

        _userManagerMock.Verify(
            um => um.AddToRoleAsync(It.IsAny<ApplicationUser>(), Core.Domain.Constants.AuthConstants.Roles.User),
            Times.Once);
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
            EmailVerified = true,
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
    public async Task ProvisionExternalUser_TerminalProviderLoginMatch_ShouldRejectWithoutMutatingOrSaving()
    {
        var terminalUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "retained-user",
            Email = "retained@example.com",
            FirstName = "Retained",
            PersonId = Guid.NewGuid(),
            IsActive = false,
            IsDeleted = false
        };
        var externalAuth = new ExternalAuthResult
        {
            Provider = "Google",
            ProviderKey = "retained-provider-key",
            Email = "attacker@example.com",
            EmailVerified = true,
            FirstName = "Attacker"
        };
        var contextMock = new Mock<IApplicationDbContext>(MockBehavior.Strict);
        var service = new JitProvisioningService(
            _userManagerMock.Object,
            contextMock.Object,
            Options.Create(new Core.Application.Options.ExternalLoginOptions()));
        _userManagerMock.Setup(manager => manager.FindByLoginAsync(externalAuth.Provider, externalAuth.ProviderKey))
            .ReturnsAsync(terminalUser);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProvisionExternalUserAsync(externalAuth));

        Assert.Equal("User account is unavailable.", exception.Message);
        Assert.Equal("retained@example.com", terminalUser.Email);
        Assert.Equal("Retained", terminalUser.FirstName);
        Assert.Equal("retained-user", terminalUser.UserName);
        _userManagerMock.Verify(manager => manager.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _userManagerMock.Verify(manager => manager.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _userManagerMock.Verify(manager => manager.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        _userManagerMock.Verify(manager => manager.FindByNameAsync(It.IsAny<string>()), Times.Never);
        contextMock.VerifyGet(context => context.Persons, Times.Never);
        contextMock.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionExternalUser_TerminalUsernameMatch_ShouldRejectBeforePersonLookupOrMutation()
    {
        var terminalUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "retained@example.com",
            Email = "retained@example.com",
            FirstName = "Retained",
            IsActive = true,
            IsDeleted = true
        };
        var externalAuth = new ExternalAuthResult
        {
            Provider = "Microsoft",
            ProviderKey = "new-provider-key",
            Email = terminalUser.UserName,
            EmailVerified = true,
            FirstName = "Attacker"
        };
        var contextMock = new Mock<IApplicationDbContext>(MockBehavior.Strict);
        var service = new JitProvisioningService(
            _userManagerMock.Object,
            contextMock.Object,
            Options.Create(new Core.Application.Options.ExternalLoginOptions()));
        _userManagerMock.Setup(manager => manager.FindByLoginAsync(externalAuth.Provider, externalAuth.ProviderKey))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(manager => manager.FindByNameAsync(terminalUser.UserName))
            .ReturnsAsync(terminalUser);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProvisionExternalUserAsync(externalAuth));

        Assert.Equal("User account is unavailable.", exception.Message);
        Assert.Null(terminalUser.PersonId);
        Assert.Equal("retained@example.com", terminalUser.Email);
        Assert.Equal("Retained", terminalUser.FirstName);
        _userManagerMock.Verify(manager => manager.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _userManagerMock.Verify(manager => manager.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _userManagerMock.Verify(manager => manager.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        contextMock.VerifyGet(context => context.Persons, Times.Never);
        contextMock.VerifyGet(context => context.Users, Times.Never);
        contextMock.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionExternalUser_ActiveUsernameMatch_ShouldLinkExistingUser()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "eligible@example.com",
            Email = "eligible@example.com",
            IsActive = true,
            IsDeleted = false
        };
        var externalAuth = new ExternalAuthResult
        {
            Provider = "Google",
            ProviderKey = "eligible-provider-key",
            Email = existingUser.UserName,
            EmailVerified = true,
            FirstName = "Eligible"
        };
        _userManagerMock.Setup(manager => manager.FindByLoginAsync(externalAuth.Provider, externalAuth.ProviderKey))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(manager => manager.FindByNameAsync(existingUser.UserName))
            .ReturnsAsync(existingUser);
        _userManagerMock.Setup(manager => manager.UpdateAsync(existingUser))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(manager => manager.AddLoginAsync(existingUser, It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        Assert.Same(existingUser, result);
        Assert.NotNull(existingUser.PersonId);
        Assert.Single(await _context.Persons.ToListAsync());
        _userManagerMock.Verify(manager => manager.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _userManagerMock.Verify(manager => manager.UpdateAsync(existingUser), Times.Once);
        _userManagerMock.Verify(manager => manager.AddLoginAsync(existingUser, It.IsAny<UserLoginInfo>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionExternalUser_ExistingLoginWithUnverifiedEmail_ShouldPreserveStoredEmail()
    {
        var personId = Guid.NewGuid();
        var existingPerson = new Person
        {
            Id = personId,
            Email = "trusted@company.com",
            FirstName = "Existing",
            CreatedAt = DateTime.UtcNow
        };
        await _context.Persons.AddAsync(existingPerson);
        await _context.SaveChangesAsync();

        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "trusted@company.com",
            Email = "trusted@company.com",
            EmailConfirmed = true,
            PersonId = personId
        };
        var externalAuth = new ExternalAuthResult
        {
            Provider = "CustomProvider",
            ProviderKey = "existing-provider-key",
            Email = "unverified@attacker.example",
            EmailVerified = false,
            FirstName = "Updated"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(externalAuth.Provider, externalAuth.ProviderKey))
            .ReturnsAsync(existingUser);
        _userManagerMock.Setup(um => um.UpdateAsync(existingUser))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        Assert.Equal("trusted@company.com", result.Email);
        Assert.True(result.EmailConfirmed);
        Assert.Equal("trusted@company.com", existingPerson.Email);
        Assert.Equal("Updated", result.FirstName);
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
            EmailVerified = true,
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
    public async Task ProvisionExternalUser_UnverifiedEmail_ShouldNotBindToExistingPerson()
    {
        var existingPerson = new Person
        {
            Id = Guid.NewGuid(),
            Email = "victim@company.com",
            FirstName = "Victim",
            CreatedAt = DateTime.UtcNow
        };
        await _context.Persons.AddAsync(existingPerson);
        await _context.SaveChangesAsync();

        var externalAuth = new ExternalAuthResult
        {
            Provider = "CustomProvider",
            ProviderKey = "attacker-provider-key",
            Email = "victim@company.com",
            FirstName = "Attacker"
        };

        _userManagerMock.Setup(um => um.FindByLoginAsync(externalAuth.Provider, externalAuth.ProviderKey))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(um => um.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(um => um.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _service.ProvisionExternalUserAsync(externalAuth);

        Assert.NotEqual(existingPerson.Id, result.PersonId);
        Assert.Equal("CustomProvider_attacker-provider-key", result.UserName);
        Assert.False(result.EmailConfirmed);
        Assert.Equal(2, await _context.Persons.CountAsync());
        var isolatedPerson = await _context.Persons.SingleAsync(person => person.Id == result.PersonId);
        Assert.Null(isolatedPerson.Email);
        _userManagerMock.Verify(um => um.FindByNameAsync(It.IsAny<string>()), Times.Never);
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
            EmailVerified = true,
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
            EmailVerified = true,
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
            EmailVerified = true,
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
