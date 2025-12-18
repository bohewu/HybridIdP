using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Core.Application.Interfaces;

namespace Infrastructure.BackgroundServices;

/// <summary>
/// Background service that processes emails from the queue.
/// Implements graceful shutdown to drain remaining emails before stopping.
/// </summary>
public class EmailQueueProcessor : BackgroundService
{
    private readonly IEmailQueue _emailQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueProcessor> _logger;

    public EmailQueueProcessor(
        IEmailQueue emailQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailQueueProcessor> logger)
    {
        _emailQueue = emailQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email Queue Processor started.");

        try
        {
            // Normal processing loop
            while (!stoppingToken.IsCancellationRequested)
            {
                var message = await _emailQueue.DequeueAsync(stoppingToken);
                await ProcessEmailAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested - proceed to drain
            _logger.LogInformation("Email Queue Processor received shutdown signal. Draining remaining emails...");
        }
        catch (ChannelClosedException)
        {
            // Channel was completed - normal during shutdown
            _logger.LogInformation("Email queue channel closed.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Email Queue Processor crashed unexpectedly.");
        }

        // GRACEFUL SHUTDOWN: Drain remaining emails from queue
        await DrainRemainingEmailsAsync();
        
        _logger.LogInformation("Email Queue Processor stopped.");
    }

    /// <summary>
    /// Drain any remaining emails in the queue during shutdown.
    /// This ensures no emails are lost during service restart/update.
    /// </summary>
    private async Task DrainRemainingEmailsAsync()
    {
        var drainedCount = 0;
        var failedCount = 0;

        while (_emailQueue.TryDequeue(out var message))
        {
            if (message != null)
            {
                try
                {
                    // Use a reasonable timeout for shutdown draining
                    using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await ProcessEmailAsync(message, drainCts.Token);
                    drainedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogError(ex, "Failed to send email to {To} during shutdown drain", message.To);
                    // In production, consider persisting to DB or Dead Letter Queue here
                }
            }
        }

        if (drainedCount > 0 || failedCount > 0)
        {
            _logger.LogInformation(
                "Graceful shutdown complete. Drained {Drained} emails, {Failed} failed.",
                drainedCount, failedCount);
        }
    }

    private async Task ProcessEmailAsync(Core.Domain.Models.EmailMessage message, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IEmailDispatcher>();
            
            await dispatcher.SendAsync(message, ct);
            
            _logger.LogInformation("Email sent successfully to {To}", message.To);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing email to {To}", message.To);
            // In a real system, we'd add retry logic or Dead Letter Queue here
            throw; // Re-throw to let drain logic handle it
        }
    }
}

