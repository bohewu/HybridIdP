using System.Threading;
using System.Threading.Tasks;

namespace Core.Application;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = false, CancellationToken ct = default);
    
    /// <summary>
    /// Sends a test email using the provided settings (not saving them).
    /// </summary>
    Task SendTestEmailAsync(MailSettingsDto settings, string to, CancellationToken ct = default);
}
