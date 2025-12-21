using System.Security.Claims;
using Core.Application;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Services;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.Application.UnitTests;

/// <summary>
/// Unit tests for UserInfoService.
/// Tests OIDC compliance: UserInfo response must respect granted scopes.
/// Per OIDC Core 5.4 - Requesting Claims using Scope Values.
/// </summary>
public class UserInfoServiceTests
{
    [Fact]
    public async Task GetUserInfoAsync_NullPrincipal_ThrowsArgumentNullException()
    {
        // Arrange
        var mockDb = CreateMockDbContext(new List<ScopeClaim>());
        var service = new UserInfoService(mockDb.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GetUserInfoAsync(null!));
    }

    [Fact]
    public async Task GetUserInfoAsync_AlwaysReturnsSubject()
    {
        // Arrange
        var mockDb = CreateMockDbContext(new List<ScopeClaim>());
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim("scope", "openid")
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert - subject is always returned (OIDC requirement)
        Assert.Equal("user123", result[Claims.Subject]);
    }

    [Fact]
    public async Task GetUserInfoAsync_OnlyOpenIdScope_ReturnsOnlySubject()
    {
        // Arrange - no scope claims mapped for this test
        var scopeClaims = new List<ScopeClaim>
        {
            CreateScopeClaim("openid", "sub", "sub", "String")
        };
        var mockDb = CreateMockDbContext(scopeClaims);
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.Email, "test@example.com"), // In principal but not in granted scope
            new Claim("scope", "openid") // Only openid, no email scope
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert - only subject, no email
        Assert.Equal("user123", result[Claims.Subject]);
        Assert.False(result.ContainsKey(Claims.Email), "Email should not be returned without email scope");
    }

    [Fact]
    public async Task GetUserInfoAsync_WithEmailScope_ReturnsEmailClaims()
    {
        // Arrange
        var scopeClaims = new List<ScopeClaim>
        {
            CreateScopeClaim("openid", "sub", "sub", "String"),
            CreateScopeClaim("email", "email", "email", "String"),
            CreateScopeClaim("email", "email_verified", "email_verified", "Boolean")
        };
        var mockDb = CreateMockDbContext(scopeClaims);
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.Email, "test@example.com"),
            new Claim(Claims.EmailVerified, "true"),
            new Claim("scope", "openid email")
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert
        Assert.Equal("user123", result[Claims.Subject]);
        Assert.Equal("test@example.com", result[Claims.Email]);
        Assert.Equal(true, result[Claims.EmailVerified]);
    }

    [Fact]
    public async Task GetUserInfoAsync_WithProfileScope_ReturnsProfileClaims()
    {
        // Arrange
        var scopeClaims = new List<ScopeClaim>
        {
            CreateScopeClaim("openid", "sub", "sub", "String"),
            CreateScopeClaim("profile", "name", "name", "String"),
            CreateScopeClaim("profile", "preferred_username", "preferred_username", "String")
        };
        var mockDb = CreateMockDbContext(scopeClaims);
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.Name, "Test User"),
            new Claim(Claims.PreferredUsername, "testuser"),
            new Claim(Claims.Email, "test@example.com"), // Should NOT be returned
            new Claim("scope", "openid profile") // Has profile but not email
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert
        Assert.Equal("user123", result[Claims.Subject]);
        Assert.Equal("Test User", result[Claims.Name]);
        Assert.Equal("testuser", result[Claims.PreferredUsername]);
        Assert.False(result.ContainsKey(Claims.Email), "Email should not be returned without email scope");
    }

    [Fact]
    public async Task GetUserInfoAsync_WithRolesScope_ReturnsRoles()
    {
        // Arrange
        var scopeClaims = new List<ScopeClaim>
        {
            CreateScopeClaim("openid", "sub", "sub", "String")
        };
        var mockDb = CreateMockDbContext(scopeClaims);
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.Role, "admin"),
            new Claim(Claims.Role, "user"),
            new Claim("scope", "openid roles")
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert
        Assert.Equal("user123", result[Claims.Subject]);
        var roles = result[Claims.Role] as List<string>;
        Assert.NotNull(roles);
        Assert.Contains("admin", roles);
        Assert.Contains("user", roles);
    }

    [Fact]
    public async Task GetUserInfoAsync_NoScopes_ReturnsOnlySubject()
    {
        // Arrange - no scope claim at all
        var mockDb = CreateMockDbContext(new List<ScopeClaim>());
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.Email, "test@example.com")
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert - should only return subject
        Assert.Equal("user123", result[Claims.Subject]);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetUserInfoAsync_BooleanClaimFalse_ReturnsFalseBoolean()
    {
        // Arrange
        var scopeClaims = new List<ScopeClaim>
        {
            CreateScopeClaim("email", "email", "email", "String"),
            CreateScopeClaim("email", "email_verified", "email_verified", "Boolean")
        };
        var mockDb = CreateMockDbContext(scopeClaims);
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.Email, "test@example.com"),
            new Claim(Claims.EmailVerified, "false"),
            new Claim("scope", "openid email")
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert
        Assert.Equal(false, result[Claims.EmailVerified]);
    }

    [Fact]
    public async Task GetUserInfoAsync_ReturnsAmrClaims()
    {
        // Arrange
        var mockDb = CreateMockDbContext(new List<ScopeClaim>());
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.AuthenticationMethodReference, "pwd"),
            new Claim("scope", "openid")
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert
        Assert.Equal("pwd", result["amr"]);
    }

    [Fact]
    public async Task GetUserInfoAsync_MultipleAmrClaims_ReturnsAmrList()
    {
        // Arrange
        var mockDb = CreateMockDbContext(new List<ScopeClaim>());
        var service = new UserInfoService(mockDb.Object);

        var claims = new List<Claim>
        {
            new Claim(Claims.Subject, "user123"),
            new Claim(Claims.AuthenticationMethodReference, "pwd"),
            new Claim(Claims.AuthenticationMethodReference, "mfa"),
            new Claim("scope", "openid")
        };
        var principal = CreatePrincipal(claims);

        // Act
        var result = await service.GetUserInfoAsync(principal);

        // Assert
        var amr = result["amr"] as List<string>;
        Assert.NotNull(amr);
        Assert.Contains("pwd", amr);
        Assert.Contains("mfa", amr);
    }

    #region Helper Methods

    private static ClaimsPrincipal CreatePrincipal(List<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private static Mock<IApplicationDbContext> CreateMockDbContext(List<ScopeClaim> scopeClaims)
    {
        var mockDb = new Mock<IApplicationDbContext>();

        // Create a mock DbSet for ScopeClaims
        var mockSet = CreateMockDbSet(scopeClaims.AsQueryable());
        mockDb.Setup(db => db.ScopeClaims).Returns(mockSet.Object);

        return mockDb;
    }

    private static Mock<DbSet<ScopeClaim>> CreateMockDbSet(IQueryable<ScopeClaim> data)
    {
        var mockSet = new Mock<DbSet<ScopeClaim>>();

        mockSet.As<IAsyncEnumerable<ScopeClaim>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<ScopeClaim>(data.GetEnumerator()));

        mockSet.As<IQueryable<ScopeClaim>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<ScopeClaim>(data.Provider));

        mockSet.As<IQueryable<ScopeClaim>>().Setup(m => m.Expression).Returns(data.Expression);
        mockSet.As<IQueryable<ScopeClaim>>().Setup(m => m.ElementType).Returns(data.ElementType);
        mockSet.As<IQueryable<ScopeClaim>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());

        return mockSet;
    }

    private static ScopeClaim CreateScopeClaim(string scopeName, string claimName, string claimType, string dataType)
    {
        return new ScopeClaim
        {
            Id = Random.Shared.Next(1, 1000),
            ScopeId = Guid.NewGuid().ToString(),
            ScopeName = scopeName,
            UserClaimId = Random.Shared.Next(1, 1000),
            AlwaysInclude = claimName == "sub",
            UserClaim = new UserClaim
            {
                Id = Random.Shared.Next(1, 1000),
                Name = claimName,
                DisplayName = claimName,
                ClaimType = claimType,
                DataType = dataType,
                UserPropertyPath = claimName,
                IsStandard = true,
                IsRequired = claimName == "sub"
            }
        };
    }

    #endregion
}
