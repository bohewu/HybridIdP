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
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

            var storedLevel = await settingsService.GetValueAsync<string>(SettingKey, cancellationToken);
            if (!string.IsNullOrEmpty(storedLevel) && Enum.TryParse<LogEventLevel>(storedLevel, true, out var parsedLevel))
            {
                _levelSwitch.MinimumLevel = parsedLevel;
            }
        }
        catch
        {
            // Fallback to default level if DB isn't ready or settings are missing
            // We can't easily log here if the logger isn't injected, but we shouldn't crash startup.
            // Console.WriteLine($"[LogSettingsSyncService] Failed to load log settings: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
