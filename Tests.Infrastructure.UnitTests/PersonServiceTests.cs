using Core.Application;
using Core.Application.Options;
using Core.Application.Utilities;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Data.Common;
using System.Threading;
using System.Security.Claims;
using System.Collections.Immutable;
using System.Text.Json;
using Core.Domain.Constants;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
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
    private readonly IOptions<AuditOptions> _auditOptions;
    private readonly Mock<IAdministrativeAuthorizationBoundary> _boundary = new();
    private readonly Mock<Microsoft.AspNetCore.Authorization.IAuthorizationService> _authorization = new();
    private readonly Mock<IOpenIddictApplicationManager> _applications = new();
    private readonly Mock<IOpenIddictScopeManager> _scopes = new();
    private readonly PrivilegedRoleProtectionOptions _roleProtection = new();

    public PersonServiceTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _loggerMock = new Mock<ILogger<PersonService>>();
        _auditServiceMock = new Mock<IAuditService>();
        _auditOptions = Options.Create(new AuditOptions { PiiMaskingLevel = PiiMaskingLevel.Partial });
        
        // Create UserManager mock
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _userManagerMock.Setup(manager => manager.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());
        _userManagerMock.Setup(manager => manager.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);
        SetActor(Guid.NewGuid(), true, Permissions.GetAll().ToArray());
    }
    
    private PersonService CreateService(ApplicationDbContext context)
    {
        var authorization = new PersonOperationAuthorization(context, _boundary.Object, _authorization.Object,
            _applications.Object, _scopes.Object, Options.Create(_roleProtection));
        return new PersonService(context, _loggerMock.Object, _auditServiceMock.Object, _userManagerMock.Object, _auditOptions, authorization);
    }

    private void SetActor(Guid actorId, bool admin, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme));
        _boundary.Setup(boundary => boundary.ResolveAsync())
            .ReturnsAsync(new AdministrativeAuthority(principal, false, new HashSet<string>()));
        _authorization.Setup(service => service.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), null, It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string permission) =>
                permissions.Contains(permission) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
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
    public async Task UpdatePersonAsync_ShouldRotateLinkedUserSecurityStamp_WhenAuthenticationEligibilityChanges()
    {
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Active",
            LastName = "Person",
            Status = Core.Domain.Enums.PersonStatus.Active
        };
        var linkedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "person-linked@example.test",
            NormalizedUserName = "PERSON-LINKED@EXAMPLE.TEST",
            Email = "person-linked@example.test",
            NormalizedEmail = "PERSON-LINKED@EXAMPLE.TEST",
            PersonId = person.Id,
            SecurityStamp = "original-stamp"
        };
        context.AddRange(person, linkedUser);
        await context.SaveChangesAsync();

        var result = await service.UpdatePersonAsync(person.Id, new Person
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            Status = Core.Domain.Enums.PersonStatus.Suspended
        });

        Assert.NotNull(result);
        Assert.Equal(Core.Domain.Enums.PersonStatus.Suspended, result.Status);
        Assert.NotEqual("original-stamp", linkedUser.SecurityStamp);
    }

    [Fact]
    public async Task UpdatePersonAsync_ShouldKeepLinkedUserSecurityStamp_WhenAuthenticationEligibilityIsUnchanged()
    {
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Active",
            LastName = "Person",
            Status = Core.Domain.Enums.PersonStatus.Active
        };
        var linkedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "eligible-linked@example.test",
            NormalizedUserName = "ELIGIBLE-LINKED@EXAMPLE.TEST",
            Email = "eligible-linked@example.test",
            NormalizedEmail = "ELIGIBLE-LINKED@EXAMPLE.TEST",
            PersonId = person.Id,
            SecurityStamp = "eligible-stamp"
        };
        context.AddRange(person, linkedUser);
        await context.SaveChangesAsync();

        var result = await service.UpdatePersonAsync(person.Id, new Person
        {
            FirstName = "Still",
            LastName = person.LastName,
            Status = Core.Domain.Enums.PersonStatus.Active
        });

        Assert.NotNull(result);
        Assert.Equal("eligible-stamp", linkedUser.SecurityStamp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdatePersonAsync_ShouldRejectActiveStatus_WhenScheduledTokenRevocationIsPending(
        bool startsInFuture)
    {
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);
        var pendingAt = DateTime.UtcNow.AddMinutes(-1);
        var endDate = DateTime.UtcNow.AddDays(-1);
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Pending",
            LastName = "Revocation",
            Status = Core.Domain.Enums.PersonStatus.Resigned,
            EndDate = endDate,
            ScheduledTokenRevocationPendingAt = pendingAt
        };
        var linkedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "pending-revocation@example.test",
            NormalizedUserName = "PENDING-REVOCATION@EXAMPLE.TEST",
            Email = "pending-revocation@example.test",
            NormalizedEmail = "PENDING-REVOCATION@EXAMPLE.TEST",
            PersonId = person.Id,
            SecurityStamp = "pending-stamp"
        };
        context.AddRange(person, linkedUser);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdatePersonAsync(person.Id, new Person
            {
                FirstName = person.FirstName,
                LastName = person.LastName,
                Status = Core.Domain.Enums.PersonStatus.Active,
                StartDate = DateTime.UtcNow.AddDays(startsInFuture ? 1 : -1),
                EndDate = null
            }));

        Assert.Contains("token revocation is pending", exception.Message);
        context.ChangeTracker.Clear();
        var persistedPerson = await context.Persons.SingleAsync(candidate => candidate.Id == person.Id);
        var persistedUser = await context.Users.SingleAsync(candidate => candidate.Id == linkedUser.Id);
        Assert.Equal(Core.Domain.Enums.PersonStatus.Resigned, persistedPerson.Status);
        Assert.Equal(endDate.Date, persistedPerson.EndDate!.Value.Date);
        Assert.Equal(pendingAt, persistedPerson.ScheduledTokenRevocationPendingAt);
        Assert.Equal("pending-stamp", persistedUser.SecurityStamp);
        Assert.False(persistedPerson.CanAuthenticate());
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
    public async Task DeletePersonAsync_ShouldTerminallyRetainLinkedUsersAndRevokeActiveSessions_WhenUsingSqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateSqliteOptions(connection);
        var fixture = await SeedHardDeleteFixtureAsync(options);

        await using (var context = new ApplicationDbContext(options))
        {
            var service = CreateService(context);

            var result = await service.DeletePersonAsync(fixture.PersonId);

            Assert.True(result);
        }

        await using var verificationContext = new ApplicationDbContext(options);
        Assert.Null(await verificationContext.Persons.FindAsync(fixture.PersonId));

        var linkedUsers = await verificationContext.Users
            .Where(user => user.Id == fixture.FirstLinkedUserId || user.Id == fixture.SecondLinkedUserId)
            .ToDictionaryAsync(user => user.Id);
        Assert.Equal(2, linkedUsers.Count);
        Assert.All(linkedUsers.Values, user =>
        {
            Assert.False(user.IsActive);
            Assert.True(user.IsDeleted);
            Assert.Null(user.PersonId);
            Assert.NotNull(user.DeletedAt);
            Assert.NotNull(user.ModifiedAt);
        });
        Assert.NotEqual(fixture.FirstLinkedUserSecurityStamp, linkedUsers[fixture.FirstLinkedUserId].SecurityStamp);
        Assert.NotEqual(fixture.SecondLinkedUserSecurityStamp, linkedUsers[fixture.SecondLinkedUserId].SecurityStamp);
        Assert.Equal(linkedUsers[fixture.FirstLinkedUserId].DeletedAt, linkedUsers[fixture.SecondLinkedUserId].DeletedAt);
        Assert.Equal(linkedUsers[fixture.FirstLinkedUserId].ModifiedAt, linkedUsers[fixture.SecondLinkedUserId].ModifiedAt);

        var activeSessions = await verificationContext.UserSessions
            .Where(session => session.Id == fixture.FirstActiveSessionId || session.Id == fixture.SecondActiveSessionId)
            .ToDictionaryAsync(session => session.Id);
        Assert.All(activeSessions.Values, session =>
        {
            Assert.NotNull(session.RevokedUtc);
            Assert.Equal("person-hard-delete", session.RevocationReason);
        });

        var alreadyRevokedSession = await verificationContext.UserSessions.FindAsync(fixture.AlreadyRevokedSessionId);
        Assert.NotNull(alreadyRevokedSession);
        Assert.Equal(fixture.AlreadyRevokedUtc, alreadyRevokedSession.RevokedUtc);
        Assert.Equal("existing-revocation", alreadyRevokedSession.RevocationReason);

        var unrelatedUser = await verificationContext.Users.FindAsync(fixture.UnrelatedUserId);
        Assert.NotNull(unrelatedUser);
        Assert.True(unrelatedUser.IsActive);
        Assert.False(unrelatedUser.IsDeleted);
        Assert.Equal(fixture.UnrelatedPersonId, unrelatedUser.PersonId);
        Assert.Equal(fixture.UnrelatedUserSecurityStamp, unrelatedUser.SecurityStamp);

        var unrelatedSession = await verificationContext.UserSessions.FindAsync(fixture.UnrelatedSessionId);
        Assert.NotNull(unrelatedSession);
        Assert.Null(unrelatedSession.RevokedUtc);
        Assert.Null(unrelatedSession.RevocationReason);
    }

    [Fact]
    public async Task DeletePersonAsync_ShouldRollbackAllLinkedState_WhenPersonDeleteFails()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateSqliteOptions(connection);
        var fixture = await SeedHardDeleteFixtureAsync(options);
        var failureOptions = CreateSqliteOptions(connection, new ThrowOnPersonDeleteInterceptor());

        await using (var context = new ApplicationDbContext(failureOptions))
        {
            var service = CreateService(context);

            await Assert.ThrowsAsync<DbUpdateException>(() => service.DeletePersonAsync(fixture.PersonId));
        }

        await using var verificationContext = new ApplicationDbContext(options);
        var person = await verificationContext.Persons.FindAsync(fixture.PersonId);
        Assert.NotNull(person);

        var linkedUsers = await verificationContext.Users
            .Where(user => user.Id == fixture.FirstLinkedUserId || user.Id == fixture.SecondLinkedUserId)
            .ToDictionaryAsync(user => user.Id);
        Assert.Equal(2, linkedUsers.Count);
        Assert.All(linkedUsers.Values, user =>
        {
            Assert.True(user.IsActive);
            Assert.False(user.IsDeleted);
            Assert.Null(user.DeletedAt);
            Assert.Null(user.ModifiedAt);
            Assert.Equal(fixture.PersonId, user.PersonId);
        });
        Assert.Equal(fixture.FirstLinkedUserSecurityStamp, linkedUsers[fixture.FirstLinkedUserId].SecurityStamp);
        Assert.Equal(fixture.SecondLinkedUserSecurityStamp, linkedUsers[fixture.SecondLinkedUserId].SecurityStamp);

        var activeSessions = await verificationContext.UserSessions
            .Where(session => session.Id == fixture.FirstActiveSessionId || session.Id == fixture.SecondActiveSessionId)
            .ToListAsync();
        Assert.All(activeSessions, session =>
        {
            Assert.Null(session.RevokedUtc);
            Assert.Null(session.RevocationReason);
        });

        _auditServiceMock.Verify(
            auditService => auditService.LogEventAsync(
                "PersonDeleted",
                It.IsAny<string?>(),
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Never);
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
                null,
                It.IsAny<CancellationToken>()),
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
                null,
                It.IsAny<CancellationToken>()),
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
                null,
                It.IsAny<CancellationToken>()),
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
                null,
                It.IsAny<CancellationToken>()),
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
                null,
                It.IsAny<CancellationToken>()),
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
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region IdentityDocumentType Auto-Inference Tests

    [Fact]
    public async Task CreatePersonAsync_WithNationalId_ShouldInferNationalIdType()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789" // Valid Taiwan National ID
        };

        // Act
        var result = await service.CreatePersonAsync(person, Guid.NewGuid());

        // Assert
        Assert.Equal("NationalId", result.IdentityDocumentType);
    }

    [Fact]
    public async Task CreatePersonAsync_WithPassportOnly_ShouldInferPassportType()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            PassportNumber = "AB1234567" // Valid passport format
        };

        // Act
        var result = await service.CreatePersonAsync(person, Guid.NewGuid());

        // Assert
        Assert.Equal("Passport", result.IdentityDocumentType);
    }

    [Fact]
    public async Task CreatePersonAsync_WithResidentCertOnly_ShouldInferResidentCertType()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            ResidentCertificateNumber = "AB12345678" // Valid resident cert format
        };

        // Act
        var result = await service.CreatePersonAsync(person, Guid.NewGuid());

        // Assert
        Assert.Equal("ResidentCertificate", result.IdentityDocumentType);
    }

    [Fact]
    public async Task CreatePersonAsync_WithNoIdentityDocs_ShouldInferNoneType()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var result = await service.CreatePersonAsync(person, Guid.NewGuid());

        // Assert
        Assert.Equal("None", result.IdentityDocumentType);
    }

    [Fact]
    public async Task CreatePersonAsync_WithMultipleIdentityDocs_ShouldPrioritizeNationalId()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var service = CreateService(context);

        var person = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            NationalId = "A123456789",
            PassportNumber = "AB1234567"
        };

        // Act
        var result = await service.CreatePersonAsync(person, Guid.NewGuid());

        // Assert - NationalId has priority over PassportNumber
        Assert.Equal("NationalId", result.IdentityDocumentType);
    }

    [Fact]
    public async Task UpdatePersonAsync_ShouldReInferIdentityDocumentType()
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
        Assert.Equal("NationalId", created.IdentityDocumentType);

        // Act - Update with passport only (remove national ID by not providing it)
        var updates = new Person
        {
            FirstName = "John",
            LastName = "Doe",
            PassportNumber = "AB1234567"
        };
        var result = await service.UpdatePersonAsync(created.Id, updates, Guid.NewGuid());

        // Assert - Should now infer based on current fields (NationalId hash is kept)
        Assert.Equal("NationalId", result!.IdentityDocumentType);
    }

    #endregion

    [Fact]
    public async Task TransferAssetsAsync_ShouldTransferAllResources()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        var service = CreateService(context);

        var fromPerson = new Person { FirstName = "From", LastName = "User" };
        var toPerson = new Person { FirstName = "To", LastName = "User" };
        context.Persons.AddRange(fromPerson, toPerson);
        await context.SaveChangesAsync();

        // Setup owned resources
        var apiResource = new ApiResource { Name = "api1", DisplayName = "API 1", OwnerPersonId = fromPerson.Id };
        context.ApiResources.Add(apiResource);

        var scopeOwnership = new ScopeOwnership { 
            ScopeId = "scope1", 
            CreatedByPersonId = fromPerson.Id,
            CreatedAt = DateTime.UtcNow 
        };
        context.ScopeOwnerships.Add(scopeOwnership);

        var clientOwnership = new ClientOwnership { 
            ClientId = "client1", 
            CreatedByPersonId = fromPerson.Id,
            CreatedAt = DateTime.UtcNow 
        };
        context.ClientOwnerships.Add(clientOwnership);

        await context.SaveChangesAsync();

        // Act
        await service.TransferAssetsAsync(fromPerson.Id, toPerson.Id);

        // Assert
        // Verify ApiResource transferred
        var updatedApi = await context.ApiResources.FirstAsync(r => r.Name == "api1");
        Assert.Equal(toPerson.Id, updatedApi.OwnerPersonId);

        // Verify ScopeOwnership transferred
        var updatedScope = await context.ScopeOwnerships.FirstAsync(s => s.ScopeId == "scope1");
        Assert.Equal(toPerson.Id, updatedScope.CreatedByPersonId);

        // Verify ClientOwnership transferred
        var updatedClient = await context.ClientOwnerships.FirstAsync(c => c.ClientId == "client1");
        Assert.Equal(toPerson.Id, updatedClient.CreatedByPersonId);

        // Verify Audit Log
        _auditServiceMock.Verify(s => s.LogEventAsync("ResourceOwnershipTransferred", It.IsAny<string>(), It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_ShouldDenyPersonnelOnlyActor_EvenBeforePersonOwnsAssets()
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Future", LastName = "Owner" };
        var account = new ApplicationUser { UserName = "controlled" };
        context.AddRange(person, account);
        await context.SaveChangesAsync();
        SetActor(account.Id, false, Permissions.Persons.Update);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(context).LinkAccountToPersonAsync(person.Id, account.Id, Guid.NewGuid()));

        // A later unrelated save must not persist a denied association.
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Null((await context.Users.FindAsync(account.Id))!.PersonId);
        _userManagerMock.Verify(manager => manager.AddToRolesAsync(It.IsAny<ApplicationUser>(),
            It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_ShouldAllowEmptyPerson_WithAccountAndPersonnelAuthority()
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Empty", LastName = "Person" };
        var account = new ApplicationUser { UserName = "account" };
        context.AddRange(person, account);
        await context.SaveChangesAsync();
        SetActor(Guid.NewGuid(), false, Permissions.Persons.Update, Permissions.Users.Update);
        var service = CreateService(context);

        Assert.True(await service.LinkAccountToPersonAsync(person.Id, account.Id));
        Assert.True(await service.UnlinkAccountFromPersonAsync(account.Id));
        Assert.Null(account.PersonId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersonAssociation_ShouldDenyRoleRetentionRoute_WithoutRoleAuthority(bool unlink)
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Protected", LastName = "Person" };
        var source = new ApplicationUser { UserName = "source", PersonId = person.Id };
        var target = new ApplicationUser { UserName = "target", PersonId = unlink ? person.Id : null };
        context.AddRange(person, source, target);
        await context.SaveChangesAsync();
        _userManagerMock.Setup(manager => manager.GetRolesAsync(source)).ReturnsAsync(new List<string> { "Admin" });
        _userManagerMock.Setup(manager => manager.GetRolesAsync(target))
            .ReturnsAsync(unlink ? new List<string> { "Admin" } : new List<string>());
        SetActor(target.Id, false, Permissions.Persons.Update, Permissions.Users.Update);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unlink
            ? service.UnlinkAccountFromPersonAsync(target.Id)
            : service.LinkAccountToPersonAsync(person.Id, target.Id));

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(unlink ? person.Id : (Guid?)null, (await context.Users.FindAsync(target.Id))!.PersonId);
        _userManagerMock.Verify(manager => manager.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _userManagerMock.Verify(manager => manager.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task LinkAccountToPersonAsync_ShouldPreflightProtectedTargetMfa(bool passkey, bool disabled, bool allowed)
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Protected", LastName = "Person" };
        var source = new ApplicationUser { UserName = "source", PersonId = person.Id };
        var target = new ApplicationUser { UserName = "target" };
        context.AddRange(person, source, target);
        if (passkey) context.UserCredentials.Add(new UserCredential
        {
            UserId = target.Id, CredentialId = [1], PublicKey = [2],
            DisabledAtUtc = disabled ? DateTime.UtcNow : null
        });
        await context.SaveChangesAsync();
        _roleProtection.RequireTargetMfaForPrivilegedRoleAssignment = true;
        _userManagerMock.Setup(manager => manager.GetRolesAsync(source)).ReturnsAsync(new List<string> { "Admin" });
        var service = CreateService(context);

        if (allowed)
        {
            Assert.True(await service.LinkAccountToPersonAsync(person.Id, target.Id));
            _userManagerMock.Verify(manager => manager.AddToRolesAsync(target,
                It.Is<IEnumerable<string>>(roles => roles.Contains("Admin"))), Times.Once);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LinkAccountToPersonAsync(person.Id, target.Id));
            await context.SaveChangesAsync();
            Assert.Null(target.PersonId);
            _userManagerMock.Verify(manager => manager.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersonAssociation_ShouldDenyForeignOwnership_WithAllDomainPermissions(bool unlink)
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Foreign", LastName = "Owner" };
        var account = new ApplicationUser { UserName = "account", PersonId = unlink ? person.Id : null };
        context.AddRange(person, account, new ApiResource { Name = "foreign", OwnerPersonId = person.Id });
        await context.SaveChangesAsync();
        SetActor(Guid.NewGuid(), false, Permissions.GetAll().ToArray());
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unlink
            ? service.UnlinkAccountFromPersonAsync(account.Id)
            : service.LinkAccountToPersonAsync(person.Id, account.Id));
        Assert.Equal(unlink ? person.Id : (Guid?)null, account.PersonId);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task LinkAccountToPersonAsync_ShouldHonorConfiguredOperatorMfa(bool requireMfa, bool completedMfa, bool allowed)
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Protected", LastName = "Person" };
        var source = new ApplicationUser { UserName = "source", PersonId = person.Id };
        var target = new ApplicationUser { UserName = "target" };
        context.AddRange(person, source, target);
        await context.SaveChangesAsync();
        _roleProtection.RequireOperatorMfaForPrivilegedRoleAssignment = requireMfa;
        _userManagerMock.Setup(manager => manager.GetRolesAsync(source)).ReturnsAsync(new List<string> { "Admin" });
        var authority = (await _boundary.Object.ResolveAsync())!;
        if (completedMfa) ((ClaimsIdentity)authority.Principal.Identity!).AddClaim(new Claim(AuthConstants.ClaimTypes.Amr, AuthConstants.Amr.Mfa));

        if (allowed) Assert.True(await CreateService(context).LinkAccountToPersonAsync(person.Id, target.Id));
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService(context).LinkAccountToPersonAsync(person.Id, target.Id));
            Assert.Null(target.PersonId);
        }
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_ShouldRejectUntrustedContext_RegardlessOfAuditUserId()
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Empty", LastName = "Person" };
        var target = new ApplicationUser { UserName = "target" };
        context.AddRange(person, target);
        await context.SaveChangesAsync();
        _boundary.Setup(boundary => boundary.ResolveAsync()).ReturnsAsync((AdministrativeAuthority?)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(context).LinkAccountToPersonAsync(person.Id, target.Id, Guid.NewGuid()));
        Assert.Null(target.PersonId);
    }

    [Fact]
    public async Task LinkAccountToPersonAsync_ShouldRespectBearerApprovalCeiling_DespiteAdminRoleClaim()
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Empty", LastName = "Person" };
        var target = new ApplicationUser { UserName = "target" };
        context.AddRange(person, target);
        await context.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, "Admin") }, AdministrativeAuthorizationBoundary.BearerScheme));
        _boundary.Setup(boundary => boundary.ResolveAsync()).ReturnsAsync(new AdministrativeAuthority(principal, true,
            new HashSet<string> { Permissions.Persons.Update }));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(context).LinkAccountToPersonAsync(person.Id, target.Id));
        Assert.Null(target.PersonId);
    }

    [Theory]
    [InlineData("clients.update")]
    [InlineData("scopes.update")]
    [InlineData("apiresources.update")]
    [InlineData("foreign-owner")]
    public async Task TransferAssetsAsync_ShouldLeaveEveryDomainUnchanged_WhenAnyAuthorityIsMissing(string missing)
    {
        using var context = new ApplicationDbContext(_options);
        var source = new Person { Id = Guid.NewGuid(), FirstName = "Source", LastName = "Owner" };
        var destination = new Person { Id = Guid.NewGuid(), FirstName = "Destination", LastName = "Owner" };
        var actor = new ApplicationUser { UserName = "actor", PersonId = missing == "foreign-owner" ? null : source.Id };
        var api = new ApiResource { Name = "api", OwnerPersonId = source.Id };
        var client = new ClientOwnership { ApplicationId = Guid.NewGuid(), ClientId = "client", CreatedByPersonId = source.Id };
        var scope = new ScopeOwnership { ScopeId = "scope-id", CreatedByPersonId = source.Id };
        context.AddRange(source, destination, actor, api, client, scope);
        await context.SaveChangesAsync();
        SetActor(actor.Id, false, Permissions.GetAll().Where(permission => permission != missing).ToArray());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(context).TransferAssetsAsync(source.Id, destination.Id));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(source.Id, (await context.ApiResources.FindAsync(api.Id))!.OwnerPersonId);
        Assert.Equal(source.Id, (await context.ClientOwnerships.FindAsync(client.Id))!.CreatedByPersonId);
        Assert.Equal(source.Id, (await context.ScopeOwnerships.FindAsync(scope.Id))!.CreatedByPersonId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransferAssetsAsync_ShouldRequireClientPermissionAndAdmin_ForUnboundClientOwnership(
        bool admin, bool clientPermission)
    {
        using var context = new ApplicationDbContext(_options);
        var source = new Person { Id = Guid.NewGuid(), FirstName = "Source", LastName = "Owner" };
        var destination = new Person { Id = Guid.NewGuid(), FirstName = "Destination", LastName = "Owner" };
        var actor = new ApplicationUser { UserName = "actor", PersonId = admin ? null : source.Id };
        var api = new ApiResource { Name = "api", OwnerPersonId = source.Id };
        var client = new ClientOwnership { ApplicationId = null, ClientId = "legacy", CreatedByPersonId = source.Id };
        var scope = new ScopeOwnership { ScopeId = "scope-id", CreatedByPersonId = source.Id };
        context.AddRange(source, destination, actor, api, client, scope);
        await context.SaveChangesAsync();
        SetActor(actor.Id, admin, Permissions.GetAll()
            .Where(permission => clientPermission || permission != Permissions.Clients.Update).ToArray());
        var service = CreateService(context);
        var allowed = admin && clientPermission;

        if (allowed) await service.TransferAssetsAsync(source.Id, destination.Id);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TransferAssetsAsync(source.Id, destination.Id));

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var expectedOwner = allowed ? destination.Id : source.Id;
        Assert.Equal(expectedOwner, (await context.ApiResources.FindAsync(api.Id))!.OwnerPersonId);
        Assert.Equal(expectedOwner, (await context.ClientOwnerships.FindAsync(client.Id))!.CreatedByPersonId);
        Assert.Null((await context.ClientOwnerships.FindAsync(client.Id))!.ApplicationId);
        Assert.Equal(expectedOwner, (await context.ScopeOwnerships.FindAsync(scope.Id))!.CreatedByPersonId);
        _applications.Verify(manager => manager.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersonAssociation_ShouldDenyUnboundClientOwnership_ForDelegatedOwner(bool unlink)
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Legacy", LastName = "Owner" };
        var actor = new ApplicationUser { UserName = "actor", PersonId = person.Id };
        var target = new ApplicationUser { UserName = "target", PersonId = unlink ? person.Id : null };
        var client = new ClientOwnership { ApplicationId = null, ClientId = "legacy", CreatedByPersonId = person.Id };
        context.AddRange(person, actor, target, client);
        await context.SaveChangesAsync();
        SetActor(actor.Id, false, Permissions.GetAll().ToArray());
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unlink
            ? service.UnlinkAccountFromPersonAsync(target.Id)
            : service.LinkAccountToPersonAsync(person.Id, target.Id));

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(unlink ? person.Id : (Guid?)null, (await context.Users.FindAsync(target.Id))!.PersonId);
        Assert.Equal(person.Id, (await context.ClientOwnerships.FindAsync(client.Id))!.CreatedByPersonId);
        Assert.Null((await context.ClientOwnerships.FindAsync(client.Id))!.ApplicationId);
        _userManagerMock.Verify(manager => manager.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _applications.Verify(manager => manager.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TransferAssetsAsync_ShouldAllowExactOwner_WithOnlyPresentDomainPermission()
    {
        using var context = new ApplicationDbContext(_options);
        var source = new Person { Id = Guid.NewGuid(), FirstName = "Source", LastName = "Owner" };
        var destination = new Person { Id = Guid.NewGuid(), FirstName = "Destination", LastName = "Owner" };
        var actor = new ApplicationUser { UserName = "actor", PersonId = source.Id };
        var api = new ApiResource { Name = "api", OwnerPersonId = source.Id };
        context.AddRange(source, destination, actor, api);
        await context.SaveChangesAsync();
        SetActor(actor.Id, false, Permissions.Persons.Update, Permissions.ApiResources.Update);

        await CreateService(context).TransferAssetsAsync(source.Id, destination.Id);
        Assert.Equal(destination.Id, api.OwnerPersonId);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task TransferAssetsAsync_ShouldPreserveAdministrativeClientAndStandardScopeRestrictions(
        bool approvedClient, bool standardScope, bool allowed)
    {
        using var context = new ApplicationDbContext(_options);
        var source = new Person { Id = Guid.NewGuid(), FirstName = "Source", LastName = "Owner" };
        var destination = new Person { Id = Guid.NewGuid(), FirstName = "Destination", LastName = "Owner" };
        var actor = new ApplicationUser { UserName = "actor", PersonId = source.Id };
        var client = new ClientOwnership { ApplicationId = Guid.NewGuid(), ClientId = "client", CreatedByPersonId = source.Id };
        var scope = new ScopeOwnership { ScopeId = "scope-id", CreatedByPersonId = source.Id };
        context.AddRange(source, destination, actor, client, scope);
        await context.SaveChangesAsync();
        SetActor(actor.Id, false, Permissions.Persons.Update, Permissions.Clients.Update, Permissions.Scopes.Update);
        var application = new object();
        _applications.Setup(manager => manager.FindByIdAsync(client.ApplicationId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _applications.Setup(manager => manager.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(approvedClient
                ? ImmutableDictionary<string, JsonElement>.Empty.Add(AdministrativeClientGrant.PermissionsProperty,
                    JsonSerializer.SerializeToElement(new[] { Permissions.Users.Read }))
                : ImmutableDictionary<string, JsonElement>.Empty);
        var oidcScope = new object();
        _scopes.Setup(manager => manager.FindByIdAsync(scope.ScopeId, It.IsAny<CancellationToken>())).ReturnsAsync(oidcScope);
        _scopes.Setup(manager => manager.GetNameAsync(oidcScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(standardScope ? "openid" : "custom");
        var service = CreateService(context);

        if (allowed) await service.TransferAssetsAsync(source.Id, destination.Id);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TransferAssetsAsync(source.Id, destination.Id));
        Assert.Equal(allowed ? destination.Id : source.Id, client.CreatedByPersonId);
        Assert.Equal(allowed ? destination.Id : source.Id, scope.CreatedByPersonId);
    }

    private static DbContextOptions<ApplicationDbContext> CreateSqliteOptions(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection);

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return optionsBuilder.Options;
    }

    private static async Task<HardDeleteFixture> SeedHardDeleteFixtureAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Target",
            LastName = "Person",
            CreatedAt = now
        };
        var unrelatedPerson = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Unrelated",
            LastName = "Person",
            CreatedAt = now
        };
        var firstLinkedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "target.one@example.test",
            NormalizedUserName = "TARGET.ONE@EXAMPLE.TEST",
            Email = "target.one@example.test",
            NormalizedEmail = "TARGET.ONE@EXAMPLE.TEST",
            PersonId = person.Id,
            IsActive = true,
            IsDeleted = false,
            SecurityStamp = "target-one-stamp",
            CreatedAt = now
        };
        var secondLinkedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "target.two@example.test",
            NormalizedUserName = "TARGET.TWO@EXAMPLE.TEST",
            Email = "target.two@example.test",
            NormalizedEmail = "TARGET.TWO@EXAMPLE.TEST",
            PersonId = person.Id,
            IsActive = true,
            IsDeleted = false,
            SecurityStamp = "target-two-stamp",
            CreatedAt = now
        };
        var unrelatedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "unrelated@example.test",
            NormalizedUserName = "UNRELATED@EXAMPLE.TEST",
            Email = "unrelated@example.test",
            NormalizedEmail = "UNRELATED@EXAMPLE.TEST",
            PersonId = unrelatedPerson.Id,
            IsActive = true,
            IsDeleted = false,
            SecurityStamp = "unrelated-stamp",
            CreatedAt = now
        };
        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = "TestRole",
            NormalizedName = "TESTROLE"
        };
        var alreadyRevokedUtc = now.AddHours(-1);
        var firstActiveSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = firstLinkedUser.Id,
            AuthorizationId = "target-one-active",
            ActiveRoleId = role.Id
        };
        var secondActiveSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = secondLinkedUser.Id,
            AuthorizationId = "target-two-active",
            ActiveRoleId = role.Id
        };
        var alreadyRevokedSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = firstLinkedUser.Id,
            AuthorizationId = "target-one-revoked",
            ActiveRoleId = role.Id,
            RevokedUtc = alreadyRevokedUtc,
            RevocationReason = "existing-revocation"
        };
        var unrelatedSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = unrelatedUser.Id,
            AuthorizationId = "unrelated-active",
            ActiveRoleId = role.Id
        };

        context.AddRange(
            person,
            unrelatedPerson,
            firstLinkedUser,
            secondLinkedUser,
            unrelatedUser,
            role,
            firstActiveSession,
            secondActiveSession,
            alreadyRevokedSession,
            unrelatedSession);
        await context.SaveChangesAsync();

        return new HardDeleteFixture(
            person.Id,
            unrelatedPerson.Id,
            firstLinkedUser.Id,
            secondLinkedUser.Id,
            unrelatedUser.Id,
            firstLinkedUser.SecurityStamp,
            secondLinkedUser.SecurityStamp,
            unrelatedUser.SecurityStamp,
            firstActiveSession.Id,
            secondActiveSession.Id,
            alreadyRevokedSession.Id,
            unrelatedSession.Id,
            alreadyRevokedUtc);
    }

    private sealed record HardDeleteFixture(
        Guid PersonId,
        Guid UnrelatedPersonId,
        Guid FirstLinkedUserId,
        Guid SecondLinkedUserId,
        Guid UnrelatedUserId,
        string? FirstLinkedUserSecurityStamp,
        string? SecondLinkedUserSecurityStamp,
        string? UnrelatedUserSecurityStamp,
        Guid FirstActiveSessionId,
        Guid SecondActiveSessionId,
        Guid AlreadyRevokedSessionId,
        Guid UnrelatedSessionId,
        DateTime AlreadyRevokedUtc);

    private sealed class ThrowOnPersonDeleteInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowWhenDeletingPerson(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowWhenDeletingPerson(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ThrowWhenDeletingPerson(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowWhenDeletingPerson(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void ThrowWhenDeletingPerson(DbCommand command)
        {
            if (command.CommandText.Contains("DELETE FROM \"Persons\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Injected failure after linked account updates.");
            }
        }
    }
}
