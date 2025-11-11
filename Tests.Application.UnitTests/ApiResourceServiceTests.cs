using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class ApiResourceServiceTests : IDisposable
{
    private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
    private readonly Mock<ILogger<ApiResourceService>> _mockLogger;
    private readonly ApplicationDbContext _dbContext;
    private readonly ApiResourceService _apiResourceService;

    public ApiResourceServiceTests()
    {
        _mockScopeManager = new Mock<IOpenIddictScopeManager>();
        _mockLogger = new Mock<ILogger<ApiResourceService>>();
        
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _dbContext = new ApplicationDbContext(options);
        
        _apiResourceService = new ApiResourceService(_dbContext, _mockScopeManager.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    #region GetResourcesAsync Tests

    [Fact]
    public async Task GetResourcesAsync_ShouldReturnAllResources_WhenNoFiltersApplied()
    {
        // Arrange
        var resource1 = new ApiResource
        {
            Name = "company_api",
            DisplayName = "Company API",
            Description = "Company management API",
            BaseUrl = "https://api.company.com",
            CreatedAt = DateTime.UtcNow
        };
        var resource2 = new ApiResource
        {
            Name = "inventory_api",
            DisplayName = "Inventory API",
            Description = "Inventory management API",
            BaseUrl = "https://api.inventory.com",
            CreatedAt = DateTime.UtcNow
        };
        
        _dbContext.ApiResources.AddRange(resource1, resource2);
        await _dbContext.SaveChangesAsync(default);

        // Act
        var (items, totalCount) = await _apiResourceService.GetResourcesAsync(0, 25, null, null);

        // Assert
        Assert.Equal(2, totalCount);
        Assert.Equal(2, items.Count());
        Assert.Contains(items, r => r.Name == "company_api");
        Assert.Contains(items, r => r.Name == "inventory_api");
    }

    [Fact]
    public async Task GetResourcesAsync_ShouldFilterBySearch_WhenSearchProvided()
    {
        // Arrange
        var resource1 = new ApiResource
        {
            Name = "company_api",
            DisplayName = "Company API",
            Description = "Company management API",
            CreatedAt = DateTime.UtcNow
        };
        var resource2 = new ApiResource
        {
            Name = "inventory_api",
            DisplayName = "Inventory API",
            Description = "Inventory management API",
            CreatedAt = DateTime.UtcNow
        };
        
        _dbContext.ApiResources.AddRange(resource1, resource2);
        await _dbContext.SaveChangesAsync(default);

        // Act
        var (items, totalCount) = await _apiResourceService.GetResourcesAsync(0, 25, "company", null);

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Single(items);
        Assert.Equal("company_api", items.First().Name);
    }

    [Fact]
    public async Task GetResourcesAsync_ShouldSortByName_WhenSortProvided()
    {
        // Arrange
        var resource1 = new ApiResource
        {
            Name = "zebra_api",
            DisplayName = "Zebra API",
            CreatedAt = DateTime.UtcNow
        };
        var resource2 = new ApiResource
        {
            Name = "alpha_api",
            DisplayName = "Alpha API",
            CreatedAt = DateTime.UtcNow
        };
        
        _dbContext.ApiResources.AddRange(resource1, resource2);
        await _dbContext.SaveChangesAsync(default);

        // Act
        var (items, totalCount) = await _apiResourceService.GetResourcesAsync(0, 25, null, "name");

        // Assert
        Assert.Equal(2, totalCount);
        Assert.Equal("alpha_api", items.First().Name);
        Assert.Equal("zebra_api", items.Last().Name);
    }

    [Fact]
    public async Task GetResourcesAsync_ShouldRespectPagination()
    {
        // Arrange
        for (int i = 1; i <= 5; i++)
        {
            _dbContext.ApiResources.Add(new ApiResource
            {
                Name = $"api_{i}",
                DisplayName = $"API {i}",
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync(default);

        // Act
        var (items, totalCount) = await _apiResourceService.GetResourcesAsync(2, 2, null, null);

        // Assert
        Assert.Equal(5, totalCount);
        Assert.Equal(2, items.Count());
    }

    #endregion

    #region GetResourceByIdAsync Tests

    [Fact]
    public async Task GetResourceByIdAsync_ShouldReturnResource_WhenIdExists()
    {
        // Arrange
        var resource = new ApiResource
        {
            Name = "test_api",
            DisplayName = "Test API",
            Description = "Test description",
            BaseUrl = "https://api.test.com",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.Add(resource);
        await _dbContext.SaveChangesAsync(default);

        // Act
        var result = await _apiResourceService.GetResourceByIdAsync(resource.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test_api", result.Name);
        Assert.Equal("Test API", result.DisplayName);
    }

    [Fact]
    public async Task GetResourceByIdAsync_ShouldReturnNull_WhenIdDoesNotExist()
    {
        // Act
        var result = await _apiResourceService.GetResourceByIdAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetResourceByIdAsync_ShouldIncludeScopes_WhenResourceHasScopes()
    {
        // Arrange
        var resource = new ApiResource
        {
            Name = "test_api",
            DisplayName = "Test API",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.Add(resource);
        await _dbContext.SaveChangesAsync(default);

        var scopeId = "scope1";
        _dbContext.ApiResourceScopes.Add(new ApiResourceScope
        {
            ApiResourceId = resource.Id,
            ScopeId = scopeId
        });
        await _dbContext.SaveChangesAsync(default);

        var mockScope = new object();
        _mockScopeManager.Setup(m => m.FindByIdAsync(scopeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockScope);
        _mockScopeManager.Setup(m => m.GetIdAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeId);
        _mockScopeManager.Setup(m => m.GetNameAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test_scope");
        _mockScopeManager.Setup(m => m.GetDisplayNameAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test Scope");
        _mockScopeManager.Setup(m => m.GetDescriptionAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test scope description");

        // Act
        var result = await _apiResourceService.GetResourceByIdAsync(resource.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Scopes);
        Assert.Equal("test_scope", result.Scopes.First().Name);
    }

    #endregion

    #region CreateResourceAsync Tests

    [Fact]
    public async Task CreateResourceAsync_ShouldCreateResource_WhenValidRequest()
    {
        // Arrange
        var request = new CreateApiResourceRequest(
            Name: "new_api",
            DisplayName: "New API",
            Description: "New API description",
            BaseUrl: "https://api.new.com",
            ScopeIds: null
        );

        // Act
        var result = await _apiResourceService.CreateResourceAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("new_api", result.Name);
        Assert.Equal("New API", result.DisplayName);
        
        var created = await _dbContext.ApiResources.FirstOrDefaultAsync(r => r.Name == "new_api");
        Assert.NotNull(created);
    }

    [Fact]
    public async Task CreateResourceAsync_ShouldThrowException_WhenNameAlreadyExists()
    {
        // Arrange
        _dbContext.ApiResources.Add(new ApiResource
        {
            Name = "existing_api",
            DisplayName = "Existing API",
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(default);

        var request = new CreateApiResourceRequest(
            Name: "existing_api",
            DisplayName: "Duplicate API",
            Description: null,
            BaseUrl: null,
            ScopeIds: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _apiResourceService.CreateResourceAsync(request));
    }

    [Fact]
    public async Task CreateResourceAsync_ShouldAssociateScopes_WhenScopeIdsProvided()
    {
        // Arrange
        var scopeId = "scope1";
        var mockScope = new object();
        _mockScopeManager.Setup(m => m.FindByIdAsync(scopeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockScope);

        var request = new CreateApiResourceRequest(
            Name: "api_with_scopes",
            DisplayName: "API with Scopes",
            Description: null,
            BaseUrl: null,
            ScopeIds: new List<string> { scopeId }
        );

        // Act
        var result = await _apiResourceService.CreateResourceAsync(request);

        // Assert
        var resource = await _dbContext.ApiResources
            .Include(r => r.Scopes)
            .FirstAsync(r => r.Id == result.Id);
        
        Assert.Single(resource.Scopes);
        Assert.Equal(scopeId, resource.Scopes.First().ScopeId);
    }

    #endregion

    #region UpdateResourceAsync Tests

    [Fact]
    public async Task UpdateResourceAsync_ShouldUpdateResource_WhenValidRequest()
    {
        // Arrange
        var resource = new ApiResource
        {
            Name = "old_name",
            DisplayName = "Old Display Name",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.Add(resource);
        await _dbContext.SaveChangesAsync(default);

        var request = new UpdateApiResourceRequest(
            Name: "new_name",
            DisplayName: "New Display Name",
            Description: "Updated description",
            BaseUrl: "https://api.updated.com",
            ScopeIds: null
        );

        // Act
        var result = await _apiResourceService.UpdateResourceAsync(resource.Id, request);

        // Assert
        Assert.True(result);
        
        var updated = await _dbContext.ApiResources.FindAsync(resource.Id);
        Assert.Equal("new_name", updated!.Name);
        Assert.Equal("New Display Name", updated.DisplayName);
        Assert.Equal("Updated description", updated.Description);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task UpdateResourceAsync_ShouldReturnFalse_WhenIdDoesNotExist()
    {
        // Arrange
        var request = new UpdateApiResourceRequest(
            Name: "name",
            DisplayName: "Display",
            Description: null,
            BaseUrl: null,
            ScopeIds: null
        );

        // Act
        var result = await _apiResourceService.UpdateResourceAsync(999, request);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateResourceAsync_ShouldThrowException_WhenNameAlreadyExistsForDifferentResource()
    {
        // Arrange
        var resource1 = new ApiResource
        {
            Name = "api1",
            DisplayName = "API 1",
            CreatedAt = DateTime.UtcNow
        };
        var resource2 = new ApiResource
        {
            Name = "api2",
            DisplayName = "API 2",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.AddRange(resource1, resource2);
        await _dbContext.SaveChangesAsync(default);

        var request = new UpdateApiResourceRequest(
            Name: "api1", // Trying to use name of resource1
            DisplayName: "Updated API 2",
            Description: null,
            BaseUrl: null,
            ScopeIds: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _apiResourceService.UpdateResourceAsync(resource2.Id, request));
    }

    [Fact]
    public async Task UpdateResourceAsync_ShouldUpdateScopes_WhenScopeIdsProvided()
    {
        // Arrange
        var resource = new ApiResource
        {
            Name = "test_api",
            DisplayName = "Test API",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.Add(resource);
        await _dbContext.SaveChangesAsync(default);

        // Add initial scope
        _dbContext.ApiResourceScopes.Add(new ApiResourceScope
        {
            ApiResourceId = resource.Id,
            ScopeId = "old_scope"
        });
        await _dbContext.SaveChangesAsync(default);

        var newScopeId = "new_scope";
        var mockScope = new object();
        _mockScopeManager.Setup(m => m.FindByIdAsync(newScopeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockScope);

        var request = new UpdateApiResourceRequest(
            Name: "test_api",
            DisplayName: "Test API",
            Description: null,
            BaseUrl: null,
            ScopeIds: new List<string> { newScopeId }
        );

        // Act
        var result = await _apiResourceService.UpdateResourceAsync(resource.Id, request);

        // Assert
        Assert.True(result);
        
        var updated = await _dbContext.ApiResources
            .Include(r => r.Scopes)
            .FirstAsync(r => r.Id == resource.Id);
        
        Assert.Single(updated.Scopes);
        Assert.Equal(newScopeId, updated.Scopes.First().ScopeId);
    }

    #endregion

    #region DeleteResourceAsync Tests

    [Fact]
    public async Task DeleteResourceAsync_ShouldDeleteResource_WhenIdExists()
    {
        // Arrange
        var resource = new ApiResource
        {
            Name = "to_delete",
            DisplayName = "To Delete",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.Add(resource);
        await _dbContext.SaveChangesAsync(default);

        // Act
        var result = await _apiResourceService.DeleteResourceAsync(resource.Id);

        // Assert
        Assert.True(result);
        
        var deleted = await _dbContext.ApiResources.FindAsync(resource.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteResourceAsync_ShouldReturnFalse_WhenIdDoesNotExist()
    {
        // Act
        var result = await _apiResourceService.DeleteResourceAsync(999);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteResourceAsync_ShouldCascadeDeleteScopes()
    {
        // Arrange
        var resource = new ApiResource
        {
            Name = "api_with_scopes",
            DisplayName = "API with Scopes",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.Add(resource);
        await _dbContext.SaveChangesAsync(default);

        _dbContext.ApiResourceScopes.Add(new ApiResourceScope
        {
            ApiResourceId = resource.Id,
            ScopeId = "scope1"
        });
        await _dbContext.SaveChangesAsync(default);

        // Act
        var result = await _apiResourceService.DeleteResourceAsync(resource.Id);

        // Assert
        Assert.True(result);
        
        var scopes = await _dbContext.ApiResourceScopes
            .Where(s => s.ApiResourceId == resource.Id)
            .ToListAsync();
        Assert.Empty(scopes);
    }

    #endregion

    #region GetResourceScopesAsync Tests

    [Fact]
    public async Task GetResourceScopesAsync_ShouldReturnScopes_WhenResourceHasScopes()
    {
        // Arrange
        var resource = new ApiResource
        {
            Name = "test_api",
            DisplayName = "Test API",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ApiResources.Add(resource);
        await _dbContext.SaveChangesAsync(default);

        var scopeId = "scope1";
        _dbContext.ApiResourceScopes.Add(new ApiResourceScope
        {
            ApiResourceId = resource.Id,
            ScopeId = scopeId
        });
        await _dbContext.SaveChangesAsync(default);

        var mockScope = new object();
        _mockScopeManager.Setup(m => m.FindByIdAsync(scopeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockScope);
        _mockScopeManager.Setup(m => m.GetIdAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopeId);
        _mockScopeManager.Setup(m => m.GetNameAsync(mockScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test_scope");

        // Act
        var result = await _apiResourceService.GetResourceScopesAsync(resource.Id);

        // Assert
        Assert.Single(result);
        Assert.Equal("test_scope", result.First().Name);
    }

    [Fact]
    public async Task GetResourceScopesAsync_ShouldReturnEmpty_WhenResourceDoesNotExist()
    {
        // Act
        var result = await _apiResourceService.GetResourceScopesAsync(999);

        // Assert
        Assert.Empty(result);
    }

    #endregion
}
