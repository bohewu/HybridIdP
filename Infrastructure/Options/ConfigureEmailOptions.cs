using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Core.Application;
using Core.Application.Options;
using Core.Domain.Constants;

namespace Infrastructure.Options;

public class ConfigureEmailOptions : IPostConfigureOptions<EmailOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ConfigureEmailOptions(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void PostConfigure(string? name, EmailOptions options)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        // We use GetValueAsync<string> which is sync-over-async.
        // Since this is PostConfigure, we are in a synchronous context.
        
        var host = settings.GetValueAsync<string>(SettingKeys.Email.SmtpHost).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(host)) options.SmtpHost = host;

        var port = settings.GetValueAsync<string>(SettingKeys.Email.SmtpPort).GetAwaiter().GetResult();
        if (int.TryParse(port, out var p)) options.SmtpPort = p;

        var username = settings.GetValueAsync<string>(SettingKeys.Email.SmtpUsername).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(username)) options.SmtpUsername = username;

        var password = settings.GetValueAsync<string>(SettingKeys.Email.SmtpPassword).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(password)) options.SmtpPassword = password;

        var enableSsl = settings.GetValueAsync<string>(SettingKeys.Email.SmtpEnableSsl).GetAwaiter().GetResult();
        if (bool.TryParse(enableSsl, out var ssl)) options.SmtpEnableSsl = ssl;

        var fromAddress = settings.GetValueAsync<string>(SettingKeys.Email.FromAddress).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(fromAddress)) options.FromAddress = fromAddress;

        var fromName = settings.GetValueAsync<string>(SettingKeys.Email.FromName).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(fromName)) options.FromName = fromName;
    }
}
