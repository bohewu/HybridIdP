using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for ApiResourcesController, AuditController, LocalizationController, 
/// MonitoringController, and DashboardController endpoints.
/// </summary>
public class AdminApiMiscTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;

    public AdminApiMiscTests(WebIdPServerFixture serverFixture)
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

    // ===== Dashboard Tests =====

    [Fact]
    public async Task Dashboard_GetStats_ReturnsStats()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/dashboard/stats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("totalClients", out _));
        Assert.True(result.TryGetProperty("totalScopes", out _));
        Assert.True(result.TryGetProperty("totalUsers", out _));
    }

    [Fact]
    public async Task Dashboard_GetStats_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/dashboard/stats");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== API Resources Tests =====

    [Fact]
    public async Task ApiResources_GetResources_ReturnsListWithTotalCount()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/resources");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("items", out _));
        Assert.True(result.TryGetProperty("totalCount", out _));
    }

    [Fact]
    public async Task ApiResources_GetResource_NonExistentId_ReturnsNotFound()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/resources/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApiResources_GetResources_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/resources");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Audit Tests =====

    [Fact]
    public async Task Audit_GetEvents_ReturnsListWithTotalCount()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/audit/events");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("items", out _));
        Assert.True(result.TryGetProperty("totalCount", out _));
    }

    [Fact]
    public async Task Audit_GetEvents_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/audit/events");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Localization Tests =====

    [Fact]
    public async Task Localization_GetResources_ReturnsListWithTotalCount()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/localization");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("items", out _));
        Assert.True(result.TryGetProperty("totalCount", out _));
    }

    [Fact]
    public async Task Localization_GetResource_NonExistentId_ReturnsNotFound()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/localization/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Localization_GetResources_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/localization");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Monitoring Tests =====

    [Fact]
    public async Task Monitoring_GetActivityStats_ReturnsStats()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/monitoring/stats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Monitoring_GetSecurityMetrics_ReturnsMetrics()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/monitoring/metrics");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Monitoring_GetDashboardActivityStats_ReturnsStats()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/monitoring/dashboard/activity-stats");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Monitoring_GetDashboardSecurityMetrics_ReturnsMetrics()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/monitoring/dashboard/security-metrics");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Monitoring_GetActiveSessions_ReturnsSessions()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/monitoring/dashboard/active-sessions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Monitoring_GetFailedLogins_ReturnsFailedLogins()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/monitoring/dashboard/failed-logins?limit=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Monitoring_GetStats_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/monitoring/stats");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Helper Methods =====

    private async Task<string> GetAdminTokenAsync()
    {
        // Request all needed scopes
        var scopes = new[] { 
            "scopes.read", "scopes.create", "scopes.update", "scopes.delete",
            "audit.read",
            "localization.read", "localization.create", "localization.update", "localization.delete",
            "monitoring.read"
        };
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
