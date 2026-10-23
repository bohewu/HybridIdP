using System.Threading;
using System.Threading.Tasks;

namespace Core.Application;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = false, CancellationToken ct = default);

    /// <summary>
    /// Waits for SMTP acceptance, not inbox delivery. Implementations must not fall back to queueing.
    /// Unsupported implementations fail closed, including older notification-only adapters.
    /// </summary>
    Task SendEmailWithConfirmationAsync(string to, string subject, string body, bool isHtml = false, CancellationToken ct = default) =>
        Task.FromException(new EmailDeliveryException("smtp_confirmation_unavailable", "SMTP acceptance could not be confirmed."));
    
    /// <summary>
    /// Sends a test email using the provided settings (not saving them).
    /// </summary>
    Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default);
}
