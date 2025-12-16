using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IEmailQueue _emailQueue;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IEmailQueue emailQueue, ILogger<EmailService> logger)
    {
        _emailQueue = emailQueue;
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string body, bool isHtml = false, CancellationToken ct = default)
    {
        var message = new EmailMessage(to, subject, body, isHtml);
        return _emailQueue.QueueEmailAsync(message);
    }

    public Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default)
    {
        // For "Test Email" feature from Admin UI, we might want to bypass the queue 
        // to give immediate feedback if settings are wrong.
        // However, IEmailService interface doesn't expose Dispatcher directly.
        // For now, let's keep it consistent: Queue it. 
        // If we need synchronous feedback, we should expose ISmtpDispatcher to the Admin API directly.
        var message = new EmailMessage(to, "Test Email from HybridIdP", "This is a test email to verify settings.", false);
        return _emailQueue.QueueEmailAsync(message);
    }
}
