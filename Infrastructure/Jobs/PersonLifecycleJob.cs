using Core.Application;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Infrastructure.Jobs;

/// <summary>
/// Quartz.NET job for processing scheduled Person lifecycle transitions.
/// Phase 18.4: Personnel Lifecycle Automation
/// 
/// This job runs daily and:
/// - Auto-activates Pending persons when their StartDate arrives
/// - Auto-terminates Active persons when their EndDate passes
/// - Revokes tokens for auto-terminated persons
/// </summary>
[DisallowConcurrentExecution]
public partial class PersonLifecycleJob : IJob
{
    private readonly IPersonLifecycleService _lifecycleService;
    private readonly ILogger<PersonLifecycleJob> _logger;

    public PersonLifecycleJob(
        IPersonLifecycleService lifecycleService,
        ILogger<PersonLifecycleJob> logger)
    {
        _lifecycleService = lifecycleService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        LogJobStarted();

        try
        {
            var changedCount = await _lifecycleService.ProcessScheduledTransitionsAsync();
            
            if (changedCount > 0)
            {
                LogJobCompleted(changedCount);
            }
            else
            {
                LogNoChanges();
            }
        }
        catch (Exception ex)
        {
            LogJobFailed(ex);
            throw; // Re-throw to let Quartz handle retry logic
        }
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "PersonLifecycleJob started. Processing scheduled transitions...")]
    partial void LogJobStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "PersonLifecycleJob completed. Processed {ChangedCount} person(s).")]
    partial void LogJobCompleted(int changedCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PersonLifecycleJob completed. No scheduled transitions to process.")]
    partial void LogNoChanges();

    [LoggerMessage(Level = LogLevel.Error, Message = "PersonLifecycleJob failed.")]
    partial void LogJobFailed(Exception ex);

    #endregion
}

/// <summary>
/// Job key constants for PersonLifecycleJob
/// </summary>
public static class PersonLifecycleJobConstants
{
    public const string JobName = "PersonLifecycleJob";
    public const string JobGroup = "LifecycleJobs";
    public const string TriggerName = "PersonLifecycleTrigger";
    
    /// <summary>
    /// Default cron expression: Every day at midnight (00:00)
    /// </summary>
    public const string DefaultCronSchedule = "0 0 0 * * ?";
}
