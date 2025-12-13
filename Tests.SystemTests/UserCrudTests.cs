using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// CRUD tests for User management with automatic cleanup
/// Uses IAsyncLifetime to cleanup test data before and after tests
/// </summary>
public class UserCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly List<string> _createdUserIds = new();
    private string? _adminToken;
    
    // Test data prefix for easy identification and cleanup
    private const string TEST_PREFIX = "test_crud_";

    public UserCrudTests(WebIdPServerFixture serverFixture)
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
        // 1. Ensure server is running
        await _serverFixture.EnsureServerRunningAsync();
        await Task.Delay(1000);

        // 2. Get admin token (M2M for testing)
        _adminToken = await GetAdminTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        // 3. Cleanup any leftover test data from previous runs
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        // Cleanup all created test data
        await CleanupCreatedUsersAsync();
        await CleanupTestDataAsync(); // Extra safety cleanup
        _httpClient?.Dispose();
    }

    [Fact]
    public async Task CreateUser_ValidData_ReturnsCreated()
    {
        // Arrange
        var createRequest = new
        {
            email = $"{TEST_PREFIX}{Guid.NewGuid()}@test.local",
            username = $"{TEST_PREFIX}user_{DateTime.UtcNow.Ticks}",
            firstName = "Test",
            lastName = "User",
            password = "TestPass123!",
            isActive = true,
            roleIds = new List<string>()
        };

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/admin/users", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var createdUser = JsonSerializer.Deserialize<JsonElement>(responseContent);
        var userId = createdUser.GetProperty("id").GetString();
        
        Assert.NotNull(userId);
        _createdUserIds.Add(userId!); // Track for cleanup
        
        Assert.Equal(createRequest.email, createdUser.GetProperty("email").GetString());
    }

    [Fact]
    public async Task UpdateUser_ValidData_ReturnsOk()
    {
        // Arrange - Create user first
        var userId = await CreateTestUserAsync();
        _createdUserIds.Add(userId);

        var updateRequest = new
        {
            email = $"{TEST_PREFIX}updated_{Guid.NewGuid()}@test.local",
            firstName = "Updated",
            lastName = "Name",
            isActive = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(updateRequest),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PutAsync($"/api/admin/users/{userId}", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var updatedUser = JsonSerializer.Deserialize<JsonElement>(responseContent);
        Assert.Equal("Updated", updatedUser.GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task DeleteUser_ValidId_ReturnsNoContent()
    {
        // Arrange - Create user first
        var userId = await CreateTestUserAsync();
        _createdUserIds.Add(userId);

        // Act
        var response = await _httpClient.DeleteAsync($"/api/admin/users/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        
        // Verify user is soft-deleted (removed from created list since it's already deleted)
        _createdUserIds.Remove(userId);
    }

    [Fact]
    public async Task GetUser_AfterCreate_ReturnsUser()
    {
        // Arrange - Create user first
        var userId = await CreateTestUserAsync();
        _createdUserIds.Add(userId);

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/users/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<JsonElement>(responseContent);
        Assert.Equal(userId, user.GetProperty("id").GetString());
    }

    // ===== Helper Methods =====

    private async Task<string> GetAdminTokenAsync()
    {
        // NOTE: This requires a test M2M client with admin API scopes
        // For now, return empty and skip authenticated tests
        // TODO: Add admin M2M client to ClientSeeder
        return string.Empty;
    }

    private async Task<string> CreateTestUserAsync()
    {
        var createRequest = new
        {
            email = $"{TEST_PREFIX}{Guid.NewGuid()}@test.local",
            username = $"{TEST_PREFIX}user_{DateTime.UtcNow.Ticks}",
            firstName = "Test",
            lastName = "User",
            password = "TestPass123!",
            isActive = true,
            roleIds = new List<string>()
        };

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/api/admin/users", content);
        response.EnsureSuccessStatusCode();
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var createdUser = JsonSerializer.Deserialize<JsonElement>(responseContent);
        return createdUser.GetProperty("id").GetString()!;
    }

    private async Task CleanupCreatedUsersAsync()
    {
        foreach (var userId in _createdUserIds.ToList())
        {
            try
            {
                await _httpClient.DeleteAsync($"/api/admin/users/{userId}");
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        _createdUserIds.Clear();
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            // Get all users and delete those with test prefix
            var response = await _httpClient.GetAsync($"/api/admin/users?take=100");
            if (!response.IsSuccessStatusCode) return;

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            if (result.TryGetProperty("items", out var items))
            {
                foreach (var user in items.EnumerateArray())
                {
                    var email = user.GetProperty("email").GetString();
                    if (email != null && email.StartsWith(TEST_PREFIX))
                    {
                        var userId = user.GetProperty("id").GetString();
                        await _httpClient.DeleteAsync($"/api/admin/users/{userId}");
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
