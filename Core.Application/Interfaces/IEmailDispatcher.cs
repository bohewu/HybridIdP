using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Models;

namespace Core.Application.Interfaces;

public interface IEmailDispatcher
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);

    /// <summary>Completes only after SMTP acceptance; missing configuration must fail.</summary>
    Task SendConfirmedAsync(EmailMessage message, CancellationToken ct = default) =>
        Task.FromException(new EmailDeliveryException("smtp_confirmation_unavailable", "SMTP acceptance could not be confirmed."));

    Task SendTestAsync(
        EmailMessage message,
        MailSettingsDto settings,
        CancellationToken ct = default);
}
