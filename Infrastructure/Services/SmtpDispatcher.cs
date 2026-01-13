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
using Microsoft.Extensions.Options;
using Core.Application.Options;

namespace Infrastructure.Services;

public partial class SmtpDispatcher : IEmailDispatcher
{
    private readonly IOptionsSnapshot<EmailOptions> _options;
    private readonly ILogger<SmtpDispatcher> _logger;

    public SmtpDispatcher(IOptionsSnapshot<EmailOptions> options, ILogger<SmtpDispatcher> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mailSettings = _options.Value;
        
        if (string.IsNullOrWhiteSpace(mailSettings.SmtpHost))
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
            if (mailSettings.SmtpHost == "localhost" || !mailSettings.SmtpEnableSsl) 
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            }

            await client.ConnectAsync(mailSettings.SmtpHost, mailSettings.SmtpPort, mailSettings.SmtpEnableSsl, ct);
            
            if (!string.IsNullOrEmpty(mailSettings.SmtpUsername))
            {
                await client.AuthenticateAsync(mailSettings.SmtpUsername, mailSettings.SmtpPassword, ct);
            }

            await client.SendAsync(mimeMessage, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            LogEmailSendFailed(_logger, ex, message.To, mailSettings.SmtpHost, mailSettings.SmtpPort);
            throw; // Job/Queue will handle retry
        }
    }

    // Removed GetMailSettingsAsync as we now use IOptionsSnapshot

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP Host not configured. Email to {To} dropped.")]
    static partial void LogSmtpNotConfigured(ILogger logger, string to);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email to {To} via {Host}:{Port}")]
    static partial void LogEmailSendFailed(ILogger logger, Exception ex, string to, string host, int port);
}
