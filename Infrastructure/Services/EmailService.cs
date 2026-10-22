using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.Interfaces;
using Core.Domain.Models;

namespace Infrastructure.Services;

public class EmailService : IEmailService
{
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(15);
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

    public async Task SendEmailWithConfirmationAsync(
        string to, string subject, string body, bool isHtml = false, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConfirmationTimeout);
        try
        {
            await _emailDispatcher.SendConfirmedAsync(new EmailMessage(to, subject, body, isHtml), timeout.Token)
                .WaitAsync(timeout.Token);
        }
        catch (EmailDeliveryException)
        {
            throw;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            // Cancellation or a lost reply cannot establish whether the server accepted DATA.
            // Do not retry or retire the pending code on an uncertain outcome.
            throw new EmailDeliveryException("smtp_confirmation_unavailable",
                "SMTP acceptance could not be confirmed.", innerException: exception);
        }
    }
}
