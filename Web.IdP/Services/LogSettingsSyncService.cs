using Serilog.Core;
using Serilog.Events;
using Core.Application;

namespace Web.IdP.Services;

public class LogSettingsSyncService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LoggingLevelSwitch _levelSwitch;
    private const string SettingKey = "Logging:Level:Global";

    public LogSettingsSyncService(IServiceProvider serviceProvider, LoggingLevelSwitch levelSwitch)
    {
        _serviceProvider = serviceProvider;
        _levelSwitch = levelSwitch;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        var storedLevel = await settingsService.GetValueAsync<string>(SettingKey, cancellationToken);
        if (!string.IsNullOrEmpty(storedLevel) && Enum.TryParse<LogEventLevel>(storedLevel, true, out var parsedLevel))
        {
            _levelSwitch.MinimumLevel = parsedLevel;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
