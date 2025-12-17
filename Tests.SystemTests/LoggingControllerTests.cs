using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

public class LoggingControllerTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;

    public LoggingControllerTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        await Task.Delay(100);
        _adminToken = await GetAdminTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }

    // ===== Happy Path Tests =====

    [Fact]
    public async Task GetLevel_ReturnsCurrentLogLevel()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/logging/level");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("level", out _));
    }

    [Fact]
    public async Task SetLevel_ValidLevel_ReturnsNoContent()
    {
        // Arrange
        var request = new { level = "Information" };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/logging/level", request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        
        // Verify level was changed
        var getResponse = await _httpClient.GetAsync("/api/admin/logging/level");
        var content = await getResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.Equal("Information", result.GetProperty("level").GetString());
    }

    // ===== Failure Path Tests =====

    [Fact]
    public async Task SetLevel_EmptyLevel_ReturnsBadRequest()
    {
        // Arrange
        var request = new { level = "" };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/logging/level", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetLevel_InvalidLevel_ReturnsBadRequest()
    {
        // Arrange
        var request = new { level = "InvalidLogLevel123" };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/logging/level", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLevel_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };
        // No auth header

        // Act
        var response = await httpClient.GetAsync("/api/admin/logging/level");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Helper Methods =====

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[] { "settings.read", "settings.update" };
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = string.Join(" ", scopes)
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("access_token").GetString()!;
    }
}
