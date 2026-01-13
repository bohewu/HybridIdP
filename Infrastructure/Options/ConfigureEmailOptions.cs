using Microsoft.Extensions.Options;
using Core.Application;
using Core.Application.Options;
using Core.Domain.Constants;

namespace Infrastructure.Options;

public class ConfigureEmailOptions : IPostConfigureOptions<EmailOptions>
{
    private readonly ISettingsService _settings;

    public ConfigureEmailOptions(ISettingsService settings)
    {
        _settings = settings;
    }

    public void PostConfigure(string? name, EmailOptions options)
    {
        // We use GetValueAsync<string> which is sync-over-async or just get raw dict.
        // Since this is PostConfigure, we are in a synchronous context.
        // This is a bit tricky with ISettingsService being mostly async.
        // However, we can use Task.Run(...).GetAwaiter().GetResult() for initialization,
        // or better, if the service provides a synchronous way.
        
        // For now, let's fetch individual settings.
        // NOTE: In a real high-load scenario, we might want to pre-fetch these.
        
        var host = _settings.GetValueAsync<string>(SettingKeys.Email.SmtpHost).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(host)) options.SmtpHost = host;

        var port = _settings.GetValueAsync<string>(SettingKeys.Email.SmtpPort).GetAwaiter().GetResult();
        if (int.TryParse(port, out var p)) options.SmtpPort = p;

        var username = _settings.GetValueAsync<string>(SettingKeys.Email.SmtpUsername).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(username)) options.SmtpUsername = username;

        var password = _settings.GetValueAsync<string>(SettingKeys.Email.SmtpPassword).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(password)) options.SmtpPassword = password;

        var enableSsl = _settings.GetValueAsync<string>(SettingKeys.Email.SmtpEnableSsl).GetAwaiter().GetResult();
        if (bool.TryParse(enableSsl, out var ssl)) options.SmtpEnableSsl = ssl;

        var fromAddress = _settings.GetValueAsync<string>(SettingKeys.Email.FromAddress).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(fromAddress)) options.FromAddress = fromAddress;

        var fromName = _settings.GetValueAsync<string>(SettingKeys.Email.FromName).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(fromName)) options.FromName = fromName;
    }
}
