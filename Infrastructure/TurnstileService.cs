using System.Text.Json;
using Core.Application;
using Core.Application.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Infrastructure;

public partial class TurnstileService : ITurnstileService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TurnstileOptions _options;
    private readonly ITurnstileStateService _stateService;
    private readonly ISettingsService _settingsService; // Added
    private readonly ILogger<TurnstileService> _logger;

    public TurnstileService(
        IHttpClientFactory httpClientFactory,
        IOptions<TurnstileOptions> options,
        ITurnstileStateService stateService,
        ISettingsService settingsService, // Added
        ILogger<TurnstileService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _stateService = stateService;
        _settingsService = settingsService; // Added
        _logger = logger;
    }

    public async Task<bool> ValidateTokenAsync(string token, string? remoteIp = null)
    {
        // Check DB setting first, fallback to Options
        var dbEnabled = await _settingsService.GetValueAsync<bool?>(Core.Domain.Constants.SettingKeys.Turnstile.Enabled);
        var enabled = dbEnabled ?? _options.Enabled;

        if (!enabled)
        {
            LogTurnstileDisabled(_logger);
            return true;
        }

        // Circuit Breaker Check
        if (!_stateService.IsAvailable)
        {
            LogTurnstileCircuitBreaker(_logger);
            return true; // Bypass validation
        }

        var secretKey = await _settingsService.GetValueAsync<string?>(Core.Domain.Constants.SettingKeys.Turnstile.SecretKey);
        if (string.IsNullOrEmpty(secretKey))
        {
            secretKey = _options.SecretKey;
        }

        if (string.IsNullOrEmpty(secretKey))
        {
            LogTurnstileSecretKeyMissing(_logger);
            return false;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var requestData = new Dictionary<string, string>
            {
                { "secret", secretKey },
                { "response", token }
            };

            if (!string.IsNullOrEmpty(remoteIp))
            {
                requestData.Add("remoteip", remoteIp);
            }

            var response = await httpClient.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                new FormUrlEncodedContent(requestData));

            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Turnstile API response: {Response}", jsonResponse);
            
            var result = JsonSerializer.Deserialize<TurnstileResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Success == true)
            {
                LogTurnstileSuccess(_logger);
                return true;
            }

            LogTurnstileFailure(_logger, string.Join(", ", result?.ErrorCodes ?? Array.Empty<string>()));
            return false;
        }
        catch (Exception ex)
        {
            LogTurnstileValidationException(_logger, ex);
            return false;
        }
    }

    private class TurnstileResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Turnstile is disabled via configuration. Skipping validation.")]
    static partial void LogTurnstileDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile is temporarily disabled due to connectivity issues (Circuit Breaker). Skipping validation.")]
    static partial void LogTurnstileCircuitBreaker(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile SecretKey is not configured. Validation will fail.")]
    static partial void LogTurnstileSecretKeyMissing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Turnstile validation succeeded.")]
    static partial void LogTurnstileSuccess(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile validation failed. Errors: {Errors}")]
    static partial void LogTurnstileFailure(ILogger logger, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error validating Turnstile token.")]
    static partial void LogTurnstileValidationException(ILogger logger, Exception ex);
}
