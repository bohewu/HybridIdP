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

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mailSettings = _options.Value;
        var settings = new MailSettingsDto
        {
            Host = mailSettings.SmtpHost,
            Port = mailSettings.SmtpPort,
            Username = mailSettings.SmtpUsername,
            Password = mailSettings.SmtpPassword,
            EnableSsl = mailSettings.SmtpEnableSsl,
            FromAddress = mailSettings.FromAddress,
            FromName = mailSettings.FromName
        };

        return SendCoreAsync(message, settings, isTest: false, ct);
    }

    public Task SendTestAsync(
        EmailMessage message,
        MailSettingsDto settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SendCoreAsync(message, settings, isTest: true, ct);
    }

    private async Task SendCoreAsync(
        EmailMessage message,
        MailSettingsDto settings,
        bool isTest,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            if (isTest)
            {
                throw new EmailDeliveryException(
                    "smtp_not_configured",
                    "SMTP host is required.");
            }

            LogSmtpNotConfigured(_logger, message.To);
            return;
        }

        try 
        {
            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
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

            // For Mailpit or Dev, we might accept all certs
            if (settings.Host == "localhost" || !settings.EnableSsl)
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            }

            await client.ConnectAsync(settings.Host, settings.Port, settings.EnableSsl, ct);
            
            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, ct);
            }

            await client.SendAsync(mimeMessage, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (SmtpCommandException ex) when (isTest)
        {
            LogEmailSendFailed(_logger, ex, message.To, settings.Host, settings.Port);
            throw new EmailDeliveryException(
                "smtp_rejected",
                "The SMTP server rejected the test email.",
                (int)ex.StatusCode,
                ex);
        }
        catch (Exception ex) when (isTest && ex is not OperationCanceledException)
        {
            LogEmailSendFailed(_logger, ex, message.To, settings.Host, settings.Port);
            throw new EmailDeliveryException(
                "smtp_delivery_failed",
                "The SMTP test could not be completed.",
                innerException: ex);
        }
        catch (Exception ex)
        {
            LogEmailSendFailed(_logger, ex, message.To, settings.Host, settings.Port);
            throw;
        }
    }

    // Removed GetMailSettingsAsync as we now use IOptionsSnapshot

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP Host not configured. Email to {To} dropped.")]
    static partial void LogSmtpNotConfigured(ILogger logger, string to);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email to {To} via {Host}:{Port}")]
    static partial void LogEmailSendFailed(ILogger logger, Exception ex, string to, string host, int port);
}
