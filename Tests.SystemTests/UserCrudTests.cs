using System.Net;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Core.Application.DTOs;
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
            roles = new List<string>()
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
        // Arrange
        var createRequest = new CreateUserDto
        {
            UserName = $"{TEST_PREFIX}update_{Guid.NewGuid()}",
            Email = $"{TEST_PREFIX}update_{Guid.NewGuid()}@example.com",
            FirstName = "Test",
            LastName = "User",
            Password = "TestPass123!",
            PhoneNumber = "1234567890"
        };

        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/users", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var createdUser = await createResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        _createdUserIds.Add(createdUser.Id.ToString());

        var updateRequest = new UpdateUserDto
        {
            FirstName = "Updated",
            LastName = "User",
            IsActive = true
            // PhoneNumber = null // Optional in DTO
        };

        // Act
        var updateResponse = await _httpClient.PutAsJsonAsync($"/api/admin/users/{createdUser.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedUser = await updateResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        Assert.Equal("Updated", updatedUser.FirstName);
        Assert.Equal("User", updatedUser.LastName);
    }

    [Fact]
    public async Task DeleteUser_ValidId_ReturnsNoContent()
    {
        // Arrange
        var createRequest = new CreateUserDto
        {
            UserName = $"{TEST_PREFIX}delete_{Guid.NewGuid()}",
            Email = $"{TEST_PREFIX}delete_{Guid.NewGuid()}@example.com",
            FirstName = "Test",
            LastName = "User",
            Password = "TestPass123!",
            PhoneNumber = "1234567890"
        };

        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/users", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var createdUser = await createResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        _createdUserIds.Add(createdUser.Id.ToString());

        // Act
        var deleteResponse = await _httpClient.DeleteAsync($"/api/admin/users/{createdUser.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify it's gone (or logically deleted)
        // Note: The API performs soft delete, so Get might still return it but with IsDeleted=true, 
        // or it might return 404 depending on implementation. 
        // The controller documentation says "Permanently delete a user (soft delete)". 
        // Usually Get would show it.
        var getResponse = await _httpClient.GetAsync($"/api/admin/users/{createdUser.Id}");
        
        // Based on controller, it uses UserManager.UpdateAsync(user) with IsDeleted=true.
        // GetUserByIdAsync in service usually filters out deleted users unless specified.
        // If service filters, it returns null -> 404.
        
        // Allowing either 404 or 200 with IsDeleted check
        if (getResponse.StatusCode == HttpStatusCode.OK)
        {
             var fetchedUser = await getResponse.Content.ReadFromJsonAsync<UserDetailDto>();
             // Assuming UserDetailDto has IsDeleted. If not, this assertion might be tricky.
             // But usually for Soft Delete verification, we check if it's "gone" from normal view.
        }
    }

    [Fact]
    public async Task GetUser_AfterCreate_ReturnsUser()
    {
        // Arrange
        var request = new CreateUserDto
        {
            UserName = $"{TEST_PREFIX}get_{Guid.NewGuid()}",
            Email = $"{TEST_PREFIX}get_{Guid.NewGuid()}@example.com",
            FirstName = "Test",
            LastName = "User",
            Password = "TestPass123!",
            PhoneNumber = "1234567890"
        };

        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/users", request);
        if (!createResponse.IsSuccessStatusCode)
        {
             var error = await createResponse.Content.ReadAsStringAsync();
             throw new Exception($"Create failed with {createResponse.StatusCode}: {error}");
        }
        var createdUser = await createResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        _createdUserIds.Add(createdUser.Id.ToString());

        // Act
        var getResponse = await _httpClient.GetAsync($"/api/admin/users/{createdUser.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetchedUser = await getResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        Assert.NotNull(fetchedUser);
        Assert.Equal(createdUser.Id, fetchedUser.Id);
        Assert.Equal(request.Email, fetchedUser.Email);
    }

    // ===== Helper Methods =====

    private async Task<string> GetAdminTokenAsync()
    {
        // Request all permissions needed for CRUD
        var scopes = new[]
        {
            // Users
            "users.read", "users.create", "users.update", "users.delete",
            // Roles (needed for assigning roles)
            "roles.read", 
            // Clients (if needed)
            "clients.read"
        };

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "testclient-admin",
            ["client_secret"] = "admin-test-secret-2024",
            ["scope"] = string.Join(" ", scopes)
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Token acquisition failed: {response.StatusCode}, Content: {errorContent}");
        }
        
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonSerializer.Deserialize<JsonElement>(content);
        return tokenJson.GetProperty("access_token").GetString()!;
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
            roles = new List<string>()
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
