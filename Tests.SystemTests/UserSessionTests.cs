using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Application.DTOs;
using Xunit;

namespace Tests.SystemTests;

public class UserSessionTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly List<string> _createdUserIds = new();
    private string? _adminToken;

    private const string TEST_PREFIX = "test_sess_";
    private readonly string _testPassword = "TestPass123!";

    public UserSessionTests(WebIdPServerFixture serverFixture)
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
        await Task.Delay(1000);
        
        _adminToken = await GetAdminTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
    }

    public async Task DisposeAsync()
    {
        foreach (var userId in _createdUserIds)
        {
            await _httpClient.DeleteAsync($"/api/admin/users/{userId}");
        }
        _httpClient.Dispose();
    }

    [Fact]
    public async Task ListSessions_ValidUser_ReturnsOk()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/users/{user.Id}/sessions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Should contain items array (empty)
        Assert.Contains("items", content); 
        Assert.Contains("total", content);
    }

    [Fact]
    public async Task RevokeAllSessions_ValidUser_ReturnsOk()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var response = await _httpClient.PostAsync($"/api/admin/users/{user.Id}/sessions/revoke-all", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("revoked", content);
    }

    [Fact]
    public async Task RevokeSession_InvalidAuthId_ReturnsNotFound()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var fakeAuthId = Guid.NewGuid().ToString();

        // Act
        var response = await _httpClient.PostAsync($"/api/admin/users/{user.Id}/sessions/{fakeAuthId}/revoke", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLoginHistory_ValidUser_ReturnsOk()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/users/{user.Id}/login-history");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Returns list/array
        var content = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("[", content.Trim());
    }

    [Fact]
    public async Task ApproveAbnormalLogin_InvalidHistoryId_ReturnsNotFound()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var fakeHistoryId = 99999;

        // Act
        var response = await _httpClient.PostAsync($"/api/admin/users/{user.Id}/login-history/{fakeHistoryId}/approve", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartImpersonation_AsM2MClient_ReturnsUnauthorized()
    {
        // M2M Clients have no "User ID" (Subject) in the default token structure used here
        // so StartImpersonation should fail with Unauthorized (as per logic in controller)
        // or BadRequest if it finds a subject but it's not a GUID.
        
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var response = await _httpClient.PostAsync($"/api/admin/users/{user.Id}/impersonate", null);

        // Assert
        // Logic: if (string.IsNullOrEmpty(currentUserIdStr) ...) return Unauthorized();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<UserDetailDto> CreateTestUserAsync()
    {
        var request = new CreateUserDto
        {
            UserName = $"{TEST_PREFIX}user_{Guid.NewGuid()}",
            Email = $"{TEST_PREFIX}user_{Guid.NewGuid()}@example.com",
            FirstName = "Test",
            LastName = "User",
            Password = _testPassword,
            PhoneNumber = "1234567890"
        };
        
        var response = await _httpClient.PostAsJsonAsync("/api/admin/users", request);
        response.EnsureSuccessStatusCode();
        
        var user = await response.Content.ReadFromJsonAsync<UserDetailDto>();
        _createdUserIds.Add(user!.Id.ToString());
        return user;
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[]
        {
            "users.read", "users.create", "users.update", "users.delete", "users.impersonate"
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
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }
}
