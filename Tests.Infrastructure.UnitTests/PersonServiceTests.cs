using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
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

    public PersonServiceTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _loggerMock = new Mock<ILogger<PersonService>>();
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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        var service = new PersonService(context, _loggerMock.Object);

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
        
        var service = new PersonService(context, _loggerMock.Object);

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
    public async Task UnlinkAccountFromPersonAsync_ShouldUnlinkAccount()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        
        var service = new PersonService(context, _loggerMock.Object);

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
        
        var service = new PersonService(context, _loggerMock.Object);

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
}
