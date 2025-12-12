using Core.Application.DTOs;
using Core.Domain.Entities;
using Infrastructure.Services;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Core.Application;

namespace Tests.Application.UnitTests;

public class LocalizationManagementServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly LocalizationManagementService _service;

    public LocalizationManagementServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new LocalizationManagementService(_dbContext);
    }

    [Fact]
    public async Task CreateResourceAsync_ShouldCreateResource_WhenKeyAndCultureAreUnique()
    {
        // Arrange
        var request = new CreateResourceRequest
        {
            Key = "test.key",
            Culture = "en-US",
            Value = "Test Value",
            Category = "Test"
        };

        // Act
        var result = await _service.CreateResourceAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.Key, result.Key);
        Assert.Equal(request.Culture, result.Culture);
        Assert.Equal(request.Value, result.Value);

        var requestInDb = await _dbContext.Resources.FindAsync(result.Id);
        Assert.NotNull(requestInDb);
    }

    [Fact]
    public async Task CreateResourceAsync_ShouldThrowException_WhenDuplicateKeyAndCulture()
    {
        // Arrange
        var resource = new Resource
        {
            Key = "test.duplicate",
            Culture = "en-US",
            Value = "Original",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        _dbContext.Resources.Add(resource);
        await _dbContext.SaveChangesAsync();

        var request = new CreateResourceRequest
        {
            Key = "test.duplicate",
            Culture = "en-US",
            Value = "New Value"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateResourceAsync(request));
        Assert.Equal($"Resource with key '{request.Key}' and culture '{request.Culture}' already exists.", exception.Message);
    }

    [Fact]
    public async Task GetResourcesAsync_ShouldReturnFilteredResources()
    {
        // Arrange
        _dbContext.Resources.AddRange(
            new Resource { Key = "key1", Culture = "en", Value = "val1", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow },
            new Resource { Key = "key2", Culture = "en", Value = "val2", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow },
            new Resource { Key = "other", Culture = "en", Value = "val3", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var (items, total) = await _service.GetResourcesAsync(0, 10, "key", null);

        // Assert
        Assert.Equal(2, items.Count());
        Assert.Equal(2, total);
        Assert.True(items.All(i => i.Key.StartsWith("key")));
    }

    [Fact]
    public async Task GetResourceByIdAsync_ShouldReturnResource_WhenExists()
    {
        // Arrange
        var resource = new Resource { Key = "find.me", Culture = "en", Value = "Found", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
        _dbContext.Resources.Add(resource);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetResourceByIdAsync(resource.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("find.me", result!.Key);
    }

    [Fact]
    public async Task UpdateResourceAsync_ShouldUpdateValue_WhenExists()
    {
        // Arrange
        var resource = new Resource { Key = "update.me", Culture = "en", Value = "Old", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
        _dbContext.Resources.Add(resource);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateResourceRequest { Value = "New", Category = "NewCat" };

        // Act
        var success = await _service.UpdateResourceAsync(resource.Id, request);

        // Assert
        Assert.True(success);
        var updated = await _dbContext.Resources.FindAsync(resource.Id);
        Assert.Equal("New", updated!.Value);
        Assert.Equal("NewCat", updated.Category);
    }

    [Fact]
    public async Task DeleteResourceAsync_ShouldDelete_WhenExists()
    {
        // Arrange
        var resource = new Resource { Key = "delete.me", Culture = "en", Value = "Bye", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
        _dbContext.Resources.Add(resource);
        await _dbContext.SaveChangesAsync();

        // Act
        var success = await _service.DeleteResourceAsync(resource.Id);

        // Assert
        Assert.True(success);
        var deleted = await _dbContext.Resources.FindAsync(resource.Id);
        Assert.Null(deleted);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
