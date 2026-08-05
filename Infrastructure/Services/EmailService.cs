using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain.Models;

namespace Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IEmailQueue _emailQueue;
    private readonly IEmailDispatcher _emailDispatcher;

    public EmailService(
        IEmailQueue emailQueue,
        IEmailDispatcher emailDispatcher)
    {
        _emailQueue = emailQueue;
        _emailDispatcher = emailDispatcher;
    }

    public Task SendEmailAsync(string to, string subject, string body, bool isHtml = false, CancellationToken ct = default)
    {
        var message = new EmailMessage(to, subject, body, isHtml);
        return _emailQueue.QueueEmailAsync(message);
    }

    public Task SendTestEmailAsync(
        MailSettingsDto settings,
        string to,
        CancellationToken ct = default)
    {
        var message = new EmailMessage(to, "Test Email from HybridIdP", "This is a test email to verify settings.", false);
        return _emailDispatcher.SendTestAsync(message, settings, ct);
    }
}
