using System.Threading;
using System.Threading.Tasks;
using Core.Domain.Models;

namespace Core.Application.Interfaces;

public interface IEmailDispatcher
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
