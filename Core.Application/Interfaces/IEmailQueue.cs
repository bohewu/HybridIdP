using System.Threading;
using System.Threading.Tasks;
using Core.Domain.Models;

namespace Core.Application.Interfaces;

public interface IEmailQueue
{
    Task QueueEmailAsync(EmailMessage message);
    ValueTask<EmailMessage> DequeueAsync(CancellationToken ct);
}
