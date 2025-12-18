using System.Threading;
using System.Threading.Tasks;
using Core.Domain.Models;

namespace Core.Application.Interfaces;

public interface IEmailQueue
{
    /// <summary>
    /// Queue an email for async sending.
    /// </summary>
    Task QueueEmailAsync(EmailMessage message);
    
    /// <summary>
    /// Dequeue next email (blocking until available or cancelled).
    /// </summary>
    ValueTask<EmailMessage> DequeueAsync(CancellationToken ct);
    
    /// <summary>
    /// Signal that no more writes will occur (for graceful shutdown).
    /// </summary>
    void Complete();
    
    /// <summary>
    /// Try to read a message without blocking (for draining on shutdown).
    /// </summary>
    bool TryDequeue(out EmailMessage? message);
    
    /// <summary>
    /// Get approximate count of pending messages.
    /// </summary>
    int PendingCount { get; }
}

