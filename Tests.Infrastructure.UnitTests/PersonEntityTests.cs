using Core.Domain;
using Core.Domain.Entities;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// Unit tests for Person entity and its relationship with ApplicationUser
/// Phase 10.1: Schema & Backfill
/// </summary>
public class PersonEntityTests
{
    [Fact]
    public void Person_CanBeCreated_WithRequiredProperties()
    {
        // Arrange & Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "John",
            LastName = "Doe",
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.NotEqual(Guid.Empty, person.Id);
        Assert.Equal("John", person.FirstName);
        Assert.Equal("Doe", person.LastName);
        Assert.NotEqual(default(DateTime), person.CreatedAt);
    }

    [Fact]
    public void Person_CanHave_FullProfileInformation()
    {
        // Arrange & Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "John",
            MiddleName = "Michael",
            LastName = "Doe",
            Nickname = "Johnny",
            EmployeeId = "EMP001",
            Department = "Engineering",
            JobTitle = "Senior Developer",
            ProfileUrl = "https://example.com/profiles/johndoe",
            PictureUrl = "https://example.com/images/johndoe.jpg",
            Website = "https://johndoe.dev",
            Address = "{\"street\":\"123 Main St\",\"city\":\"New York\"}",
            Birthdate = "1990-01-15",
            Gender = "Male",
            TimeZone = "America/New_York",
            Locale = "en-US",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid()
        };

        // Assert
        Assert.Equal("John", person.FirstName);
        Assert.Equal("Michael", person.MiddleName);
        Assert.Equal("Doe", person.LastName);
        Assert.Equal("Johnny", person.Nickname);
        Assert.Equal("EMP001", person.EmployeeId);
        Assert.Equal("Engineering", person.Department);
        Assert.Equal("Senior Developer", person.JobTitle);
        Assert.NotNull(person.ProfileUrl);
        Assert.NotNull(person.PictureUrl);
        Assert.NotNull(person.Website);
        Assert.NotNull(person.Address);
        Assert.Equal("1990-01-15", person.Birthdate);
        Assert.Equal("Male", person.Gender);
        Assert.Equal("America/New_York", person.TimeZone);
        Assert.Equal("en-US", person.Locale);
        Assert.NotNull(person.CreatedBy);
    }

    [Fact]
    public void Person_CanHave_ModificationTracking()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Smith",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid()
        };

        // Act
        person.ModifiedAt = DateTime.UtcNow.AddMinutes(5);
        person.ModifiedBy = Guid.NewGuid();

        // Assert
        Assert.NotNull(person.ModifiedAt);
        Assert.NotNull(person.ModifiedBy);
        Assert.True(person.ModifiedAt > person.CreatedAt);
    }

    [Fact]
    public void ApplicationUser_CanLink_ToPerson()
    {
        // Arrange
        var personId = Guid.NewGuid();
        var person = new Person
        {
            Id = personId,
            FirstName = "Alice",
            LastName = "Johnson",
            CreatedAt = DateTime.UtcNow
        };

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "alice.johnson",
            Email = "alice@example.com",
            PersonId = personId,
            Person = person
        };

        // Assert
        Assert.Equal(personId, user.PersonId);
        Assert.NotNull(user.Person);
        Assert.Equal("Alice", user.Person.FirstName);
        Assert.Equal("Johnson", user.Person.LastName);
    }

    [Fact]
    public void ApplicationUser_CanHave_NullPersonId()
    {
        // Arrange & Act
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "newuser",
            Email = "newuser@example.com",
            PersonId = null
        };

        // Assert
        Assert.Null(user.PersonId);
        Assert.Null(user.Person);
    }

    [Fact]
    public void Person_CanHave_MultipleAccounts()
    {
        // Arrange
        var personId = Guid.NewGuid();
        var person = new Person
        {
            Id = personId,
            FirstName = "Bob",
            LastName = "Williams",
            EmployeeId = "EMP002",
            CreatedAt = DateTime.UtcNow,
            Accounts = new List<ApplicationUser>()
        };

        var contractAccount = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "bob.williams.contract",
            Email = "bob.contract@example.com",
            PersonId = personId
        };

        var permanentAccount = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "bob.williams",
            Email = "bob@example.com",
            PersonId = personId
        };

        // Act
        person.Accounts.Add(contractAccount);
        person.Accounts.Add(permanentAccount);

        // Assert
        Assert.NotNull(person.Accounts);
        Assert.Equal(2, person.Accounts.Count);
        Assert.Contains(contractAccount, person.Accounts);
        Assert.Contains(permanentAccount, person.Accounts);
        Assert.All(person.Accounts, account => Assert.Equal(personId, account.PersonId));
    }

    [Fact]
    public void Person_EmployeeId_IsOptional()
    {
        // Arrange & Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Charlie",
            LastName = "Brown",
            EmployeeId = null,  // Not an employee, maybe a contractor
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Null(person.EmployeeId);
        Assert.NotNull(person.FirstName);
        Assert.NotNull(person.LastName);
    }

    [Fact]
    public void Person_CreatedAt_IsRequired()
    {
        // Arrange & Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "David",
            LastName = "Miller",
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.NotEqual(default(DateTime), person.CreatedAt);
    }

    [Fact]
    public void Person_CanStore_AddressAsJson()
    {
        // Arrange
        var addressJson = "{\"street\":\"456 Oak Ave\",\"city\":\"Boston\",\"state\":\"MA\",\"zip\":\"02101\"}";

        // Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Emily",
            LastName = "Davis",
            Address = addressJson,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.NotNull(person.Address);
        Assert.Contains("Boston", person.Address);
        Assert.Contains("456 Oak Ave", person.Address);
    }
}
