using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Core.Application;
using OpenIddict.Abstractions;
using Infrastructure.Services;

namespace Tests.Application.UnitTests;

public class ClientAllowedScopesServiceTests : IDisposable
{
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
    private readonly ClientAllowedScopesService _service;

    public ClientAllowedScopesServiceTests()
    {
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        _service = new ClientAllowedScopesService(_mockApplicationManager.Object);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region GetAllowedScopesAsync Tests

    [Fact]
    public async Task GetAllowedScopesAsync_ShouldReturnScopes_WhenClientExists()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var permissions = new List<string>
        {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid",
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}profile",
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}email"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var scopes = await _service.GetAllowedScopesAsync(Guid.Parse(clientId));

        // Assert
        Assert.NotNull(scopes);
        Assert.Equal(3, scopes.Count);
        Assert.Contains("openid", scopes);
        Assert.Contains("profile", scopes);
        Assert.Contains("email", scopes);
    }

    [Fact]
    public async Task GetAllowedScopesAsync_ShouldReturnEmpty_WhenClientNotFound()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        
        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var scopes = await _service.GetAllowedScopesAsync(Guid.Parse(clientId));

        // Assert
        Assert.NotNull(scopes);
        Assert.Empty(scopes);
    }

    [Fact]
    public async Task GetAllowedScopesAsync_ShouldReturnEmpty_WhenNoScopePermissions()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var permissions = new List<string>
        {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var scopes = await _service.GetAllowedScopesAsync(Guid.Parse(clientId));

        // Assert
        Assert.NotNull(scopes);
        Assert.Empty(scopes);
    }

    #endregion

    #region SetAllowedScopesAsync Tests

    [Fact]
    public async Task SetAllowedScopesAsync_ShouldSetScopes_WhenClientExists()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var newScopes = new List<string> { "openid", "profile", "email" };
        var existingPermissions = new List<string>
        {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}oldscope"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPermissions);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), mockClient, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((descriptor, _, _) =>
            {
                // Simulate PopulateAsync by adding existing permissions to descriptor
                foreach (var permission in existingPermissions)
                {
                    descriptor.Permissions.Add(permission);
                }
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(It.IsAny<object>(), It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _service.SetAllowedScopesAsync(Guid.Parse(clientId), newScopes);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(
            mockClient,
            It.Is<OpenIddictApplicationDescriptor>(d => 
                d.Permissions.Contains($"{OpenIddictConstants.Permissions.Prefixes.Scope}openid") &&
                d.Permissions.Contains($"{OpenIddictConstants.Permissions.Prefixes.Scope}profile") &&
                d.Permissions.Contains($"{OpenIddictConstants.Permissions.Prefixes.Scope}email") &&
                !d.Permissions.Contains($"{OpenIddictConstants.Permissions.Prefixes.Scope}oldscope") &&
                d.Permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Authorization) &&
                d.Permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Token)
            ),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task SetAllowedScopesAsync_ShouldThrowException_WhenClientNotFound()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var newScopes = new List<string> { "openid" };

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.SetAllowedScopesAsync(Guid.Parse(clientId), newScopes)
        );
    }

    [Fact]
    public async Task SetAllowedScopesAsync_ShouldPreserveNonScopePermissions()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var newScopes = new List<string> { "openid" };
        var existingPermissions = new List<string>
        {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}oldscope"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPermissions);
        _mockApplicationManager.Setup(m => m.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), mockClient, It.IsAny<CancellationToken>()))
            .Callback<OpenIddictApplicationDescriptor, object, CancellationToken>((descriptor, _, _) =>
            {
                // Simulate PopulateAsync by adding existing permissions to descriptor
                foreach (var permission in existingPermissions)
                {
                    descriptor.Permissions.Add(permission);
                }
            })
            .Returns(ValueTask.CompletedTask);
        _mockApplicationManager.Setup(m => m.UpdateAsync(It.IsAny<object>(), It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _service.SetAllowedScopesAsync(Guid.Parse(clientId), newScopes);

        // Assert
        _mockApplicationManager.Verify(m => m.UpdateAsync(
            mockClient,
            It.Is<OpenIddictApplicationDescriptor>(d => 
                d.Permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Authorization) &&
                d.Permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Token) &&
                d.Permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode) &&
                d.Permissions.Contains($"{OpenIddictConstants.Permissions.Prefixes.Scope}openid") &&
                !d.Permissions.Contains($"{OpenIddictConstants.Permissions.Prefixes.Scope}oldscope")
            ),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    #endregion

    #region IsScopeAllowedAsync Tests

    [Fact]
    public async Task IsScopeAllowedAsync_ShouldReturnTrue_WhenScopeIsAllowed()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var permissions = new List<string>
        {
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid",
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}profile"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _service.IsScopeAllowedAsync(Guid.Parse(clientId), "openid");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsScopeAllowedAsync_ShouldReturnFalse_WhenScopeIsNotAllowed()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var permissions = new List<string>
        {
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _service.IsScopeAllowedAsync(Guid.Parse(clientId), "email");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsScopeAllowedAsync_ShouldReturnFalse_WhenClientNotFound()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _service.IsScopeAllowedAsync(Guid.Parse(clientId), "openid");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ValidateRequestedScopesAsync Tests

    [Fact]
    public async Task ValidateRequestedScopesAsync_ShouldReturnAllowedScopes_WhenAllAreAllowed()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var requestedScopes = new List<string> { "openid", "profile", "email" };
        var permissions = new List<string>
        {
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid",
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}profile",
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}email"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var allowed = await _service.ValidateRequestedScopesAsync(Guid.Parse(clientId), requestedScopes);

        // Assert
        Assert.NotNull(allowed);
        Assert.Equal(3, allowed.Count);
        Assert.Contains("openid", allowed);
        Assert.Contains("profile", allowed);
        Assert.Contains("email", allowed);
    }

    [Fact]
    public async Task ValidateRequestedScopesAsync_ShouldReturnOnlyAllowedScopes_WhenSomeAreNotAllowed()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var requestedScopes = new List<string> { "openid", "profile", "notallowed" };
        var permissions = new List<string>
        {
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid",
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}profile"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var allowed = await _service.ValidateRequestedScopesAsync(Guid.Parse(clientId), requestedScopes);

        // Assert
        Assert.NotNull(allowed);
        Assert.Equal(2, allowed.Count);
        Assert.Contains("openid", allowed);
        Assert.Contains("profile", allowed);
        Assert.DoesNotContain("notallowed", allowed);
    }

    [Fact]
    public async Task ValidateRequestedScopesAsync_ShouldReturnEmpty_WhenNoScopesAreAllowed()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var requestedScopes = new List<string> { "notallowed1", "notallowed2" };
        var permissions = new List<string>
        {
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var allowed = await _service.ValidateRequestedScopesAsync(Guid.Parse(clientId), requestedScopes);

        // Assert
        Assert.NotNull(allowed);
        Assert.Empty(allowed);
    }

    [Fact]
    public async Task ValidateRequestedScopesAsync_ShouldReturnEmpty_WhenClientNotFound()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var requestedScopes = new List<string> { "openid" };

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var allowed = await _service.ValidateRequestedScopesAsync(Guid.Parse(clientId), requestedScopes);

        // Assert
        Assert.NotNull(allowed);
        Assert.Empty(allowed);
    }

    [Fact]
    public async Task ValidateRequestedScopesAsync_ShouldHandleEmptyRequestedScopes()
    {
        // Arrange
        var clientId = Guid.NewGuid().ToString();
        var mockClient = new object();
        var requestedScopes = new List<string>();
        var permissions = new List<string>
        {
            $"{OpenIddictConstants.Permissions.Prefixes.Scope}openid"
        }.ToImmutableArray();

        _mockApplicationManager.Setup(m => m.FindByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockClient);
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(mockClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var allowed = await _service.ValidateRequestedScopesAsync(Guid.Parse(clientId), requestedScopes);

        // Assert
        Assert.NotNull(allowed);
        Assert.Empty(allowed);
    }

    #endregion
}
