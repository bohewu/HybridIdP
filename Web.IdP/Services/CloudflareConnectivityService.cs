using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Core.Application;

namespace Web.IdP.Services;

public class CloudflareConnectivityService : BackgroundService
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
        _logger.LogInformation("Cloudflare Connectivity Check Background Service is starting.");

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
        
        _logger.LogInformation("Cloudflare Connectivity Check Background Service is stopping.");
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
                    _logger.LogInformation("Cloudflare connectivity restored. Re-enabling Turnstile.");
                    _stateService.SetAvailable(true);
                }
            }
            else
            {
                if (_stateService.IsAvailable)
                {
                    _logger.LogWarning("Cloudflare connectivity check failed (Status: {StatusCode}). Disabling Turnstile.", response.StatusCode);
                    _stateService.SetAvailable(false);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
        {
            if (_stateService.IsAvailable)
            {
                _logger.LogWarning(ex, "Cloudflare connectivity check failed (Exception). Disabling Turnstile.");
                _stateService.SetAvailable(false);
            }
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Unexpected error during Cloudflare connectivity check.");
             // Decide whether to disable or keep previous state. Failing safe (disabling) is usually safer for UX.
             if (_stateService.IsAvailable)
             {
                 _stateService.SetAvailable(false);
             }
        }
    }
}
