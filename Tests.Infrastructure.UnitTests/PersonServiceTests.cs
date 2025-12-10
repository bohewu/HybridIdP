using Core.Application;
using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// Unit tests for PersonService
/// Phase 10.2: Services & API
/// </summary>
public class PersonServiceTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly Mock<ILogger<PersonService>> _loggerMock;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;

    public PersonServiceTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _loggerMock = new Mock<ILogger<PersonService>>();
        _auditServiceMock = new Mock<IAuditService>();
        
        // Create UserManager mock
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
    
    private PersonService CreateService(ApplicationDbContext context)
    {
        return new PersonService(context, _loggerMock.Object, _auditServiceMock.Object, _userManagerMock.Object);
    }

    public void Dispose()
    {
        // Cleanup in-memory database
        using var context = new ApplicationDbContext(_options);
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task CreatePersonAsync_ShouldCreatePerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            EmployeeId = "EMP001"
        };

        // Act
        var result = await service.CreatePersonAsync(person, Guid.NewGuid());

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("John", result.FirstName);
        Assert.Equal("Doe", result.LastName);
        Assert.Equal("EMP001", result.EmployeeId);
        Assert.NotNull(result.CreatedBy);
        Assert.NotEqual(default(DateTime), result.CreatedAt);
    }

    [Fact]
    public async Task CreatePersonAsync_WithDuplicateEmployeeId_ShouldThrowException()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person1 = new Person { FirstName = "John", LastName = "Doe", EmployeeId = "EMP001" };
        await service.CreatePersonAsync(person1);

        var person2 = new Person { FirstName = "Jane", LastName = "Smith", EmployeeId = "EMP001" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.CreatePersonAsync(person2));
    }

    [Fact]
    public async Task GetPersonByIdAsync_ShouldReturnPerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var created = await service.CreatePersonAsync(person);

        // Act
        var result = await service.GetPersonByIdAsync(created.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(created.Id, result.Id);
        Assert.Equal("John", result.FirstName);
        Assert.Equal("Doe", result.LastName);
    }

    [Fact]
    public async Task GetPersonByIdAsync_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        // Act
        var result = await service.GetPersonByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPersonByEmployeeIdAsync_ShouldReturnPerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe", EmployeeId = "EMP001" };
        await service.CreatePersonAsync(person);

        // Act
        var result = await service.GetPersonByEmployeeIdAsync("EMP001");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("EMP001", result.EmployeeId);
        Assert.Equal("John", result.FirstName);
    }

    [Fact]
    public async Task UpdatePersonAsync_ShouldUpdatePerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe", EmployeeId = "EMP001" };
        var created = await service.CreatePersonAsync(person);

        var updates = new Person
        {
            FirstName = "Jane",
            LastName = "Smith",
            EmployeeId = "EMP002",
            Department = "IT"
        };

        // Act
        var result = await service.UpdatePersonAsync(created.Id, updates, Guid.NewGuid());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Jane", result.FirstName);
        Assert.Equal("Smith", result.LastName);
        Assert.Equal("EMP002", result.EmployeeId);
        Assert.Equal("IT", result.Department);
        Assert.NotNull(result.ModifiedAt);
        Assert.NotNull(result.ModifiedBy);
    }

    [Fact]
    public async Task UpdatePersonAsync_WithDuplicateEmployeeId_ShouldThrowException()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person1 = new Person { FirstName = "John", LastName = "Doe", EmployeeId = "EMP001" };
        await service.CreatePersonAsync(person1);

        var person2 = new Person { FirstName = "Jane", LastName = "Smith", EmployeeId = "EMP002" };
        var created2 = await service.CreatePersonAsync(person2);

        var updates = new Person { FirstName = "Jane", LastName = "Smith", EmployeeId = "EMP001" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.UpdatePersonAsync(created2.Id, updates));
    }

    [Fact]
    public async Task DeletePersonAsync_ShouldDeletePerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var created = await service.CreatePersonAsync(person);

        // Act
        var result = await service.DeletePersonAsync(created.Id);

        // Assert
        Assert.True(result);

        var deleted = await service.GetPersonByIdAsync(created.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeletePersonAsync_WithInvalidId_ShouldReturnFalse()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        // Act
        var result = await service.DeletePersonAsync(Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetAllPersonsAsync_ShouldReturnPersons()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        await service.CreatePersonAsync(new Person { FirstName = "Alice", LastName = "Anderson" });
        await service.CreatePersonAsync(new Person { FirstName = "Bob", LastName = "Brown" });
        await service.CreatePersonAsync(new Person { FirstName = "Charlie", LastName = "Cooper" });

        // Act
        var result = await service.GetAllPersonsAsync(0, 10);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("Anderson", result[0].LastName); // Sorted by last name
        Assert.Equal("Brown", result[1].LastName);
        Assert.Equal("Cooper", result[2].LastName);
    }

    [Fact]
    public async Task GetAllPersonsAsync_WithPagination_ShouldReturnPagedResults()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        for (int i = 0; i < 10; i++)
        {
            await service.CreatePersonAsync(new Person 
            { 
                FirstName = $"Person{i}", 
                LastName = $"LastName{i:D2}" 
            });
        }

        // Act
        var page1 = await service.GetAllPersonsAsync(0, 3);
        var page2 = await service.GetAllPersonsAsync(3, 3);

        // Assert
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.NotEqual(page1[0].Id, page2[0].Id);
    }

    [Fact]
    public async Task SearchPersonsAsync_ShouldFindPersonsByName()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        await service.CreatePersonAsync(new Person { FirstName = "John", LastName = "Doe" });
        await service.CreatePersonAsync(new Person { FirstName = "Jane", LastName = "Smith" });
        await service.CreatePersonAsync(new Person { FirstName = "John", LastName = "Johnson" });

        // Act
        var result = await service.SearchPersonsAsync("John");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Contains("John", p.FirstName + p.LastName));
    }

    [Fact]
    public async Task SearchPersonsAsync_ByEmployeeId_ShouldFindPerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        await service.CreatePersonAsync(new Person 
        { 
            FirstName = "John", 
            LastName = "Doe", 
            EmployeeId = "EMP001" 
        });
        await service.CreatePersonAsync(new Person 
        { 
            FirstName = "Jane", 
            LastName = "Smith", 
            EmployeeId = "EMP002" 
        });

        // Act
        var result = await service.SearchPersonsAsync("EMP001");

        // Assert
        Assert.Single(result);
        Assert.Equal("EMP001", result[0].EmployeeId);
    }

    [Fact]
    public async Task GetPersonsCountAsync_ShouldReturnCount()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        await service.CreatePersonAsync(new Person { FirstName = "John", LastName = "Doe" });
        await service.CreatePersonAsync(new Person { FirstName = "Jane", LastName = "Smith" });

        // Act
        var count = await service.GetPersonsCountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_ShouldLinkAccount()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson = await service.CreatePersonAsync(person);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "johndoe",
            Email = "john@example.com"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await service.LinkAccountToPersonAsync(createdPerson.Id, user.Id, Guid.NewGuid());

        // Assert
        Assert.True(result);

        var updatedUser = await context.Users.FindAsync(user.Id);
        Assert.Equal(createdPerson.Id, updatedUser!.PersonId);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_WithAlreadyLinkedUser_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person1 = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson1 = await service.CreatePersonAsync(person1);

        var person2 = new Person { FirstName = "Jane", LastName = "Smith" };
        var createdPerson2 = await service.CreatePersonAsync(person2);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        // Link user to person1
        await service.LinkAccountToPersonAsync(createdPerson1.Id, user.Id, Guid.NewGuid());

        // Act & Assert - Attempt to link same user to person2 should throw
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.LinkAccountToPersonAsync(createdPerson2.Id, user.Id, Guid.NewGuid()));

        Assert.Contains("already linked", exception.Message);
        Assert.Contains(createdPerson1.Id.ToString(), exception.Message);

        // Verify user is still linked to person1
        var updatedUser = await context.Users.FindAsync(user.Id);
        Assert.Equal(createdPerson1.Id, updatedUser!.PersonId);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_WithSamePersonTwice_ShouldBeIdempotent()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson = await service.CreatePersonAsync(person);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        // Link user to person
        var result1 = await service.LinkAccountToPersonAsync(createdPerson.Id, user.Id, Guid.NewGuid());

        // Act - Link same user to same person again (should succeed and be idempotent)
        var result2 = await service.LinkAccountToPersonAsync(createdPerson.Id, user.Id, Guid.NewGuid());

        // Assert
        Assert.True(result1);
        Assert.True(result2);

        var updatedUser = await context.Users.FindAsync(user.Id);
        Assert.Equal(createdPerson.Id, updatedUser!.PersonId);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_WithNonExistentPerson_ShouldReturnFalse()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await service.LinkAccountToPersonAsync(Guid.NewGuid(), user.Id, Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_WithNonExistentUser_ShouldReturnFalse()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson = await service.CreatePersonAsync(person);

        // Act
        var result = await service.LinkAccountToPersonAsync(createdPerson.Id, Guid.NewGuid(), Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UnlinkAccountFromPersonAsync_ShouldUnlinkAccount()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson = await service.CreatePersonAsync(person);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "johndoe",
            Email = "john@example.com",
            PersonId = createdPerson.Id
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var result = await service.UnlinkAccountFromPersonAsync(user.Id, Guid.NewGuid());

        // Assert
        Assert.True(result);

        var updatedUser = await context.Users.FindAsync(user.Id);
        Assert.Null(updatedUser!.PersonId);
    }

    [Fact]
    public async Task GetPersonAccountsAsync_ShouldReturnLinkedAccounts()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson = await service.CreatePersonAsync(person);

        var user1 = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "johndoe.contract",
            Email = "john.contract@example.com",
            PersonId = createdPerson.Id
        };
        var user2 = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "johndoe",
            Email = "john@example.com",
            PersonId = createdPerson.Id
        };

        context.Users.Add(user1);
        context.Users.Add(user2);
        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var accounts = await service.GetPersonAccountsAsync(createdPerson.Id);

        // Assert
        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.UserName == "johndoe.contract");
        Assert.Contains(accounts, a => a.UserName == "johndoe");
    }

    // Phase 10.5: Audit Event Logging Tests

    [Fact]
    public async Task CreatePersonAsync_ShouldLogAuditEvent()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            EmployeeId = "EMP001"
        };
        var createdBy = Guid.NewGuid();

        // Act
        await service.CreatePersonAsync(person, createdBy);

        // Assert
        _auditServiceMock.Verify(
            a => a.LogEventAsync(
                "PersonCreated",
                createdBy.ToString(),
                It.IsAny<string>(),
                null,
                null),
            Times.Once);
    }

    [Fact]
    public async Task UpdatePersonAsync_ShouldLogAuditEvent()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var created = await service.CreatePersonAsync(person);
        
        _auditServiceMock.Reset(); // Clear previous audit calls

        var updatedPerson = new Person
        {
            FirstName = "Jane",
            LastName = "Smith",
            EmployeeId = "EMP002"
        };
        var modifiedBy = Guid.NewGuid();

        // Act
        await service.UpdatePersonAsync(created.Id, updatedPerson, modifiedBy);

        // Assert
        _auditServiceMock.Verify(
            a => a.LogEventAsync(
                "PersonUpdated",
                modifiedBy.ToString(),
                It.IsAny<string>(),
                null,
                null),
            Times.Once);
    }

    [Fact]
    public async Task DeletePersonAsync_ShouldLogAuditEvent()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var created = await service.CreatePersonAsync(person);
        
        _auditServiceMock.Reset(); // Clear previous audit calls

        // Act
        await service.DeletePersonAsync(created.Id);

        // Assert
        _auditServiceMock.Verify(
            a => a.LogEventAsync(
                "PersonDeleted",
                null,
                It.IsAny<string>(),
                null,
                null),
            Times.Once);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_ShouldLogAuditEvent()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson = await service.CreatePersonAsync(person);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "johndoe",
            Email = "john@example.com"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        _auditServiceMock.Reset(); // Clear previous audit calls
        var linkedBy = Guid.NewGuid();

        // Act
        await service.LinkAccountToPersonAsync(createdPerson.Id, user.Id, linkedBy);

        // Assert
        _auditServiceMock.Verify(
            a => a.LogEventAsync(
                "PersonAccountLinked",
                linkedBy.ToString(),
                It.IsAny<string>(),
                null,
                null),
            Times.Once);
    }

    [Fact]
    public async Task UnlinkAccountFromPersonAsync_ShouldLogAuditEvent()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = CreateService(context);

        var person = new Person { FirstName = "John", LastName = "Doe" };
        var createdPerson = await service.CreatePersonAsync(person);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "johndoe",
            Email = "john@example.com",
            PersonId = createdPerson.Id
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        _auditServiceMock.Reset(); // Clear previous audit calls
        var unlinkedBy = Guid.NewGuid();

        // Act
        await service.UnlinkAccountFromPersonAsync(user.Id, unlinkedBy);

        // Assert
        _auditServiceMock.Verify(
            a => a.LogEventAsync(
                "PersonAccountUnlinked",
                unlinkedBy.ToString(),
                It.IsAny<string>(),
                null,
                null),
            Times.Once);
    }

    #region Phase 10.6: Identity Document Validation Tests

    [Fact]
    public async Task CreatePersonAsync_WithValidNationalId_ShouldCreatePerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789"
        };

        // Act
        var result = await service.CreatePersonAsync(person, Guid.NewGuid());

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(PidHasher.Hash("A123456789"), result.NationalId);
    }

    [Fact]
    public async Task CreatePersonAsync_WithInvalidNationalId_ShouldThrowException()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456780" // Invalid checksum
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePersonAsync(person, Guid.NewGuid()));
        Assert.Contains("Invalid Taiwan National ID format", exception.Message);
    }

    [Fact]
    public async Task CreatePersonAsync_WithDuplicateNationalId_ShouldThrowException()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person1 = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789"
        };
        await service.CreatePersonAsync(person1);

        var person2 = new Person
        {
            FirstName = "Jane",
            LastName = "Smith",
            NationalId = "A123456789" // Duplicate
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePersonAsync(person2));
        Assert.Contains("Person with this identity document already exists", exception.Message);
    }

    [Fact]
    public async Task CreatePersonAsync_WithInvalidPassportNumber_ShouldThrowException()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            PassportNumber = "12345" // Too short
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePersonAsync(person, Guid.NewGuid()));
        Assert.Contains("Invalid passport number format", exception.Message);
    }

    [Fact]
    public async Task CreatePersonAsync_WithInvalidResidentCertificate_ShouldThrowException()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            ResidentCertificateNumber = "ABC123" // Too short
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePersonAsync(person, Guid.NewGuid()));
        Assert.Contains("Invalid resident certificate format", exception.Message);
    }

    [Fact]
    public async Task UpdatePersonAsync_ChangingNationalId_ShouldResetVerification()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789"
        };
        var created = await service.CreatePersonAsync(person);

        // Verify identity
        await service.VerifyPersonIdentityAsync(created.Id, Guid.NewGuid());

        // Update with new national ID
        var updatedPerson = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "B123456780" // Different valid ID
        };

        // Act
        var result = await service.UpdatePersonAsync(created.Id, updatedPerson);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PidHasher.Hash("B123456780"), result.NationalId);
        Assert.Null(result.IdentityVerifiedAt);
        Assert.Null(result.IdentityVerifiedBy);
    }

    [Fact]
    public async Task CheckPersonUniquenessAsync_WithDuplicateNationalId_ShouldReturnFalse()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789"
        };
        await service.CreatePersonAsync(person);

        // Act
        var (success, errorMessage) = await service.CheckPersonUniquenessAsync(PidHasher.Hash("A123456789"), null, null);

        // Assert
        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Contains("already exists", errorMessage);
    }

    [Fact]
    public async Task CheckPersonUniquenessAsync_WithNoDuplicate_ShouldReturnTrue()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        // Act
        var (success, errorMessage) = await service.CheckPersonUniquenessAsync(PidHasher.Hash("A123456789"), null, null);

        // Assert
        Assert.True(success);
        Assert.Null(errorMessage);
    }

    [Fact]
    public async Task CheckPersonUniquenessAsync_ExcludingSamePerson_ShouldReturnTrue()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789"
        };
        var created = await service.CreatePersonAsync(person);

        // Act - check same national ID but exclude the person who owns it
        var (success, errorMessage) = await service.CheckPersonUniquenessAsync(
            PidHasher.Hash("A123456789"), null, null, created.Id);

        // Assert
        Assert.True(success);
        Assert.Null(errorMessage);
    }

    [Fact]
    public async Task VerifyPersonIdentityAsync_WithValidPerson_ShouldSetVerificationFields()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789"
        };
        var created = await service.CreatePersonAsync(person);
        var verifierId = Guid.NewGuid();

        // Act
        var result = await service.VerifyPersonIdentityAsync(created.Id, verifierId);

        // Assert
        Assert.True(result);

        var verifiedPerson = await service.GetPersonByIdAsync(created.Id);
        Assert.NotNull(verifiedPerson);
        Assert.NotNull(verifiedPerson.IdentityVerifiedAt);
        Assert.Equal(verifierId, verifiedPerson.IdentityVerifiedBy);
    }

    [Fact]
    public async Task VerifyPersonIdentityAsync_WithNonExistentPerson_ShouldReturnFalse()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        // Act
        var result = await service.VerifyPersonIdentityAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyPersonIdentityAsync_WithNoIdentityDocument_ShouldReturnFalse()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe"
            // No identity document
        };
        var created = await service.CreatePersonAsync(person);

        // Act
        var result = await service.VerifyPersonIdentityAsync(created.Id, Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyPersonIdentityAsync_ShouldLogAuditEvent()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789"
        };
        var created = await service.CreatePersonAsync(person);

        _auditServiceMock.Reset(); // Clear previous audit calls
        var verifierId = Guid.NewGuid();

        // Act
        await service.VerifyPersonIdentityAsync(created.Id, verifierId);

        // Assert
        _auditServiceMock.Verify(
            a => a.LogEventAsync(
                "PersonIdentityVerified",
                verifierId.ToString(),
                It.IsAny<string>(),
                null,
                null),
            Times.Once);
    }

    #endregion
}
