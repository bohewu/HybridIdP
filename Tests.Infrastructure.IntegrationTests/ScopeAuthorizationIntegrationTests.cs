using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

/// <summary>
/// Integration tests for scope authorization system
/// Tests the interaction between ScopeRequirement, ScopeAuthorizationHandler, and ScopeAuthorizationPolicyProvider
/// </summary>
public class ScopeAuthorizationIntegrationTests
{
    private readonly ScopeAuthorizationHandler _handler;
    private readonly ScopeAuthorizationPolicyProvider _policyProvider;
    private readonly Mock<ILogger<ScopeAuthorizationHandler>> _mockLogger;

    public ScopeAuthorizationIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<ScopeAuthorizationHandler>>();
        _handler = new ScopeAuthorizationHandler(_mockLogger.Object);

        // Create policy provider
        var options = Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions());
        _policyProvider = new ScopeAuthorizationPolicyProvider(options);
    }

    private async Task<bool> TestAuthorizationAsync(ClaimsPrincipal user, string scopeName)
    {
        var requirement = new ScopeRequirement(scopeName);
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);
        await ((IAuthorizationHandler)_handler).HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldSucceed_WhenUserHasRequiredScope()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "api:company:read api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = await TestAuthorizationAsync(user, "api:company:read");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldFail_WhenUserLacksRequiredScope()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "api:company:read"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = await TestAuthorizationAsync(user, "api:admin");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldFail_WhenUserNotAuthenticated()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // No authentication

        // Act
        var result = await TestAuthorizationAsync(user, "api:company:read");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldSucceed_WithScpClaimFormat()
    {
        // Arrange (Azure AD format)
        var claims = new[]
        {
            new Claim("scp", "api:company:read"),
            new Claim("scp", "api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = await TestAuthorizationAsync(user, "api:company:write");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldSucceed_WithMixedClaimFormats()
    {
        // Arrange (both scope and scp)
        var claims = new[]
        {
            new Claim("scope", "openid profile"),
            new Claim("scp", "email"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act - Check all three scopes
        var result1 = await TestAuthorizationAsync(user, "openid");
        var result2 = await TestAuthorizationAsync(user, "profile");
        var result3 = await TestAuthorizationAsync(user, "email");

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "API:Company:Read"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = await TestAuthorizationAsync(user, "api:company:read");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldHandleMultipleScopes()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "openid profile email offline_access api:company:read api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act - Test multiple scope requirements
        var result1 = await TestAuthorizationAsync(user, "openid");
        var result2 = await TestAuthorizationAsync(user, "profile");
        var result3 = await TestAuthorizationAsync(user, "email");
        var result4 = await TestAuthorizationAsync(user, "offline_access");
        var result5 = await TestAuthorizationAsync(user, "api:company:read");
        var result6 = await TestAuthorizationAsync(user, "api:company:write");

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
        Assert.True(result4);
        Assert.True(result5);
        Assert.True(result6);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldFail_ForNonExistentScope()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "api:company:read api:company:write"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = await TestAuthorizationAsync(user, "api:company:delete");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldHandleEmptyScopeClaim()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "   "), // Empty/whitespace
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = await TestAuthorizationAsync(user, "api:company:read");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldSucceed_WithComplexScopeName()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "api:resource:action:subaction api.other-scope_v2"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result1 = await TestAuthorizationAsync(user, "api:resource:action:subaction");
        var result2 = await TestAuthorizationAsync(user, "api.other-scope_v2");

        // Assert
        Assert.True(result1);
        Assert.True(result2);
    }

    [Fact]
    public async Task PolicyProvider_ShouldCreatePolicyForScopePattern()
    {
        // Arrange
        var policyName = "RequireScope:api:test";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, r => r is ScopeRequirement sr && sr.Scope == "api:test");
    }

    [Fact]
    public async Task PolicyProvider_ShouldReturnNullForNonScopePolicies()
    {
        // Arrange
        var policyName = "SomeOtherPolicy";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.Null(policy); // Falls back to default which returns null for unknown policies
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldHandlePartialScopeMatch()
    {
        // Arrange - User has "api:company" but needs "api:company:read"
        var claims = new[]
        {
            new Claim("scope", "api:company"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act
        var result = await TestAuthorizationAsync(user, "api:company:read");

        // Assert - Should fail (exact match required, not prefix)
        Assert.False(result);
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldHandleMultipleAuthorizationChecks()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("scope", "api:company:read"),
            new Claim("sub", "user123")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // Act - Multiple sequential checks
        var result1 = await TestAuthorizationAsync(user, "api:company:read");
        var result2 = await TestAuthorizationAsync(user, "api:company:write");
        var result3 = await TestAuthorizationAsync(user, "api:company:read");

        // Assert
        Assert.True(result1);
        Assert.False(result2);
        Assert.True(result3);
    }
}
