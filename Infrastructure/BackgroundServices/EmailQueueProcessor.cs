using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Core.Application.Interfaces;

namespace Infrastructure.BackgroundServices;

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
            while (!stoppingToken.IsCancellationRequested)
            {
                var message = await _emailQueue.DequeueAsync(stoppingToken);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IEmailDispatcher>();
                    
                    await dispatcher.SendAsync(message, stoppingToken);
                    
                    _logger.LogInformation("Email sent successfully to {To}", message.To);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing email to {To}", message.To);
                    // In a real system, we'd add retry logic or Dead Letter Queue here
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Email Queue Processor crashed.");
        }

        _logger.LogInformation("Email Queue Processor stopping.");
    }
}
