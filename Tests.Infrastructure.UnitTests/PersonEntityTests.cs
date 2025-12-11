using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// Unit tests for Person entity and its relationship with ApplicationUser
/// Phase 10.1: Schema & Backfill
/// Phase 18: Added lifecycle management tests
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

    #region Phase 18: Lifecycle Management Tests

    [Fact]
    public void Person_DefaultStatus_IsActive()
    {
        // Arrange & Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User"
        };

        // Assert
        Assert.Equal(PersonStatus.Active, person.Status);
    }

    [Theory]
    [InlineData(PersonStatus.Pending)]
    [InlineData(PersonStatus.Active)]
    [InlineData(PersonStatus.Suspended)]
    [InlineData(PersonStatus.Resigned)]
    [InlineData(PersonStatus.Terminated)]
    public void Person_CanHave_AllStatusValues(PersonStatus status)
    {
        // Arrange & Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            Status = status
        };

        // Assert
        Assert.Equal(status, person.Status);
    }

    [Fact]
    public void Person_CanHave_StartAndEndDates()
    {
        // Arrange
        var startDate = DateTime.UtcNow.AddDays(-30);
        var endDate = DateTime.UtcNow.AddMonths(6);

        // Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Contract",
            LastName = "Worker",
            StartDate = startDate,
            EndDate = endDate
        };

        // Assert
        Assert.NotNull(person.StartDate);
        Assert.NotNull(person.EndDate);
        Assert.Equal(startDate, person.StartDate);
        Assert.Equal(endDate, person.EndDate);
    }

    [Fact]
    public void Person_CanBe_SoftDeleted()
    {
        // Arrange
        var deletedBy = Guid.NewGuid();
        var deletedAt = DateTime.UtcNow;

        // Act
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Deleted",
            LastName = "User",
            IsDeleted = true,
            DeletedAt = deletedAt,
            DeletedBy = deletedBy
        };

        // Assert
        Assert.True(person.IsDeleted);
        Assert.Equal(deletedAt, person.DeletedAt);
        Assert.Equal(deletedBy, person.DeletedBy);
    }

    [Fact]
    public void CanAuthenticate_ActivePerson_ReturnsTrue()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Active",
            LastName = "User",
            Status = PersonStatus.Active,
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanAuthenticate_DeletedPerson_ReturnsFalse()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Deleted",
            LastName = "User",
            Status = PersonStatus.Active,
            IsDeleted = true
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(PersonStatus.Pending)]
    [InlineData(PersonStatus.Suspended)]
    [InlineData(PersonStatus.Resigned)]
    [InlineData(PersonStatus.Terminated)]
    public void CanAuthenticate_NonActiveStatus_ReturnsFalse(PersonStatus status)
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Inactive",
            LastName = "User",
            Status = status,
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanAuthenticate_FutureStartDate_ReturnsFalse()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Future",
            LastName = "User",
            Status = PersonStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(7),
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanAuthenticate_PastEndDate_ReturnsFalse()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Expired",
            LastName = "User",
            Status = PersonStatus.Active,
            EndDate = DateTime.UtcNow.AddDays(-1),
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanAuthenticate_WithinDateRange_ReturnsTrue()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Valid",
            LastName = "User",
            Status = PersonStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(-30),
            EndDate = DateTime.UtcNow.AddDays(30),
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanAuthenticate_StartDateToday_ReturnsTrue()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Today",
            LastName = "User",
            Status = PersonStatus.Active,
            StartDate = DateTime.UtcNow.Date,
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanAuthenticate_EndDateToday_ReturnsTrue()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "LastDay",
            LastName = "User",
            Status = PersonStatus.Active,
            EndDate = DateTime.UtcNow.Date,
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanAuthenticate_NullDates_ReturnsTrue()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "NoDates",
            LastName = "User",
            Status = PersonStatus.Active,
            StartDate = null,
            EndDate = null,
            IsDeleted = false
        };

        // Act
        var result = person.CanAuthenticate();

        // Assert
        Assert.True(result);
    }

    #endregion
}

