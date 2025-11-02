using System.Text.Json;
using Core.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Infrastructure;

public class TurnstileService : ITurnstileService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TurnstileService> _logger;

    public TurnstileService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TurnstileService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> ValidateTokenAsync(string token, string? remoteIp = null)
    {
        var enabled = _configuration.GetValue<bool>("Turnstile:Enabled");
        if (!enabled)
        {
            _logger.LogInformation("Turnstile is disabled. Skipping validation.");
            return true; // Pass validation when disabled
        }

        var secretKey = _configuration["Turnstile:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
        {
            _logger.LogWarning("Turnstile SecretKey is not configured. Validation will fail.");
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
            var result = JsonSerializer.Deserialize<TurnstileResponse>(jsonResponse);

            if (result?.Success == true)
            {
                _logger.LogInformation("Turnstile validation succeeded.");
                return true;
            }

            _logger.LogWarning("Turnstile validation failed. Errors: {Errors}",
                string.Join(", ", result?.ErrorCodes ?? Array.Empty<string>()));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Turnstile token.");
            return false;
        }
    }

    private class TurnstileResponse
    {
        public bool Success { get; set; }
        public string[]? ErrorCodes { get; set; }
    }
}
