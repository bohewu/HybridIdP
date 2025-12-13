using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

public class SettingsCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;

    public SettingsCrudTests(WebIdPServerFixture serverFixture)
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
        await Task.Delay(500);
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
    public async Task GetByPrefix_WithValidPrefix_ReturnsSettings()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/settings?prefix=branding.");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Response should be an array of settings
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task GetByKey_ExistingKey_ReturnsValue()
    {
        // Arrange - set a test value first
        var testKey = $"test.settings.{Guid.NewGuid():N}".Substring(0, 30);
        var setRequest = new { value = "test_value_123" };
        await _httpClient.PutAsJsonAsync($"/api/admin/settings/{testKey}", setRequest);

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/settings/{testKey}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task UpdateSetting_ValidData_ReturnsOk()
    {
        // Arrange
        var testKey = $"test.update.{Guid.NewGuid():N}".Substring(0, 30);
        var request = new { value = "updated_value_456" };

        // Act
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/settings/{testKey}", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task InvalidateCache_ReturnsOk()
    {
        // Arrange
        var request = new { key = (string?)null };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/settings/invalidate", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidateCache_WithKey_ReturnsOk()
    {
        // Arrange
        var request = new { key = "branding." };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/settings/invalidate", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===== Failure Path Tests =====

    [Fact]
    public async Task GetByPrefix_WithoutPrefix_ReturnsBadRequest()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/settings");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetByKey_NonExistentKey_ReturnsNotFound()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/settings/nonexistent.key.12345");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSetting_EmptyValue_ReturnsBadRequest()
    {
        // Arrange
        var request = new { value = "" };

        // Act
        var response = await _httpClient.PutAsJsonAsync("/api/admin/settings/test.key", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetByPrefix_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/settings?prefix=branding.");

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
