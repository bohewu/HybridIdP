using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for Api controllers: LanguageController, ScopeProtectedController
/// Note: MyAccountController and ProfileManagementController require user login (cookie auth)
/// </summary>
public class ApiControllerTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _accessToken;

    public ApiControllerTests(WebIdPServerFixture serverFixture)
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
        _accessToken = await GetAccessTokenAsync();
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }

    // ===== LanguageController Tests =====

    [Fact]
    public async Task Language_SetValidCulture_ReturnsOk()
    {
        // Arrange
        var request = new { culture = "en-US" };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/language/set", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Language_SetInvalidCulture_ReturnsBadRequest()
    {
        // Arrange
        var request = new { culture = "invalid-culture" };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/language/set", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Language_SetEmptyCulture_ReturnsBadRequest()
    {
        // Arrange
        var request = new { culture = "" };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/language/set", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ===== ScopeProtectedController Tests =====

    [Fact]
    public async Task ScopeProtected_Public_ReturnsOkWithoutAuth()
    {
        // Act - no auth required
        var response = await _httpClient.GetAsync("/api/test/scopeprotected/public");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task ScopeProtected_Authenticated_NoAuth_ReturnsUnauthorized()
    {
        // Act - no auth
        var response = await _httpClient.GetAsync("/api/test/scopeprotected/authenticated");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ScopeProtected_OpenId_NoAuth_ReturnsUnauthorized()
    {
        // Act - no auth
        var response = await _httpClient.GetAsync("/api/test/scopeprotected/openid");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== MyAccountController Tests =====

    [Fact]
    public async Task MyAccount_GetAccounts_NoAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/my/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MyAccount_SwitchAccount_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        var request = new { targetAccountId = Guid.NewGuid() };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/my/switch-account", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== ProfileManagementController Tests =====

    [Fact]
    public async Task Profile_GetProfile_NoAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/profile");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_UpdateProfile_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        var request = new { firstName = "Test" };

        // Act
        var response = await _httpClient.PutAsJsonAsync("/api/profile", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_ChangePassword_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        var request = new { currentPassword = "old", newPassword = "new" };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/profile/change-password", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Helper Methods =====

    private async Task<string> GetAccessTokenAsync()
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = "openid"
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("access_token").GetString()!;
    }
}
