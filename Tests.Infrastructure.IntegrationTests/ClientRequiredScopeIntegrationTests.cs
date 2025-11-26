using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.IntegrationTests;

/// <summary>
/// Integration tests for ClientRequiredScope functionality.
/// Uses real EF Core in-memory database and mocked OpenIddict managers.
/// </summary>
public class ClientRequiredScopeIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ClientAllowedScopesService _service;
    private readonly Mock<IOpenIddictApplicationManager> _mockAppManager;
    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
    private readonly Guid _testClientId;
    private readonly string _testClientIdString;

    public ClientRequiredScopeIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _mockAppManager = new Mock<IOpenIddictApplicationManager>();
        _mockScopeManager = new Mock<IOpenIddictScopeManager>();
        
        _testClientId = Guid.NewGuid();
        _testClientIdString = _testClientId.ToString();

        _service = new ClientAllowedScopesService(
            _mockAppManager.Object,
            _mockScopeManager.Object,
            _db);

        // Setup mock client
        var mockClient = new object();
        _mockAppManager.Setup(m => m.FindByIdAsync(_testClientIdString, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    #region GetRequiredScopesAsync Tests

    [Fact]
    public async Task GetRequiredScopesAsync_ShouldReturnEmpty_WhenNoRequiredScopes()
    {
        // Act
        var result = await _service.GetRequiredScopesAsync(_testClientId);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRequiredScopesAsync_ShouldReturnScopeNames_WhenRequiredScopesExist()
    {
        // Arrange
        var scope1Id = Guid.NewGuid().ToString();
        var scope2Id = Guid.NewGuid().ToString();

        // Add required scopes to database
        _db.ClientRequiredScopes.AddRange(
            new ClientRequiredScope { ClientId = _testClientIdString, ScopeId = scope1Id },
            new ClientRequiredScope { ClientId = _testClientIdString, ScopeId = scope2Id }
        );
        await _db.SaveChangesAsync();

        // Mock OpenIddict scope manager to return scope names
        var mockScope1 = new object();
        var mockScope2 = new object();
        var mockScopes = new List<object> { mockScope1, mockScope2 };

        _mockScopeManager.Setup(m => m.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(mockScopes));
        
        _mockScopeManager.Setup(m => m.GetIdAsync(mockScope1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope1Id);
        _mockScopeManager.Setup(m => m.GetIdAsync(mockScope2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope2Id);
        
        _mockScopeManager.Setup(m => m.GetNameAsync(mockScope1, It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");
        _mockScopeManager.Setup(m => m.GetNameAsync(mockScope2, It.IsAny<CancellationToken>()))
            .ReturnsAsync("profile");

        // Act
        var result = await _service.GetRequiredScopesAsync(_testClientId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains("openid", result);
        Assert.Contains("profile", result);
    }

    #endregion

    #region SetRequiredScopesAsync Tests

    [Fact]
    public async Task SetRequiredScopesAsync_ShouldThrowException_WhenClientNotFound()
    {
        // Arrange
        var nonExistentClientId = Guid.NewGuid();
        _mockAppManager.Setup(m => m.FindByIdAsync(nonExistentClientId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetRequiredScopesAsync(nonExistentClientId, new[] { "openid" }));
    }

    [Fact]
    public async Task SetRequiredScopesAsync_ShouldThrowException_WhenRequiredScopeNotInAllowedScopes()
    {
        // Arrange
        var allowedScopes = new[] { "openid", "profile" };
        SetupAllowedScopes(allowedScopes);

        var mockScope = new object();
        var scopeId = Guid.NewGuid().ToString();
        _mockScopeManager.Setup(m => m.FindByNameAsync("email", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockScope);
        _mockScopeManager.Setup(m => m.GetIdAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeId);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetRequiredScopesAsync(_testClientId, new[] { "email" }));
        
        Assert.Contains("not in the client's allowed scopes", exception.Message);
    }

    [Fact]
    public async Task SetRequiredScopesAsync_ShouldThrowException_WhenScopeNotFound()
    {
        // Arrange
        SetupAllowedScopes(new[] { "openid", "nonexistent" });
        
        _mockScopeManager.Setup(m => m.FindByNameAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetRequiredScopesAsync(_testClientId, new[] { "nonexistent" }));
        
        Assert.Contains("Scope 'nonexistent' not found", exception.Message);
    }

    [Fact]
    public async Task SetRequiredScopesAsync_ShouldAddRequiredScopes_WhenValid()
    {
        // Arrange
        var allowedScopes = new[] { "openid", "profile" };
        SetupAllowedScopes(allowedScopes);

        var scope1 = new object();
        var scope2 = new object();
        var scope1Id = Guid.NewGuid().ToString();
        var scope2Id = Guid.NewGuid().ToString();

        _mockScopeManager.Setup(m => m.FindByNameAsync("openid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope1);
        _mockScopeManager.Setup(m => m.FindByNameAsync("profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope2);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope1Id);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope2Id);

        // Act
        await _service.SetRequiredScopesAsync(_testClientId, new[] { "openid", "profile" });

        // Assert
        var requiredScopes = await _db.ClientRequiredScopes
            .Where(crs => crs.ClientId == _testClientIdString)
            .ToListAsync();
        
        Assert.Equal(2, requiredScopes.Count);
        Assert.Contains(requiredScopes, rs => rs.ScopeId == scope1Id);
        Assert.Contains(requiredScopes, rs => rs.ScopeId == scope2Id);
        Assert.All(requiredScopes, rs => Assert.True(rs.CreatedAt <= DateTime.UtcNow));
    }

    [Fact]
    public async Task SetRequiredScopesAsync_ShouldReplaceExistingScopes()
    {
        // Arrange
        var oldScopeId = Guid.NewGuid().ToString();
        _db.ClientRequiredScopes.Add(new ClientRequiredScope
        {
            ClientId = _testClientIdString,
            ScopeId = oldScopeId
        });
        await _db.SaveChangesAsync();

        SetupAllowedScopes(new[] { "openid" });

        var newScope = new object();
        var newScopeId = Guid.NewGuid().ToString();
        _mockScopeManager.Setup(m => m.FindByNameAsync("openid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newScope);
        _mockScopeManager.Setup(m => m.GetIdAsync(newScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newScopeId);

        // Act
        await _service.SetRequiredScopesAsync(_testClientId, new[] { "openid" });

        // Assert
        var requiredScopes = await _db.ClientRequiredScopes
            .Where(crs => crs.ClientId == _testClientIdString)
            .ToListAsync();
        
        Assert.Single(requiredScopes);
        Assert.Equal(newScopeId, requiredScopes[0].ScopeId);
    }

    #endregion

    #region IsScopeRequiredAsync Tests

    [Fact]
    public async Task IsScopeRequiredAsync_ShouldReturnFalse_WhenScopeNotRequired()
    {
        // Act
        var result = await _service.IsScopeRequiredAsync(_testClientId, "openid");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsScopeRequiredAsync_ShouldReturnTrue_WhenScopeIsRequired()
    {
        // Arrange
        var scopeId = Guid.NewGuid().ToString();
        _db.ClientRequiredScopes.Add(new ClientRequiredScope
        {
            ClientId = _testClientIdString,
            ScopeId = scopeId
        });
        await _db.SaveChangesAsync();

        var mockScope = new object();
        var mockScopes = new List<object> { mockScope };
        _mockScopeManager.Setup(m => m.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(mockScopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeId);
        _mockScopeManager.Setup(m => m.GetNameAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");

        // Act
        var result = await _service.IsScopeRequiredAsync(_testClientId, "openid");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsScopeRequiredAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        var scopeId = Guid.NewGuid().ToString();
        _db.ClientRequiredScopes.Add(new ClientRequiredScope
        {
            ClientId = _testClientIdString,
            ScopeId = scopeId
        });
        await _db.SaveChangesAsync();

        var mockScope = new object();
        var mockScopes = new List<object> { mockScope };
        _mockScopeManager.Setup(m => m.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(mockScopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeId);
        _mockScopeManager.Setup(m => m.GetNameAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");

        // Act
        var result = await _service.IsScopeRequiredAsync(_testClientId, "OPENID");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Helper Methods

    private void SetupAllowedScopes(string[] scopes)
    {
        var permissions = scopes.Select(s => $"{OpenIddictConstants.Permissions.Prefixes.Scope}{s}").ToList();
        permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
        permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);

        var mockClient = new object();
        _mockAppManager.Setup(m => m.FindByIdAsync(_testClientIdString, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockAppManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Collections.Immutable.ImmutableArray.CreateRange(permissions));
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    #endregion
}
