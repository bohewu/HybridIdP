using Core.Application;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using System.Linq.Expressions;

namespace Tests.Application.UnitTests;

public class SettingsServiceTests
{
    private readonly Mock<IApplicationDbContext> _mockDbContext;
    private readonly IMemoryCache _memoryCache;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _mockDbContext = new Mock<IApplicationDbContext>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _service = new SettingsService(_mockDbContext.Object, _memoryCache);
    }

    #region GetValueAsync Tests

    [Fact]
    public async Task GetValueAsync_WhenSettingExists_ReturnsValue()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting
            {
                Key = SettingKeys.Branding.AppName,
                Value = "TestApp",
                DataType = SettingDataType.String,
                UpdatedUtc = DateTime.UtcNow
            }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetValueAsync(SettingKeys.Branding.AppName);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestApp", result);
    }

    [Fact]
    public async Task GetValueAsync_WhenSettingNotFound_ReturnsNull()
    {
        // Arrange
        var settings = new List<Setting>();
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetValueAsync("nonexistent.key");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetValueAsync_UsesCacheOnSecondCall()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting
            {
                Key = SettingKeys.Branding.AppName,
                Value = "CachedApp",
                DataType = SettingDataType.String,
                UpdatedUtc = DateTime.UtcNow
            }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act - First call (should query DB)
        var firstResult = await _service.GetValueAsync(SettingKeys.Branding.AppName);
        
        // Clear the mock to verify cache is used
        _mockDbContext.Invocations.Clear();
        
        // Act - Second call (should use cache)
        var secondResult = await _service.GetValueAsync(SettingKeys.Branding.AppName);

        // Assert
        Assert.Equal(firstResult, secondResult);
        // Verify DB was not queried on second call
        _mockDbContext.Verify(x => x.Settings, Times.Never);
    }

    #endregion

    #region GetValueAsync<T> Typed Tests

    [Fact]
    public async Task GetValueAsync_Int_ConvertsCorrectly()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting
            {
                Key = SettingKeys.Security.PasswordMinLength,
                Value = "8",
                DataType = SettingDataType.Int,
                UpdatedUtc = DateTime.UtcNow
            }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetValueAsync<int>(SettingKeys.Security.PasswordMinLength);

        // Assert
        Assert.Equal(8, result);
    }

    [Fact]
    public async Task GetValueAsync_Bool_ConvertsCorrectly()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting
            {
                Key = "test.boolSetting",
                Value = "true",
                DataType = SettingDataType.Bool,
                UpdatedUtc = DateTime.UtcNow
            }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetValueAsync<bool>("test.boolSetting");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetValueAsync_String_ReturnsDirectly()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting
            {
                Key = SettingKeys.Branding.ProductName,
                Value = "TestProduct",
                DataType = SettingDataType.String,
                UpdatedUtc = DateTime.UtcNow
            }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetValueAsync<string>(SettingKeys.Branding.ProductName);

        // Assert
        Assert.Equal("TestProduct", result);
    }

    [Fact]
    public async Task GetValueAsync_Json_DeserializesCorrectly()
    {
        // Arrange
        var testObject = new { Name = "Test", Count = 42 };
        var settings = new List<Setting>
        {
            new Setting
            {
                Key = "test.json",
                Value = System.Text.Json.JsonSerializer.Serialize(testObject),
                DataType = SettingDataType.Json,
                UpdatedUtc = DateTime.UtcNow
            }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetValueAsync<dynamic>("test.json");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetValueAsync_WhenNotFound_ReturnsDefault()
    {
        // Arrange
        var settings = new List<Setting>();
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var intResult = await _service.GetValueAsync<int>("nonexistent.int");
        var boolResult = await _service.GetValueAsync<bool>("nonexistent.bool");
        var stringResult = await _service.GetValueAsync<string>("nonexistent.string");

        // Assert
        Assert.Equal(0, intResult);
        Assert.False(boolResult);
        Assert.Null(stringResult);
    }

    #endregion

    #region SetValueAsync Tests

    [Fact]
    public async Task SetValueAsync_WhenNewSetting_CreatesNew()
    {
        // Arrange
        var settings = new List<Setting>();
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);
        _mockDbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var newKey = "test.newkey";
        var newValue = "newvalue";

        // Act
        await _service.SetValueAsync(newKey, newValue, "TestUser");

        // Assert
        _mockDbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetValueAsync_WhenExistingSetting_UpdatesValue()
    {
        // Arrange
        var existingSetting = new Setting
        {
            Id = Guid.NewGuid(),
            Key = SettingKeys.Branding.AppName,
            Value = "OldValue",
            DataType = SettingDataType.String,
            UpdatedUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedBy = "OldUser"
        };
        var settings = new List<Setting> { existingSetting };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);
        _mockDbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.SetValueAsync(SettingKeys.Branding.AppName, "NewValue", "NewUser");

        // Assert
        Assert.Equal("NewValue", existingSetting.Value);
        Assert.Equal("NewUser", existingSetting.UpdatedBy);
        Assert.True(existingSetting.UpdatedUtc > DateTime.UtcNow.AddSeconds(-5));
        _mockDbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetByPrefixAsync Tests

    [Fact]
    public async Task GetByPrefixAsync_ReturnsMatchingSettings()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting { Key = "branding.appName", Value = "App", DataType = SettingDataType.String, UpdatedUtc = DateTime.UtcNow },
            new Setting { Key = "branding.productName", Value = "Product", DataType = SettingDataType.String, UpdatedUtc = DateTime.UtcNow },
            new Setting { Key = "security.minLength", Value = "6", DataType = SettingDataType.Int, UpdatedUtc = DateTime.UtcNow }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetByPrefixAsync("branding.");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.StartsWith("branding.", s.Key));
    }

    [Fact]
    public async Task GetByPrefixAsync_WithoutPrefix_ReturnsAllSettings()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting { Key = "key1", Value = "value1", DataType = SettingDataType.String, UpdatedUtc = DateTime.UtcNow },
            new Setting { Key = "key2", Value = "value2", DataType = SettingDataType.String, UpdatedUtc = DateTime.UtcNow }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Act
        var result = await _service.GetByPrefixAsync("");

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region InvalidateAsync Tests

    [Fact]
    public async Task InvalidateAsync_RemovesFromCache()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting
            {
                Key = SettingKeys.Branding.AppName,
                Value = "CachedValue",
                DataType = SettingDataType.String,
                UpdatedUtc = DateTime.UtcNow
            }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Prime the cache
        await _service.GetValueAsync(SettingKeys.Branding.AppName);

        // Act - Invalidate cache
        await _service.InvalidateAsync(SettingKeys.Branding.AppName);

        // Clear mock invocations to track new DB calls
        _mockDbContext.Invocations.Clear();

        // Act - Try to get value again (should query DB, not cache)
        var result = await _service.GetValueAsync(SettingKeys.Branding.AppName);

        // Assert
        Assert.Equal("CachedValue", result);
        // Verify DB was queried (cache was invalidated)
        _mockDbContext.Verify(x => x.Settings, Times.Once);
    }

    [Fact]
    public async Task InvalidateAsync_WithPrefix_RemovesMatchingKeysFromCache()
    {
        // Arrange
        var settings = new List<Setting>
        {
            new Setting { Key = "branding.key1", Value = "value1", DataType = SettingDataType.String, UpdatedUtc = DateTime.UtcNow },
            new Setting { Key = "branding.key2", Value = "value2", DataType = SettingDataType.String, UpdatedUtc = DateTime.UtcNow }
        };
        var mockSet = CreateMockDbSet(settings);
        _mockDbContext.Setup(x => x.Settings).Returns(mockSet.Object);

        // Prime the cache
        await _service.GetValueAsync("branding.key1");
        await _service.GetValueAsync("branding.key2");

        // Act - Invalidate all keys with prefix
        await _service.InvalidateAsync("branding.");

        // Clear mock invocations
        _mockDbContext.Invocations.Clear();

        // Act - Try to get both values (should query DB)
        await _service.GetValueAsync("branding.key1");
        await _service.GetValueAsync("branding.key2");

        // Assert - DB was queried for both (cache was cleared)
        _mockDbContext.Verify(x => x.Settings, Times.Exactly(2));
    }

    #endregion

    #region Helper Methods

    private Mock<DbSet<Setting>> CreateMockDbSet(List<Setting> data)
    {
        var queryable = data.AsQueryable();
        var mockSet = new Mock<DbSet<Setting>>();

        mockSet.As<IQueryable<Setting>>().Setup(m => m.Provider).Returns(new TestAsyncQueryProvider<Setting>(queryable.Provider));
        mockSet.As<IQueryable<Setting>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockSet.As<IQueryable<Setting>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockSet.As<IQueryable<Setting>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());
        mockSet.As<IAsyncEnumerable<Setting>>().Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<Setting>(queryable.GetEnumerator()));

        return mockSet;
    }

    #endregion
}

#region Test Helpers for Async Queryable

// Helper classes for async LINQ testing
internal class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;

    internal TestAsyncQueryProvider(IQueryProvider inner)
    {
        _inner = inner;
    }

    public IQueryable CreateQuery(Expression expression)
    {
        return new TestAsyncEnumerable<TEntity>(expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new TestAsyncEnumerable<TElement>(expression);
    }

    public object? Execute(Expression expression)
    {
        return _inner.Execute(expression);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        return _inner.Execute<TResult>(expression);
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        return Execute<TResult>(expression);
    }
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable)
        : base(enumerable)
    { }

    public TestAsyncEnumerable(Expression expression)
        : base(expression)
    { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    IQueryProvider IQueryable.Provider
    {
        get { return new TestAsyncQueryProvider<T>(this); }
    }
}

internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    public TestAsyncEnumerator(IEnumerator<T> inner)
    {
        _inner = inner;
    }

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MoveNextAsync()
    {
        return ValueTask.FromResult(_inner.MoveNext());
    }

    public T Current
    {
        get { return _inner.Current; }
    }
}

#endregion
