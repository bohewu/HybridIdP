using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundServices;

public sealed class RecoveryNotificationProcessor(IServiceScopeFactory scopes,
    IOptions<RecoveryEmailSelectionOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !options.Value.SelfServiceEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RecoveryNotificationService>().ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { /* Durable leases permit restart. Never log recipient-bearing exceptions. */ }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
