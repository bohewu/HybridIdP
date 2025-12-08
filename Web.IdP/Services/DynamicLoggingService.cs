using Core.Application;
using Serilog.Core;
using Serilog.Events;

namespace Web.IdP.Services;

public class DynamicLoggingService : IDynamicLoggingService
{
    private readonly ISettingsService _settingsService;
    private readonly LoggingLevelSwitch _levelSwitch;
    private const string SettingKey = "Logging:Level:Global";

    public DynamicLoggingService(ISettingsService settingsService, LoggingLevelSwitch levelSwitch)
    {
        _settingsService = settingsService;
        _levelSwitch = levelSwitch;
    }

    public async Task SetGlobalLogLevelAsync(string level)
    {
        if (Enum.TryParse<LogEventLevel>(level, true, out var parsedLevel))
        {
            _levelSwitch.MinimumLevel = parsedLevel;
            await _settingsService.SetValueAsync(SettingKey, level);
        }
        else
        {
            throw new ArgumentException($"Invalid log level: {level}");
        }
    }

    public async Task<string> GetGlobalLogLevelAsync()
    {
        // Source of truth is the DB, but fallback to current switch
        var stored = await _settingsService.GetValueAsync<string>(SettingKey);
        return stored ?? _levelSwitch.MinimumLevel.ToString();
    }
}
