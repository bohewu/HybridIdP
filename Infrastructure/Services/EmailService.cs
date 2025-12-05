using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(ISettingsService settings, ILogger<EmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<MailSettingsDto> GetMailSettingsAsync(CancellationToken ct = default)
    {
        var settings = new MailSettingsDto();
        settings.Host = await _settings.GetValueAsync<string>("Mail.Host", ct) ?? "";
        var portStr = await _settings.GetValueAsync<string>("Mail.Port", ct);
        settings.Port = int.TryParse(portStr, out var port) ? port : 587;
        settings.Username = await _settings.GetValueAsync<string>("Mail.Username", ct) ?? "";
        settings.Password = await _settings.GetValueAsync<string>("Mail.Password", ct) ?? "";
        var sslStr = await _settings.GetValueAsync<string>("Mail.EnableSsl", ct);
        settings.EnableSsl = bool.TryParse(sslStr, out var ssl) ? ssl : true;
        settings.FromAddress = await _settings.GetValueAsync<string>("Mail.FromAddress", ct) ?? "";
        settings.FromName = await _settings.GetValueAsync<string>("Mail.FromName", ct) ?? "";
        
        return settings;
    }

    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false, CancellationToken ct = default)
    {
        var settings = await GetMailSettingsAsync(ct);
        await SendEmailInternalAsync(settings, to, subject, body, isHtml, ct);
    }

    public async Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default)
    {
        await SendEmailInternalAsync(settings, to, "Test Email from HybridIdP", "This is a test email to verify your settings.", false, ct);
    }

    private async Task SendEmailInternalAsync(MailSettingsDto settings, string to, string subject, string body, bool isHtml, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
             throw new InvalidOperationException("Mail host is not configured.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var builder = new BodyBuilder();
        if (isHtml)
        {
            builder.HtmlBody = body;
        }
        else
        {
            builder.TextBody = body;
        }
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try 
        {
            await client.ConnectAsync(settings.Host, settings.Port, settings.EnableSsl, ct);
            
            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            throw;
        }
    }
}
