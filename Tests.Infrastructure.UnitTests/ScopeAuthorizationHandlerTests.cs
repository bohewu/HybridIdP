using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public class ScopeAuthorizationHandlerTests
{
    private readonly Mock<ILogger<ScopeAuthorizationHandler>> _mockLogger;
    private readonly ScopeAuthorizationHandler _handler;

    public ScopeAuthorizationHandlerTests()
    {
        _mockLogger = new Mock<ILogger<ScopeAuthorizationHandler>>();
        _handler = new ScopeAuthorizationHandler(_mockLogger.Object);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldSucceed_WhenScopeClaimPresent()
    {
        // Arrange
        var requirement = new ScopeRequirement("api:company:read");
        var claims = new[]
        {
            new Claim("scope", "api:company:read api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldFail_WhenScopeClaimMissing()
    {
        // Arrange
        var requirement = new ScopeRequirement("api:admin");
        var claims = new[]
        {
            new Claim("scope", "api:company:read api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldSucceed_WhenScpClaimPresent()
    {
        // Arrange (Azure AD format with "scp" claims)
        var requirement = new ScopeRequirement("api:company:read");
        var claims = new[]
        {
            new Claim("scp", "api:company:read"),
            new Claim("scp", "api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldSucceed_WhenScopePresentInEitherClaimType()
    {
        // Arrange (mixed format - both scope and scp)
        var requirement = new ScopeRequirement("openid");
        var claims = new[]
        {
            new Claim("scope", "profile email"),
            new Claim("scp", "openid"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        var requirement = new ScopeRequirement("API:Company:Read");
        var claims = new[]
        {
            new Claim("scope", "api:company:read api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldFail_WhenUserNotAuthenticated()
    {
        // Arrange
        var requirement = new ScopeRequirement("api:company:read");
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // No authentication type
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldFail_WhenUserIsNull()
    {
        // Arrange
        var requirement = new ScopeRequirement("api:company:read");
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, 
            new ClaimsPrincipal(), 
            null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldFail_WhenNoScopeClaims()
    {
        // Arrange
        var requirement = new ScopeRequirement("api:company:read");
        var claims = new[]
        {
            new Claim("sub", "user123"),
            new Claim("name", "Test User")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldHandleEmptyScopeClaim()
    {
        // Arrange
        var requirement = new ScopeRequirement("api:company:read");
        var claims = new[]
        {
            new Claim("scope", "   "),  // Empty/whitespace
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldHandleMultipleSpaceSeparatedScopes()
    {
        // Arrange
        var requirement = new ScopeRequirement("profile");
        var claims = new[]
        {
            new Claim("scope", "openid profile email offline_access"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_ShouldHandleMultipleScpClaims()
    {
        // Arrange
        var requirement = new ScopeRequirement("email");
        var claims = new[]
        {
            new Claim("scp", "openid"),
            new Claim("scp", "profile"),
            new Claim("scp", "email"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        // Act
        await ((IAuthorizationHandler)_handler).HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public void ScopeRequirement_ShouldThrowArgumentNullException_WhenScopeIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ScopeRequirement(null!));
    }

    [Fact]
    public void ScopeRequirement_ShouldStoreScope()
    {
        // Arrange & Act
        var requirement = new ScopeRequirement("test:scope");

        // Assert
        Assert.Equal("test:scope", requirement.Scope);
    }
}
