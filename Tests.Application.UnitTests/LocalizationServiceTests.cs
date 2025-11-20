using Core.Application;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Tests.Application.UnitTests;

public class LocalizationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly LocalizationService _localizationService;

    public LocalizationServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _localizationService = new LocalizationService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }

    [Fact]
    public async Task GetLocalizedStringAsync_ReturnsValue_WhenExactCultureMatch()
    {
        // Arrange
        var resource = new Resource
        {
            Key = "test.key",
            Culture = "zh-TW",
            Value = "測試值"
        };
        _dbContext.Resources.Add(resource);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _localizationService.GetLocalizedStringAsync("test.key", "zh-TW");

        // Assert
        Assert.Equal("測試值", result);
    }

    [Fact]
    public async Task GetLocalizedStringAsync_FallsBackToDefaultCulture_WhenCultureNotFound()
    {
        // Arrange
        var resource = new Resource
        {
            Key = "test.key",
            Culture = "en-US",
            Value = "Default Value"
        };
        _dbContext.Resources.Add(resource);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _localizationService.GetLocalizedStringAsync("test.key", "fr-FR");

        // Assert
        Assert.Equal("Default Value", result);
    }

    [Fact]
    public async Task GetLocalizedStringAsync_ReturnsNull_WhenNoResourceFound()
    {
        // Act
        var result = await _localizationService.GetLocalizedStringAsync("nonexistent.key", "en-US");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLocalizedStringAsync_PrefersExactCulture_OverFallback()
    {
        // Arrange
        var exactResource = new Resource
        {
            Key = "test.key",
            Culture = "zh-TW",
            Value = "精確值"
        };
        var fallbackResource = new Resource
        {
            Key = "test.key",
            Culture = "en-US",
            Value = "Fallback Value"
        };
        _dbContext.Resources.AddRange(exactResource, fallbackResource);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _localizationService.GetLocalizedStringAsync("test.key", "zh-TW");

        // Assert
        Assert.Equal("精確值", result);
    }

    [Fact]
    public async Task GetLocalizedStringAsync_ReturnsNull_WhenDefaultCultureNotFound()
    {
        // Arrange
        var resource = new Resource
        {
            Key = "test.key",
            Culture = "zh-TW",
            Value = "測試值"
        };
        _dbContext.Resources.Add(resource);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _localizationService.GetLocalizedStringAsync("test.key", "fr-FR");

        // Assert
        Assert.Null(result);
    }
}