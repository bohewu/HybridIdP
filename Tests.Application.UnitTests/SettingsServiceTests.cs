using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Tests.Application.UnitTests;

public class SettingsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _memoryCache;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Unique DB for each test
            .Options;
        
        _context = new ApplicationDbContext(options);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _service = new SettingsService(_context, _memoryCache);
    }

    private async Task SeedData(IEnumerable<Setting> settings)
    {
        await _context.Settings.AddRangeAsync(settings);
        await _context.SaveChangesAsync();
    }

    #region GetValueAsync Tests

    [Fact]
    public async Task GetValueAsync_WhenSettingExists_ReturnsValue()
    {
        // Arrange
        await SeedData(new[] { new Setting { Key = SettingKeys.Branding.AppName, Value = "TestApp", DataType = SettingDataType.String } });

        // Act
        var result = await _service.GetValueAsync(SettingKeys.Branding.AppName);

        // Assert
        Assert.Equal("TestApp", result);
    }

    [Fact]
    public async Task GetValueAsync_WhenSettingNotFound_ReturnsNull()
    {
        // Arrange & Act
        var result = await _service.GetValueAsync("nonexistent.key");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetValueAsync_UsesCacheOnSecondCall()
    {
        // Arrange
        var key = SettingKeys.Branding.AppName;
        await SeedData(new[] { new Setting { Key = key, Value = "CachedApp", DataType = SettingDataType.String } });

        // Act - First call (should query DB and cache the value)
        var firstResult = await _service.GetValueAsync(key);
        
        // Tamper with DB to ensure cache is used on the second call
        var settingInDb = await _context.Settings.FirstAsync();
        settingInDb.Value = "TamperedValue";
        await _context.SaveChangesAsync();
        
        // Act - Second call (should return the original cached value)
        var secondResult = await _service.GetValueAsync(key);

        // Assert
        Assert.Equal("CachedApp", firstResult);
        Assert.Equal("CachedApp", secondResult); // Should be the original value from cache, not "TamperedValue"
    }

    #endregion

    #region GetValueAsync<T> Typed Tests

    [Fact]
    public async Task GetValueAsync_Int_ConvertsCorrectly()
    {
        // Arrange
        await SeedData(new[] { new Setting { Key = SettingKeys.Security.PasswordMinLength, Value = "8", DataType = SettingDataType.Int } });

        // Act
        var result = await _service.GetValueAsync<int>(SettingKeys.Security.PasswordMinLength);

        // Assert
        Assert.Equal(8, result);
    }

    [Fact]
    public async Task GetValueAsync_Bool_ConvertsCorrectly()
    {
        // Arrange
        await SeedData(new[] { new Setting { Key = "test.boolSetting", Value = "true", DataType = SettingDataType.Bool } });

        // Act
        var result = await _service.GetValueAsync<bool>("test.boolSetting");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetValueAsync_String_ReturnsDirectly()
    {
        // Arrange
        await SeedData(new[] { new Setting { Key = SettingKeys.Branding.ProductName, Value = "TestProduct", DataType = SettingDataType.String } });

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
        await SeedData(new[] { new Setting { Key = "test.json", Value = JsonSerializer.Serialize(testObject), DataType = SettingDataType.Json } });

        // Act
        var result = await _service.GetValueAsync<JsonElement>("test.json");

        // Assert
        Assert.Equal("Test", result.GetProperty("Name").GetString());
        Assert.Equal(42, result.GetProperty("Count").GetInt32());
    }

    [Fact]
    public async Task GetValueAsync_WhenNotFound_ReturnsDefault()
    {
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
        var newKey = "test.newkey";
        var newValue = "newvalue";

        // Act
        await _service.SetValueAsync(newKey, newValue, "TestUser");

        // Assert
        var settingInDb = await _context.Settings.SingleOrDefaultAsync(s => s.Key == newKey);
        Assert.NotNull(settingInDb);
        Assert.Equal(newValue, settingInDb.Value);
        Assert.Equal("TestUser", settingInDb.UpdatedBy);
    }

    [Fact]
    public async Task SetValueAsync_WhenExistingSetting_UpdatesValue()
    {
        // Arrange
        var key = SettingKeys.Branding.AppName;
        await SeedData(new[] { new Setting { Key = key, Value = "OldValue", DataType = SettingDataType.String, UpdatedBy = "OldUser" } });

        // Act
        await _service.SetValueAsync(key, "NewValue", "NewUser");

        // Assert
        var settingInDb = await _context.Settings.SingleOrDefaultAsync(s => s.Key == key);
        Assert.NotNull(settingInDb);
        Assert.Equal("NewValue", settingInDb.Value);
        Assert.Equal("NewUser", settingInDb.UpdatedBy);
    }

    #endregion

    #region GetByPrefixAsync Tests

    [Fact]
    public async Task GetByPrefixAsync_ReturnsMatchingSettings()
    {
        // Arrange
        await SeedData(new[]
        {
            new Setting { Key = "branding.appName", Value = "App" },
            new Setting { Key = "branding.productName", Value = "Product" },
            new Setting { Key = "security.minLength", Value = "6" }
        });

        // Act
        var result = await _service.GetByPrefixAsync("branding.");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("branding.appName"));
        Assert.True(result.ContainsKey("branding.productName"));
    }

    [Fact]
    public async Task GetByPrefixAsync_WithoutPrefix_ReturnsAllSettings()
    {
        // Arrange
        await SeedData(new[]
        {
            new Setting { Key = "key1", Value = "value1" },
            new Setting { Key = "key2", Value = "value2" }
        });

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
        var key = SettingKeys.Branding.AppName;
        await SeedData(new[] { new Setting { Key = key, Value = "CachedValue" } });

        // 1. Prime the cache
        var firstResult = await _service.GetValueAsync(key);
        Assert.Equal("CachedValue", firstResult);

        // 2. Invalidate the cache
        await _service.InvalidateAsync(key);
        
        // 3. Update the value in the database
        var settingInDb = await _context.Settings.FirstAsync(s => s.Key == key);
        settingInDb.Value = "NewValue";
        await _context.SaveChangesAsync();

        // Act: 4. Get the value again
        var secondResult = await _service.GetValueAsync(key);

        // Assert: 5. The new value should be fetched from DB, proving cache was invalidated
        Assert.Equal("NewValue", secondResult);
    }

    [Fact]
    public async Task InvalidateAsync_WithPrefix_RemovesMatchingKeysFromCache()
    {
        // Arrange
        var key1 = "branding.key1";
        var key2 = "branding.key2";
        await SeedData(new[]
        {
            new Setting { Key = key1, Value = "value1" },
            new Setting { Key = key2, Value = "value2" }
        });

        // 1. Prime the cache for both keys
        Assert.Equal("value1", await _service.GetValueAsync(key1));
        Assert.Equal("value2", await _service.GetValueAsync(key2));

        // 2. Invalidate the entire "branding." prefix
        await _service.InvalidateAsync("branding.");
        
        // 3. Update values in the database
        var setting1 = await _context.Settings.FirstAsync(s => s.Key == key1);
        var setting2 = await _context.Settings.FirstAsync(s => s.Key == key2);
        setting1.Value = "newValue1";
        setting2.Value = "newValue2";
        await _context.SaveChangesAsync();

        // Act: 4. Get both values again
        var result1 = await _service.GetValueAsync(key1);
        var result2 = await _service.GetValueAsync(key2);

        // Assert: 5. Both keys should have fetched the new values, proving prefix invalidation worked
        Assert.Equal("newValue1", result1);
        Assert.Equal("newValue2", result2);
    }

    #endregion
    
    public void Dispose()
    {
        _context.Dispose();
        _memoryCache.Dispose();
    }
}
