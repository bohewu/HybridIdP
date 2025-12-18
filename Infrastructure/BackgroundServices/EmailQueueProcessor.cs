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
public partial class EmailQueueProcessor : BackgroundService
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
        LogProcessorStarted(_logger);

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
            LogShutdownSignalReceived(_logger);
        }
        catch (ChannelClosedException)
        {
            // Channel was completed - normal during shutdown
            LogChannelClosed(_logger);
        }
        catch (Exception ex)
        {
            LogProcessorCrashed(_logger, ex);
        }

        // GRACEFUL SHUTDOWN: Drain remaining emails from queue
        await DrainRemainingEmailsAsync();
        
        LogProcessorStopped(_logger);
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
                    LogDrainFailed(_logger, ex, message.To);
                    // In production, consider persisting to DB or Dead Letter Queue here
                }
            }
        }

        if (drainedCount > 0 || failedCount > 0)
        {
            LogShutdownComplete(_logger, drainedCount, failedCount);
        }
    }

    private async Task ProcessEmailAsync(Core.Domain.Models.EmailMessage message, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IEmailDispatcher>();
            
            await dispatcher.SendAsync(message, ct);
            
            LogEmailSent(_logger, message.To);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingError(_logger, ex, message.To);
            // In a real system, we'd add retry logic or Dead Letter Queue here
            throw; // Re-throw to let drain logic handle it
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email Queue Processor started.")]
    static partial void LogProcessorStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email Queue Processor received shutdown signal. Draining remaining emails...")]
    static partial void LogShutdownSignalReceived(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email queue channel closed.")]
    static partial void LogChannelClosed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Email Queue Processor crashed unexpectedly.")]
    static partial void LogProcessorCrashed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email Queue Processor stopped.")]
    static partial void LogProcessorStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email to {To} during shutdown drain")]
    static partial void LogDrainFailed(ILogger logger, Exception ex, string to);

    [LoggerMessage(Level = LogLevel.Information, Message = "Graceful shutdown complete. Drained {Drained} emails, {Failed} failed.")]
    static partial void LogShutdownComplete(ILogger logger, int drained, int failed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email sent successfully to {To}")]
    static partial void LogEmailSent(ILogger logger, string to);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing email to {To}")]
    static partial void LogProcessingError(ILogger logger, Exception ex, string to);
}

