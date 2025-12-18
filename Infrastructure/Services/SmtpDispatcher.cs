using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain.Models;
using Core.Domain.Constants;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public partial class SmtpDispatcher : IEmailDispatcher
{
    private readonly ISettingsService _settings;
    private readonly ILogger<SmtpDispatcher> _logger;

    public SmtpDispatcher(ISettingsService settings, ILogger<SmtpDispatcher> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mailSettings = await GetMailSettingsAsync(ct);
        
        if (string.IsNullOrWhiteSpace(mailSettings.Host))
        {
            LogSmtpNotConfigured(_logger, message.To);
            return;
        }

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(mailSettings.FromName, mailSettings.FromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        var builder = new BodyBuilder();
        if (message.IsHtml)
        {
            builder.HtmlBody = message.Body;
        }
        else
        {
            builder.TextBody = message.Body;
        }
        mimeMessage.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try 
        {
            // For Mailpit or Dev, we might accept all certs
            if (mailSettings.Host == "localhost" || !mailSettings.EnableSsl) 
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            }

            await client.ConnectAsync(mailSettings.Host, mailSettings.Port, mailSettings.EnableSsl, ct);
            
            if (!string.IsNullOrEmpty(mailSettings.Username))
            {
                await client.AuthenticateAsync(mailSettings.Username, mailSettings.Password, ct);
            }

            await client.SendAsync(mimeMessage, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            LogEmailSendFailed(_logger, ex, message.To, mailSettings.Host, mailSettings.Port);
            throw; // Job/Queue will handle retry
        }
    }

    private async Task<MailSettingsDto> GetMailSettingsAsync(CancellationToken ct)
    {
        var settings = new MailSettingsDto();
        settings.Host = await _settings.GetValueAsync<string>(SettingKeys.Email.SmtpHost, ct) ?? "";
        var portStr = await _settings.GetValueAsync<string>(SettingKeys.Email.SmtpPort, ct);
        settings.Port = int.TryParse(portStr, out var port) ? port : 587;
        settings.Username = await _settings.GetValueAsync<string>(SettingKeys.Email.SmtpUsername, ct) ?? "";
        settings.Password = await _settings.GetValueAsync<string>(SettingKeys.Email.SmtpPassword, ct) ?? "";
        var sslStr = await _settings.GetValueAsync<string>(SettingKeys.Email.SmtpEnableSsl, ct);
        settings.EnableSsl = bool.TryParse(sslStr, out var ssl) ? ssl : false;
        settings.FromAddress = await _settings.GetValueAsync<string>(SettingKeys.Email.FromAddress, ct) ?? "";
        settings.FromName = await _settings.GetValueAsync<string>(SettingKeys.Email.FromName, ct) ?? "";
        
        return settings;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP Host not configured. Email to {To} dropped.")]
    static partial void LogSmtpNotConfigured(ILogger logger, string to);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email to {To} via {Host}:{Port}")]
    static partial void LogEmailSendFailed(ILogger logger, Exception ex, string to, string host, int port);
}
