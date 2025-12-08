using Core.Application;
using Core.Application.DTOs;
using System.Net.Http.Json;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class LegacyAuthService : ILegacyAuthService
{
    private readonly HttpClient _httpClient;
    private readonly LegacyAuthOptions _options;
    private readonly ILogger<LegacyAuthService> _logger;

    public LegacyAuthService(
        IHttpClientFactory httpClientFactory,
        IOptions<LegacyAuthOptions> options,
        ILogger<LegacyAuthService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LegacyUserDto> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new LegacyUserDto { IsAuthenticated = false };
        }

        try
        {
            var requestUrl = _options.LoginUrl;
            var requestBody = new { username, password };

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("X-Internal-Secret", _options.Secret);
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Legacy auth API returned status code {StatusCode}", response.StatusCode);
                return new LegacyUserDto { IsAuthenticated = false };
            }

            var apiResult = await response.Content.ReadFromJsonAsync<LegacyApiLoginResult>(cancellationToken: cancellationToken);

            if (apiResult == null || !apiResult.Authenticated)
            {
                return new LegacyUserDto { IsAuthenticated = false };
            }

            return new LegacyUserDto
            {
                IsAuthenticated = true,
                ExternalId = apiResult.SsoUuid ?? apiResult.UserId,
                Email = apiResult.Email ?? apiResult.Username,
                FullName = apiResult.Username, // Mapping username to FullName as best effort
                Department = null, 
                Phone = null,
                NationalId = apiResult.NationalId,
                PassportNumber = apiResult.PassportNumber,
                ResidentCertificateNumber = apiResult.ResidentCertificateNumber
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling legacy auth API");
            return new LegacyUserDto { IsAuthenticated = false };
        }
    }

    private class LegacyApiLoginResult
    {
        public bool Authenticated { get; set; }
        public string? UserId { get; set; }
        public string? SsoUuid { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? NationalId { get; set; }
        public string? PassportNumber { get; set; }
        public string? ResidentCertificateNumber { get; set; }
    }
}
