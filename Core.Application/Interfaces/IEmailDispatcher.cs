using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Models;

namespace Core.Application.Interfaces;

public interface IEmailDispatcher
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);

    Task SendTestAsync(
        EmailMessage message,
        MailSettingsDto settings,
        CancellationToken ct = default);
}
