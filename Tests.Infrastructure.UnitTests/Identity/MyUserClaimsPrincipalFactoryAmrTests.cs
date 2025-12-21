using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests.Identity;

public class MyUserClaimsPrincipalFactoryAmrTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<RoleManager<ApplicationRole>> _roleManagerMock;
    private readonly Mock<IOptions<IdentityOptions>> _optionsAccessorMock;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ILogger<MyUserClaimsPrincipalFactory>> _loggerMock;

    public MyUserClaimsPrincipalFactoryAmrTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(userStoreMock.Object, null, null, null, null, null, null, null, null);
        
        var roleStoreMock = new Mock<IRoleStore<ApplicationRole>>();
        _roleManagerMock = new Mock<RoleManager<ApplicationRole>>(roleStoreMock.Object, null, null, null, null);

        _optionsAccessorMock = new Mock<IOptions<IdentityOptions>>();
        _optionsAccessorMock.Setup(o => o.Value).Returns(new IdentityOptions());

        _auditServiceMock = new Mock<IAuditService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<MyUserClaimsPrincipalFactory>>();
    }

    public void Dispose()
    {
        using var context = new ApplicationDbContext(_options);
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task GenerateClaimsAsync_AddsAmrClaimsFromSession()
    {
        // Arrange
        using var context = new ApplicationDbContext(_options);
        
        // Setup Person for user
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            CreatedAt = DateTime.UtcNow
        };
        context.Persons.Add(person);
        await context.SaveChangesAsync();

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
            PersonId = person.Id,
            Person = person
        };

        var amrValues = new List<string> { "pwd", "mfa", "otp" };
        var amrJson = JsonSerializer.Serialize(amrValues);
        
        var sessionMock = new Mock<ISession>();
        var sessionBytes = System.Text.Encoding.UTF8.GetBytes(amrJson);
        
        // Mock session.GetString equivalent
        sessionMock.Setup(s => s.TryGetValue("AuthenticationMethods", out sessionBytes)).Returns(true);

        var httpContextMock = new Mock<HttpContext>();
        httpContextMock.Setup(c => c.Session).Returns(sessionMock.Object);
        _httpContextAccessorMock.Setup(a => a.HttpContext).Returns(httpContextMock.Object);

        _userManagerMock.Setup(um => um.GetRolesAsync(user)).ReturnsAsync(new List<string>());
        _userManagerMock.Setup(um => um.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
        _userManagerMock.Setup(um => um.GetUserNameAsync(user)).ReturnsAsync(user.UserName);

        // Act
        var principal = await factory.CreateAsync(user);
        var identity = principal.Identity as ClaimsIdentity;

        // Assert
        Assert.NotNull(identity);
        var amrClaims = identity.FindAll("amr").Select(c => c.Value).ToList();
        Assert.Contains("pwd", amrClaims);
        Assert.Contains("mfa", amrClaims);
        Assert.Contains("otp", amrClaims);
        Assert.Equal(3, amrClaims.Count);
    }
}
