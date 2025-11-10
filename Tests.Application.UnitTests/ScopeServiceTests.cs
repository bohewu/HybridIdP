using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenIddict.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class ScopeServiceTests : IDisposable
{
    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
    private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly ScopeService _scopeService;

    public ScopeServiceTests()
    {
        _mockScopeManager = new Mock<IOpenIddictScopeManager>();
        _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
        
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new ApplicationDbContext(options);
        
        _scopeService = new ScopeService(_mockScopeManager.Object, _mockApplicationManager.Object, _dbContext);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    #region GetScopesAsync Tests

    [Fact]
    public async Task GetScopesAsync_ShouldReturnAllScopes_WhenNoFiltersApplied()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension 
        { 
            ScopeId = "scope1", 
            ConsentDisplayName = "Consent 1", 
            DisplayOrder = 1 
        });
        await _dbContext.SaveChangesAsync();

        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "openid" }
        };
        
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("scope1");
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID scope");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("resource1"));

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(0, 25, null, null);

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        var scope = items.First();
        Assert.Equal("scope1", scope.Id);
        Assert.Equal("openid", scope.Name);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldFilterBySearch_WhenSearchProvided()
    {
        // Arrange
        // No ScopeExtensions needed for this test

        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "openid" },
            new { Id = "scope2", Name = "profile" }
        };
        
        var index = 0;
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"scope{++index}");
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object scope, CancellationToken _) => 
                scope.ToString()!.Contains("scope1") ? "openid" : "profile");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(0, 25, "openid", null);

        // Assert
        Assert.Single(items);
        Assert.Equal(1, totalCount);
        Assert.Contains(items, s => s.Name.Contains("openid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetScopesAsync_ShouldApplyPagination_WhenSkipAndTakeProvided()
    {
        // Arrange
        // No ScopeExtensions needed for this test

        var scopes = Enumerable.Range(1, 30).Select(i => new { Id = $"scope{i}", Name = $"scope{i}" }).ToList();
        
        var index = 0;
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes.Cast<object>()));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"scope{++index}");
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object scope, CancellationToken _) => scope.ToString());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(10, 5, null, null);

        // Assert
        Assert.Equal(5, items.Count());
        Assert.Equal(30, totalCount);
    }

    [Fact]
    public async Task GetScopesAsync_ShouldSortAscending_WhenSortParameterIsNameAsc()
    {
        // Arrange
        // No ScopeExtensions needed for this test

        var scopes = new List<object>
        {
            new { Id = "scope1", Name = "zeta" },
            new { Id = "scope2", Name = "alpha" }
        };
        
        var names = new Queue<string>(new[] { "zeta", "alpha" });
        _mockScopeManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(scopes));
        _mockScopeManager.Setup(m => m.GetIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid().ToString());
        _mockScopeManager.Setup(m => m.GetNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => names.Dequeue());
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray<string>.Empty);

        // Act
        var (items, totalCount) = await _scopeService.GetScopesAsync(0, 25, null, "name:asc");

        // Assert
        var itemList = items.ToList();
        Assert.Equal("alpha", itemList[0].Name);
        Assert.Equal("zeta", itemList[1].Name);
    }

    #endregion

    #region GetScopeByIdAsync Tests

    [Fact]
    public async Task GetScopeByIdAsync_ShouldReturnScope_WhenScopeExists()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension 
        { 
            ScopeId = "scope1", 
            ConsentDisplayName = "Consent Name" 
        });
        await _dbContext.SaveChangesAsync();

        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scope1");
        _mockScopeManager.Setup(m => m.GetNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("openid");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("OpenID scope");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("resource1"));

        // Act
        var result = await _scopeService.GetScopeByIdAsync("scope1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("scope1", result.Id);
        Assert.Equal("openid", result.Name);
        Assert.Equal("Consent Name", result.ConsentDisplayName);
    }

    [Fact]
    public async Task GetScopeByIdAsync_ShouldReturnNull_WhenScopeDoesNotExist()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _scopeService.GetScopeByIdAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region CreateScopeAsync Tests

    [Fact]
    public async Task CreateScopeAsync_ShouldCreateScope_WithDefaultResource()
    {
        // Arrange
        var request = new CreateScopeRequest(
            Name: "newscope",
            DisplayName: "New Scope",
            Description: "A new scope",
            Resources: null
        );

        var scope = new { Id = "newscope" };
        _mockScopeManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictScopeDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("newscope");

        // Act
        var result = await _scopeService.CreateScopeAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("newscope", result.Id);
        Assert.Equal("newscope", result.Name);
        _mockScopeManager.Verify(m => m.CreateAsync(
            It.Is<OpenIddictScopeDescriptor>(d => 
                d.Name == "newscope" && 
                d.Resources.Contains(AuthConstants.Resources.ResourceServer)), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateScopeAsync_ShouldCreateScopeExtension_WhenConsentFieldsProvided()
    {
        // Arrange
        var request = new CreateScopeRequest(
            Name: "newscope",
            DisplayName: "New Scope",
            Description: null,
            Resources: null,
            ConsentDisplayName: "Consent Display",
            ConsentDescription: "Consent Description",
            IconUrl: "icon.png",
            IsRequired: true,
            DisplayOrder: 5,
            Category: "Custom"
        );

        var scope = new { Id = "newscope" };
        _mockScopeManager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictScopeDescriptor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("newscope");

        // Act
        var result = await _scopeService.CreateScopeAsync(request);

        // Assert
        Assert.NotNull(result);
        var extension = await _dbContext.ScopeExtensions.FirstOrDefaultAsync(e => e.ScopeId == "newscope");
        Assert.NotNull(extension);
        Assert.Equal("Consent Display", extension.ConsentDisplayName);
        Assert.Equal("Consent Description", extension.ConsentDescription);
        Assert.Equal("icon.png", extension.IconUrl);
        Assert.True(extension.IsRequired);
        Assert.Equal(5, extension.DisplayOrder);
        Assert.Equal("Custom", extension.Category);
    }

    #endregion

    #region UpdateScopeAsync Tests

    [Fact]
    public async Task UpdateScopeAsync_ShouldReturnFalse_WhenScopeDoesNotExist()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        var request = new UpdateScopeRequest(Name: "updated", null, null, null);

        // Act
        var result = await _scopeService.UpdateScopeAsync("nonexistent", request);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateScopeAsync_ShouldUpdateScope_WhenScopeExists()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension 
        { 
            ScopeId = "scope1", 
            ConsentDisplayName = "Old Consent" 
        });
        await _dbContext.SaveChangesAsync();

        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByIdAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("oldname");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Old Display");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Old Description");
        _mockScopeManager.Setup(m => m.GetResourcesAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create("resource1"));

        var request = new UpdateScopeRequest(
            Name: "newname",
            DisplayName: "New Display",
            Description: null,
            Resources: null,
            ConsentDisplayName: "New Consent",
            ConsentDescription: null,
            IconUrl: null,
            IsRequired: null,
            DisplayOrder: null,
            Category: null
        );

        // Act
        var result = await _scopeService.UpdateScopeAsync("scope1", request);

        // Assert
        Assert.True(result);
        _mockScopeManager.Verify(m => m.UpdateAsync(scope, It.IsAny<CancellationToken>()), Times.Once);
        var extension = await _dbContext.ScopeExtensions.FirstOrDefaultAsync(e => e.ScopeId == "scope1");
        Assert.NotNull(extension);
        Assert.Equal("New Consent", extension.ConsentDisplayName);
    }

    #endregion

    #region DeleteScopeAsync Tests

    [Fact]
    public async Task DeleteScopeAsync_ShouldReturnFalse_WhenScopeDoesNotExist()
    {
        // Arrange
        _mockScopeManager.Setup(m => m.FindByNameAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _scopeService.DeleteScopeAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteScopeAsync_ShouldReturnFalse_WhenScopeIsInUse()
    {
        // Arrange
        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByNameAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        var apps = new List<object> { new { Id = "app1" } };
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(apps));
        _mockApplicationManager.Setup(m => m.GetPermissionsAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImmutableArray.Create($"{OpenIddictConstants.Permissions.Prefixes.Scope}scope1"));

        // Act
        var result = await _scopeService.DeleteScopeAsync("scope1");

        // Assert
        Assert.False(result);
        _mockScopeManager.Verify(m => m.DeleteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteScopeAsync_ShouldDeleteScope_WhenNotInUse()
    {
        // Arrange
        _dbContext.ScopeExtensions.Add(new ScopeExtension { ScopeId = "scope1" });
        await _dbContext.SaveChangesAsync();

        var scope = new { Id = "scope1" };
        _mockScopeManager.Setup(m => m.FindByNameAsync("scope1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _mockScopeManager.Setup(m => m.GetIdAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("scope1");

        var apps = new List<object>();
        _mockApplicationManager.Setup(m => m.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(apps));

        // Act
        var result = await _scopeService.DeleteScopeAsync("scope1");

        // Assert
        Assert.True(result);
        _mockScopeManager.Verify(m => m.DeleteAsync(scope, It.IsAny<CancellationToken>()), Times.Once);
        var extension = await _dbContext.ScopeExtensions.FirstOrDefaultAsync(e => e.ScopeId == "scope1");
        Assert.Null(extension);
    }

    #endregion

    // Helper method to create async enumerable for mocking
    private IAsyncEnumerable<object> CreateAsyncEnumerable(IEnumerable<object> items)
    {
        return new AsyncEnumerable(items);
    }

    private class AsyncEnumerable : IAsyncEnumerable<object>
    {
        private readonly IEnumerable<object> _items;

        public AsyncEnumerable(IEnumerable<object> items)
        {
            _items = items;
        }

        public IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new AsyncEnumerator(_items.GetEnumerator());
        }
    }

    private class AsyncEnumerator : IAsyncEnumerator<object>
    {
        private readonly IEnumerator<object> _enumerator;

        public AsyncEnumerator(IEnumerator<object> enumerator)
        {
            _enumerator = enumerator;
        }

        public object Current => _enumerator.Current;

        public ValueTask DisposeAsync()
        {
            _enumerator.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return new ValueTask<bool>(_enumerator.MoveNext());
        }
    }
}
