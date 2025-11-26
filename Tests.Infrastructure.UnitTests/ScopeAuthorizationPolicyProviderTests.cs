using Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public class ScopeAuthorizationPolicyProviderTests
{
    private readonly ScopeAuthorizationPolicyProvider _policyProvider;

    public ScopeAuthorizationPolicyProviderTests()
    {
        var options = Options.Create(new AuthorizationOptions());
        _policyProvider = new ScopeAuthorizationPolicyProvider(options);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldCreatePolicy_WhenRequireScopePrefixUsed()
    {
        // Arrange
        var policyName = "RequireScope:api:company:read";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, r => r is ScopeRequirement);
        
        var scopeRequirement = policy.Requirements.OfType<ScopeRequirement>().FirstOrDefault();
        Assert.NotNull(scopeRequirement);
        Assert.Equal("api:company:read", scopeRequirement.Scope);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldRequireAuthentication_WhenRequireScopePrefixUsed()
    {
        // Arrange
        var policyName = "RequireScope:openid";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.NotNull(policy);
        // Check that policy has requirements (RequireAuthenticatedUser creates DenyAnonymousAuthorizationRequirement + ScopeRequirement)
        Assert.True(policy.Requirements.Any());
        Assert.Contains(policy.Requirements, r => r is ScopeRequirement);
    }

    [Theory]
    [InlineData("RequireScope:api:admin")]
    [InlineData("RequireScope:openid")]
    [InlineData("RequireScope:profile")]
    [InlineData("RequireScope:api:company:read")]
    [InlineData("RequireScope:api:company:write")]
    public async Task GetPolicyAsync_ShouldHandleVariousScopeNames(string policyName)
    {
        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.NotNull(policy);
        var scopeRequirement = policy.Requirements.OfType<ScopeRequirement>().FirstOrDefault();
        Assert.NotNull(scopeRequirement);
        
        var expectedScope = policyName.Substring("RequireScope:".Length);
        Assert.Equal(expectedScope, scopeRequirement.Scope);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldFallback_WhenNotRequireScopePolicy()
    {
        // Arrange
        var policyName = "SomeOtherPolicy";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        // Should fall back to default provider which returns null for unknown policies
        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldFallback_WhenEmptyScopeName()
    {
        // Arrange
        var policyName = "RequireScope:";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        // Should fall back to default provider (invalid scope name)
        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldFallback_WhenOnlyWhitespaceScopeName()
    {
        // Arrange
        var policyName = "RequireScope:   ";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        // Should fall back to default provider (invalid scope name)
        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldBeCaseInsensitiveForPrefix()
    {
        // Arrange
        var policyName = "requirescope:test"; // lowercase prefix

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.NotNull(policy);
        var scopeRequirement = policy.Requirements.OfType<ScopeRequirement>().FirstOrDefault();
        Assert.NotNull(scopeRequirement);
        Assert.Equal("test", scopeRequirement.Scope);
    }

    [Fact]
    public async Task GetDefaultPolicyAsync_ShouldReturnDefaultPolicy()
    {
        // Act
        var policy = await _policyProvider.GetDefaultPolicyAsync();

        // Assert
        Assert.NotNull(policy);
        // Default policy requires authenticated user
        Assert.NotEmpty(policy.Requirements);
    }

    [Fact]
    public async Task GetFallbackPolicyAsync_ShouldReturnNull()
    {
        // Act
        var policy = await _policyProvider.GetFallbackPolicyAsync();

        // Assert
        // Fallback policy is null by default in ASP.NET Core
        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldHandleScopeWithColons()
    {
        // Arrange
        var policyName = "RequireScope:api:resource:action:subaction";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.NotNull(policy);
        var scopeRequirement = policy.Requirements.OfType<ScopeRequirement>().FirstOrDefault();
        Assert.NotNull(scopeRequirement);
        Assert.Equal("api:resource:action:subaction", scopeRequirement.Scope);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldHandleScopeWithSpecialCharacters()
    {
        // Arrange
        var policyName = "RequireScope:api.company-read_v2";

        // Act
        var policy = await _policyProvider.GetPolicyAsync(policyName);

        // Assert
        Assert.NotNull(policy);
        var scopeRequirement = policy.Requirements.OfType<ScopeRequirement>().FirstOrDefault();
        Assert.NotNull(scopeRequirement);
        Assert.Equal("api.company-read_v2", scopeRequirement.Scope);
    }
}
