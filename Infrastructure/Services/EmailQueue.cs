using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Core.Application.Interfaces;
using Core.Domain.Models;

namespace Infrastructure.Services;

public class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _queue;

    public EmailQueue()
    {
        // Capacity 1000, Wait when full (backpressure)
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true, // We have one background service reading
            SingleWriter = false // Multiple threads can write (Controller actions)
        };
        _queue = Channel.CreateBounded<EmailMessage>(options);
    }

    public async Task QueueEmailAsync(EmailMessage message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        await _queue.Writer.WriteAsync(message);
    }

    public ValueTask<EmailMessage> DequeueAsync(CancellationToken ct)
    {
        return _queue.Reader.ReadAsync(ct);
    }

    /// <summary>
    /// Signal that no more writes will occur. Call during graceful shutdown.
    /// </summary>
    public void Complete()
    {
        _queue.Writer.Complete();
    }

    /// <summary>
    /// Try to read a message without blocking (for draining remaining messages on shutdown).
    /// </summary>
    public bool TryDequeue(out EmailMessage? message)
    {
        return _queue.Reader.TryRead(out message);
    }

    /// <summary>
    /// Get approximate count of pending messages.
    /// </summary>
    public int PendingCount => _queue.Reader.Count;
}

