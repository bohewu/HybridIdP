using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading;
using Xunit;
using Core.Domain.Constants;

namespace Tests.Infrastructure.UnitTests;

/// <summary>
/// Unit tests for MyUserClaimsPrincipalFactory
/// </summary>
public class MyUserClaimsPrincipalFactoryTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<RoleManager<ApplicationRole>> _roleManagerMock;
    private readonly Mock<IOptions<IdentityOptions>> _optionsAccessorMock;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<ILogger<MyUserClaimsPrincipalFactory>> _loggerMock;

    public MyUserClaimsPrincipalFactoryTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Mock UserManager
        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object,
            null, null, null, null, null, null, null, null);

        // Mock RoleManager
        var roleStoreMock = new Mock<IRoleStore<ApplicationRole>>();
        _roleManagerMock = new Mock<RoleManager<ApplicationRole>>(
            roleStoreMock.Object,
            null, null, null, null);

        // Mock IOptions<IdentityOptions>
        _optionsAccessorMock = new Mock<IOptions<IdentityOptions>>();
        _optionsAccessorMock.Setup(o => o.Value).Returns(new IdentityOptions());

        _auditServiceMock = new Mock<IAuditService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<MyUserClaimsPrincipalFactory>>();
    }

    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;

    public void Dispose()
    {
        using var context = new ApplicationDbContext(_options);
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task CreateAsync_WhenActiveUserHasNoPerson_CreatesPersonAndLogsAudit()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var factory = new MyUserClaimsPrincipalFactory(
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _optionsAccessorMock.Object,
            context,
            _auditServiceMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "testuser",
            FirstName = "Test",
            LastName = "User",
            Department = "IT",
            PersonId = null // Orphan user
        };

        // Setup UserManager methods
        _userManagerMock.Setup(um => um.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        SetupPrincipalGeneration(user);

        // Act
        var principal = await factory.CreateAsync(user);

        // Assert
        Assert.NotNull(principal.Identity);
        Assert.NotNull(user.PersonId);
        Assert.NotNull(user.Person);
        Assert.Equal(user.PersonId, user.Person.Id);

        // Verify Person was added to DB
        var personInDb = await context.Persons.FindAsync(user.PersonId.Value);
        Assert.NotNull(personInDb);
        Assert.Equal("Test", personInDb.FirstName);
        Assert.Equal("User", personInDb.LastName);
        Assert.Equal("IT", personInDb.Department);

        // Verify audit was logged
        _auditServiceMock.Verify(a => a.LogEventAsync(
            "OrphanUserAutoHealed",
            user.Id.ToString(),
            It.Is<string>(s => s.Contains("PersonId") && s.Contains("ApplicationUserId") && s.Contains("HealedAt")),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenActiveUserHasPerson_DoesNotCreatePerson()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var factory = new MyUserClaimsPrincipalFactory(
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _optionsAccessorMock.Object,
            context,
            _auditServiceMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);

        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Existing",
            LastName = "Person",
            Department = "HR",
            CreatedAt = DateTime.UtcNow
        };
        context.Persons.Add(person);
        await context.SaveChangesAsync();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            UserName = "existinguser",
            FirstName = "Existing",
            LastName = "User",
            Department = "HR",
            PersonId = person.Id
        };

        var initialPersonCount = await context.Persons.CountAsync();

        // Setup UserManager methods
        SetupPrincipalGeneration(user);

        // Act
        var principal = await factory.CreateAsync(user);

        // Assert
        Assert.NotNull(principal.Identity);
        Assert.Equal(person.Id, user.PersonId);
        Assert.NotNull(user.Person);
        Assert.Equal(person.Id, user.Person.Id);

        // Verify no new Person was created
        var finalPersonCount = await context.Persons.CountAsync();
        Assert.Equal(initialPersonCount, finalPersonCount);

        // Verify no audit was logged
        _auditServiceMock.Verify(a => a.LogEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task CreateAsync_WhenUserIsTerminal_ThrowsBeforeCreatingPrincipalOrPerson(
        bool isActive,
        bool isDeleted)
    {
        using var context = new ApplicationDbContext(_options);
        var factory = new MyUserClaimsPrincipalFactory(
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _optionsAccessorMock.Object,
            context,
            _auditServiceMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "terminal-user",
            Email = "terminal@example.com",
            IsActive = isActive,
            IsDeleted = isDeleted
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(user));

        Assert.Equal("User account is unavailable.", exception.Message);
        Assert.Null(user.PersonId);
        Assert.Empty(await context.Persons.ToListAsync());
        _userManagerMock.Verify(manager => manager.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _userManagerMock.Verify(manager => manager.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _auditServiceMock.Verify(audit => audit.LogEventAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupPrincipalGeneration(ApplicationUser user)
    {
        _userManagerMock.Setup(manager => manager.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(manager => manager.GetRolesAsync(user)).ReturnsAsync(new List<string>());
        _userManagerMock.Setup(manager => manager.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _userManagerMock.Setup(manager => manager.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
    }

    [Theory]
    [InlineData("Account", "Person", "Managed Name", "Account")]
    [InlineData(null, "Person", "Managed Name", "Person")]
    [InlineData(null, null, "Managed Name", "Managed Name")]
    [InlineData(null, null, null, "provider_subject")]
    [InlineData("Account", "Person", "example.organization", "Account")]
    [InlineData(null, "Person", "example.organization", "Person")]
    [InlineData(null, null, "example.organization", "provider_subject")]
    public async Task CreateAsync_ShouldUseManagedDisplayFallbackWithoutChangingIdentifiers(
        string? accountName, string? personName, string? managedName, string expected)
    {
        using var context = new ApplicationDbContext(_options);
        var person = new Person { Id = Guid.NewGuid(), FirstName = personName };
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, UserName = "provider_subject",
            Email = "contact@example.test", FirstName = accountName, PersonId = person.Id, Person = person };
        SetupPrincipalGeneration(user);
        var logins = new List<UserLoginInfo>
        {
            new("example.organization", "subject", managedName),
            new("Google", "social-subject", "Social Name"),
            new("Legacy", "subject", "Legacy Name")
        };
        var originalLogins = logins.Select(login => (login.LoginProvider, login.ProviderKey, login.ProviderDisplayName)).ToArray();
        _userManagerMock.Setup(manager => manager.GetLoginsAsync(user)).ReturnsAsync(logins);
        var factory = new MyUserClaimsPrincipalFactory(_userManagerMock.Object, _roleManagerMock.Object,
            _optionsAccessorMock.Object, context, _auditServiceMock.Object, _httpContextAccessorMock.Object,
            _loggerMock.Object, Options.Create(new global::Infrastructure.Options.ProviderProofOptions { TrustedProviderNamespace = "example.organization" }));
        var principal = await factory.CreateAsync(user);
        Assert.Equal(expected, principal.Identity!.Name);
        Assert.Equal(userId.ToString(), principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        Assert.Equal(user.Email, principal.FindFirst(AuthConstants.Claims.PreferredUsername)!.Value);
        Assert.Equal(userId, user.Id);
        Assert.Equal("provider_subject", user.UserName);
        Assert.Equal(originalLogins, logins.Select(login => (login.LoginProvider, login.ProviderKey, login.ProviderDisplayName)).ToArray());
        _userManagerMock.Verify(manager => manager.UpdateAsync(user), Times.Never);
        _userManagerMock.Verify(manager => manager.AddLoginAsync(user, It.IsAny<UserLoginInfo>()), Times.Never);
        _userManagerMock.Verify(manager => manager.RemoveLoginAsync(user, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

}
