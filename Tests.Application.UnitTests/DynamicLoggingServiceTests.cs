using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Serilog.Core;
using Serilog.Events;
using Web.IdP.Services;
using Core.Application;

namespace Tests.Application.UnitTests;

public class DynamicLoggingServiceTests
{
    private readonly Mock<ISettingsService> _settingsServiceMock;
    private readonly LoggingLevelSwitch _loggingLevelSwitch;
    private readonly DynamicLoggingService _service;
    private const string SettingKey = "Logging:Level:Global";

    public DynamicLoggingServiceTests()
    {
        _settingsServiceMock = new Mock<ISettingsService>();
        _loggingLevelSwitch = new LoggingLevelSwitch();
        _service = new DynamicLoggingService(_settingsServiceMock.Object, _loggingLevelSwitch);
    }

    #region SetGlobalLogLevelAsync

    [Fact]
    public async Task SetGlobalLogLevelAsync_ValidLevel_UpdatesSwitchAndSettings()
    {
        // Arrange
        var level = "Debug";

        // Act
        await _service.SetGlobalLogLevelAsync(level);

        // Assert
        Assert.Equal(LogEventLevel.Debug, _loggingLevelSwitch.MinimumLevel);
        _settingsServiceMock.Verify(s => s.SetValueAsync(SettingKey, level, null), Times.Once);
    }

    [Fact]
    public async Task SetGlobalLogLevelAsync_InvalidLevel_ThrowsArgumentException()
    {
        // Arrange
        var invalidLevel = "InvalidLevel";
        var initialLevel = _loggingLevelSwitch.MinimumLevel;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetGlobalLogLevelAsync(invalidLevel));
        
        // Assert state unchanged
        Assert.Equal(initialLevel, _loggingLevelSwitch.MinimumLevel);
        _settingsServiceMock.Verify(s => s.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region GetGlobalLogLevelAsync

    [Fact]
    public async Task GetGlobalLogLevelAsync_WhenSettingExists_ReturnsStoredValue()
    {
        // Arrange
        _settingsServiceMock.Setup(s => s.GetValueAsync<string>(SettingKey))
            .ReturnsAsync("Warning");

        // Act
        var result = await _service.GetGlobalLogLevelAsync();

        // Assert
        Assert.Equal("Warning", result);
    }

    [Fact]
    public async Task GetGlobalLogLevelAsync_WhenSettingMissing_ReturnsSwitchValue()
    {
        // Arrange
        _settingsServiceMock.Setup(s => s.GetValueAsync<string>(SettingKey))
            .ReturnsAsync((string?)null);
        _loggingLevelSwitch.MinimumLevel = LogEventLevel.Error;

        // Act
        var result = await _service.GetGlobalLogLevelAsync();

        // Assert
        Assert.Equal("Error", result);
    }

    #endregion
}
