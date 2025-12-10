using System.Net;
using Core.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Web.IdP.Services;

public partial class CloudflareConnectivityService : BackgroundService
{
    private readonly ITurnstileStateService _stateService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CloudflareConnectivityService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(60);
    private const string TargetUrl = "https://challenges.cloudflare.com/cdn-cgi/trace";

    public CloudflareConnectivityService(
        ITurnstileStateService stateService,
        HttpClient httpClient,
        ILogger<CloudflareConnectivityService> logger)
    {
        _stateService = stateService;
        _httpClient = httpClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarting();

        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckConnectivityAsync(stoppingToken);

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        
        LogServiceStopping();
    }

    public async Task CheckConnectivityAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Use HttpCompletionOption.ResponseHeadersRead for efficiency
            var response = await _httpClient.GetAsync(TargetUrl, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
            
            if (response.IsSuccessStatusCode)
            {
                if (!_stateService.IsAvailable)
                {
                    LogConnectivityRestored();
                    _stateService.SetAvailable(true);
                }
            }
            else
            {
                if (_stateService.IsAvailable)
                {
                    LogConnectivityCheckFailed(response.StatusCode);
                    _stateService.SetAvailable(false);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
        {
            if (_stateService.IsAvailable)
            {
                LogConnectivityCheckException(ex);
                _stateService.SetAvailable(false);
            }
        }
        catch (Exception ex)
        {
             LogUnexpectedError(ex);
             // Decide whether to disable or keep previous state. Failing safe (disabling) is usually safer for UX.
             if (_stateService.IsAvailable)
             {
                 _stateService.SetAvailable(false);
             }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cloudflare Connectivity Check Background Service is starting.")]
    private partial void LogServiceStarting();

    [LoggerMessage(Level = LogLevel.Information, Message = "Cloudflare Connectivity Check Background Service is stopping.")]
    private partial void LogServiceStopping();

    [LoggerMessage(Level = LogLevel.Information, Message = "Cloudflare connectivity restored. Re-enabling Turnstile.")]
    private partial void LogConnectivityRestored();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cloudflare connectivity check failed (Status: {StatusCode}). Disabling Turnstile.")]
    private partial void LogConnectivityCheckFailed(HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cloudflare connectivity check failed (Exception). Disabling Turnstile.")]
    private partial void LogConnectivityCheckException(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error during Cloudflare connectivity check.")]
    private partial void LogUnexpectedError(Exception ex);
}
