using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for remaining Admin controllers: AdminController, PermissionsController, SecurityPolicyController
/// </summary>
public class AdminMiscEndpointTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;

    public AdminMiscEndpointTests(WebIdPServerFixture serverFixture)
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

    // ===== AdminController Tests =====

    [Fact]
    public async Task Admin_Health_ReturnsHealthy()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("status", out var status));
        Assert.Equal("healthy", status.GetString());
    }

    [Fact]
    public async Task Admin_Health_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/health");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== PermissionsController Tests =====

    [Fact]
    public async Task Permissions_GetCurrent_WithM2MToken_ReturnsUnauthorized()
    {
        // Note: This endpoint requires user claims (NameIdentifier) which M2M tokens don't have
        // It's designed for interactive users, not M2M clients
        
        // Act
        var response = await _httpClient.GetAsync("/api/admin/permissions/current");

        // Assert - M2M tokens don't have user ID claim, so endpoint returns Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Permissions_GetCurrent_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/permissions/current");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== SecurityPolicyController Tests =====

    [Fact]
    public async Task SecurityPolicy_GetPolicy_ReturnsPolicy()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/security/policies");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("minPasswordLength", out _));
        Assert.True(result.TryGetProperty("requireUppercase", out _));
    }

    [Fact]
    public async Task SecurityPolicy_GetPolicy_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/security/policies");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecurityPolicy_UpdatePolicy_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        var request = new { minPasswordLength = 8 };

        // Act
        var response = await httpClient.PutAsJsonAsync("/api/admin/security/policies", request);

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
