using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.Domain.Constants;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for ApiResourcesController, AuditController, LocalizationController, 
/// MonitoringController, and DashboardController endpoints.
/// </summary>
[Collection(IsolatedClientAdminHostCollection.Name)]
public class AdminApiMiscTests : IAsyncLifetime
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
        await Task.Delay(100);
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

    [Fact]
    public async Task MonitoringHub_Negotiate_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        using var response = await httpClient.PostAsync(
            "/monitoringHub/negotiate?negotiateVersion=1",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MonitoringHub_Negotiate_WithoutMonitoringPermission_ReturnsForbidden()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await GetTokenWithoutMonitoringPermissionAsync());

        // Act
        using var response = await httpClient.PostAsync(
            "/monitoringHub/negotiate?negotiateVersion=1",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MonitoringHub_Negotiate_WithMonitoringPermission_ReturnsNegotiationPayload()
    {
        // Act
        using var response = await _httpClient.PostAsync(
            "/monitoringHub/negotiate?negotiateVersion=1",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("connectionId", out _));
        Assert.True(payload.TryGetProperty("availableTransports", out _));
    }

    [Fact]
    public async Task MonitoringHub_Negotiate_WithAuthorizedCookie_ReturnsNegotiationPayload()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };
        await SignInAsAdminAsync(httpClient);

        // Act
        using var response = await httpClient.PostAsync(
            "/monitoringHub/negotiate?negotiateVersion=1",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ===== SecurityPolicy Tests =====

    [Fact]
    public async Task SecurityPolicy_EnforceMfaWithoutMfaMethod_ReturnsBadRequest()
    {
        // Arrange - Get current policy first
        var getResponse = await _httpClient.GetAsync("/api/admin/security/policies");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var policyJson = await getResponse.Content.ReadAsStringAsync();
        var policy = JsonSerializer.Deserialize<JsonElement>(policyJson, _jsonOptions);

        // Create an invalid policy: MFA enforcement ON but all MFA methods OFF
        var invalidPolicy = new
        {
            minPasswordLength = policy.GetProperty("minPasswordLength").GetInt32(),
            requireUppercase = policy.GetProperty("requireUppercase").GetBoolean(),
            requireLowercase = policy.GetProperty("requireLowercase").GetBoolean(),
            requireDigit = policy.GetProperty("requireDigit").GetBoolean(),
            requireNonAlphanumeric = policy.GetProperty("requireNonAlphanumeric").GetBoolean(),
            minCharacterTypes = policy.GetProperty("minCharacterTypes").GetInt32(),
            passwordHistoryCount = policy.GetProperty("passwordHistoryCount").GetInt32(),
            passwordExpirationDays = policy.GetProperty("passwordExpirationDays").GetInt32(),
            minPasswordAgeDays = policy.GetProperty("minPasswordAgeDays").GetInt32(),
            maxFailedAccessAttempts = policy.GetProperty("maxFailedAccessAttempts").GetInt32(),
            lockoutDurationMinutes = policy.GetProperty("lockoutDurationMinutes").GetInt32(),
            abnormalLoginHistoryCount = policy.GetProperty("abnormalLoginHistoryCount").GetInt32(),
            blockAbnormalLogin = policy.GetProperty("blockAbnormalLogin").GetBoolean(),
            allowSelfPasswordChange = policy.GetProperty("allowSelfPasswordChange").GetBoolean(),
            enablePasskey = false,     // OFF - all MFA methods disabled!
            enableTotpMfa = false,     // OFF
            enableEmailMfa = false,    // OFF
            maxPasskeysPerUser = 5,
            requireMfaForPasskey = false,
            enforceMandatoryMfaEnrollment = true, // ON - conflicts with no MFA methods!
            mfaEnforcementGracePeriodDays = 0 // Set grace period to 0 as per instruction
        };

        // Act
        var response = await _httpClient.PutAsJsonAsync("/api/admin/security/policies", invalidPolicy);

        // Assert - Should fail with 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("MFA", errorContent, StringComparison.OrdinalIgnoreCase);
    }

    // ===== Helper Methods =====

    private async Task<string> GetAdminTokenAsync()
    {
        // Request all needed scopes
        var scopes = new[] { 
            "scopes.read", "scopes.create", "scopes.update", "scopes.delete",
            "apiresources.read", "apiresources.create", "apiresources.update", "apiresources.delete",
            "audit.read",
            "localization.read", "localization.create", "localization.update", "localization.delete",
            "monitoring.read", "settings.read", "settings.update"
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

    private async Task<string> GetTokenWithoutMonitoringPermissionAsync()
    {
        using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-m2m",
            ["client_secret"] = "m2m-test-secret-2024",
            ["scope"] = "api:company:read"
        });

        using var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("access_token").GetString()!;
    }

    private static async Task SignInAsAdminAsync(HttpClient httpClient)
    {
        using var loginPage = await httpClient.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
        var page = await loginPage.Content.ReadAsStringAsync();
        var match = Regex.Match(
            page,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success);

        using var login = await httpClient.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Login"] = AuthConstants.DefaultAdmin.Email,
                ["Input.Password"] = AuthConstants.DefaultAdmin.Password,
                ["__RequestVerificationToken"] = match.Groups[1].Value
            }));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }
}
