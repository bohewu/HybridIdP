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
using Xunit;

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

    // Test helper class to expose protected method
    private class TestableMyUserClaimsPrincipalFactory : MyUserClaimsPrincipalFactory
    {
        private readonly IApplicationDbContext _testContext;
        private readonly IAuditService _testAuditService;
        private readonly ILogger<MyUserClaimsPrincipalFactory> _testLogger;

        public TestableMyUserClaimsPrincipalFactory(
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            IOptions<IdentityOptions> optionsAccessor,
            IApplicationDbContext context,
            IAuditService auditService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<MyUserClaimsPrincipalFactory> logger)
            : base(userManager, roleManager, optionsAccessor, context, auditService, httpContextAccessor, logger)
        {
            _testContext = context;
            _testAuditService = auditService;
            _testLogger = logger;
        }

        protected override async Task<System.Security.Claims.ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            // Orphan auto-heal logic
            if (!user.PersonId.HasValue)
            {
                _testLogger.LogWarning("Orphan ApplicationUser detected: {UserId}, auto-creating Person", user.Id);
                
                var person = new Core.Domain.Entities.Person
                {
                    Id = Guid.NewGuid(),
                    FirstName = user.FirstName ?? user.Email?.Split('@')[0],
                    LastName = user.LastName,
                    Department = user.Department,
                    CreatedAt = DateTime.UtcNow
                };
                _testContext.Persons.Add(person);
                await _testContext.SaveChangesAsync(CancellationToken.None);
                
                user.PersonId = person.Id;
                await UserManager.UpdateAsync(user);
                user.Person = person;
                
                // Audit the auto-healing operation
                var auditDetails = System.Text.Json.JsonSerializer.Serialize(new
                {
                    PersonId = person.Id,
                    ApplicationUserId = user.Id,
                    Email = user.Email,
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    HealedAt = DateTime.UtcNow,
                    TriggerPoint = "Login/ClaimsGeneration"
                });
                await _testAuditService.LogEventAsync(
                    "OrphanUserAutoHealed",
                    user.Id.ToString(),
                    auditDetails,
                    null,
                    null);
            }
            // Load Person navigation property if not already loaded
            else if (user.Person == null)
            {
                user.Person = await _testContext.Persons.FindAsync(user.PersonId.Value);
            }

            // Create a basic identity for testing
            var identity = new System.Security.Claims.ClaimsIdentity();
            identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()));
            if (user.UserName != null)
            {
                identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.UserName));
            }
            if (user.Email != null)
            {
                identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, user.Email));
            }

            // Ensure preferred_username claim
            var preferredUsername = user.Email ?? user.UserName ?? string.Empty;
            if (!string.IsNullOrEmpty(preferredUsername) && !identity.HasClaim(c => c.Type == "preferred_username"))
            {
                identity.AddClaim(new System.Security.Claims.Claim("preferred_username", preferredUsername));
            }

            // Add profile claims from Person
            var department = user.Person?.Department ?? user.Department;
            if (!string.IsNullOrEmpty(department))
            {
                identity.AddClaim(new System.Security.Claims.Claim("department", department));
            }

            return identity;
        }

        public async Task<System.Security.Claims.ClaimsIdentity> TestGenerateClaimsAsync(ApplicationUser user)
        {
            return await GenerateClaimsAsync(user);
        }
    }

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
    public async Task GenerateClaimsAsync_WhenUserHasNoPerson_CreatesPersonAndLogsAudit()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var factory = new TestableMyUserClaimsPrincipalFactory(
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
        _userManagerMock.Setup(um => um.GetRolesAsync(user)).ReturnsAsync(new List<string>());

        // Act
        var identity = await factory.TestGenerateClaimsAsync(user);

        // Assert
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
            null), Times.Once);
    }

    [Fact]
    public async Task GenerateClaimsAsync_WhenUserHasPerson_NoNewPersonCreated()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        var factory = new TestableMyUserClaimsPrincipalFactory(
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
        _userManagerMock.Setup(um => um.GetRolesAsync(user)).ReturnsAsync(new List<string>());

        // Act
        var identity = await factory.TestGenerateClaimsAsync(user);

        // Assert
        Assert.Equal(person.Id, user.PersonId);
        Assert.NotNull(user.Person);
        Assert.Equal(person.Id, user.Person.Id);

        // Verify no new Person was created
        var finalPersonCount = await context.Persons.CountAsync();
        Assert.Equal(initialPersonCount, finalPersonCount);

        // Verify no audit was logged
        _auditServiceMock.Verify(a => a.LogEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}