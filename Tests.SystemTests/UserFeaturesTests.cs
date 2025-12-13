using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Application.DTOs;
using Xunit;

namespace Tests.SystemTests;

[Collection("SystemTests")]
public class UserFeaturesTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly List<string> _createdUserIds = new();
    private readonly List<string> _createdRoleIds = new();
    private string? _adminToken;

    private const string TEST_PREFIX = "test_feat_";
    private readonly string _testPassword = "TestPass123!"; // Strong password

    public UserFeaturesTests(WebIdPServerFixture serverFixture)
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
        // Cleanup users
        foreach (var userId in _createdUserIds)
        {
            await _httpClient.DeleteAsync($"/api/admin/users/{userId}");
        }

        // Cleanup roles
        foreach (var roleId in _createdRoleIds)
        {
            await _httpClient.DeleteAsync($"/api/admin/roles/{roleId}");
        }
        
        _httpClient.Dispose();
    }

    [Fact]
    public async Task DeactivateUser_ValidId_ReturnsNoContent_AndUserIsInactive()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var response = await _httpClient.PostAsync($"/api/admin/users/{user.Id}/deactivate", null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var verifyResponse = await _httpClient.GetAsync($"/api/admin/users/{user.Id}");
        verifyResponse.EnsureSuccessStatusCode();
        var updatedUser = await verifyResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        
        // Assuming IsActive is false (Verify property name in UserDetailDto, usually IsActive)
        Assert.False(updatedUser!.IsActive);
    }

    [Fact]
    public async Task ReactivateUser_ValidId_ReturnsOk_AndUserIsActive()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        
        // Deactivate first
        var deactResponse = await _httpClient.PostAsync($"/api/admin/users/{user.Id}/deactivate", null);
        deactResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _httpClient.PostAsync($"/api/admin/users/{user.Id}/reactivate", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reactivatedUser = await response.Content.ReadFromJsonAsync<UserDetailDto>();
        Assert.True(reactivatedUser!.IsActive);

        // Verify Get
        var verifyResponse = await _httpClient.GetAsync($"/api/admin/users/{user.Id}");
        var fetchedUser = await verifyResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        Assert.True(fetchedUser!.IsActive);
    }

    [Fact]
    public async Task AssignRoles_ValidRoleNames_ReturnsOk_AndRolesAssigned()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var role1 = await CreateTestRoleAsync();
        var role2 = await CreateTestRoleAsync();

        // Check initial roles 
        // (Default user might have no roles or default roles, assume empty for test user setup)
        Assert.DoesNotContain(role1.Name, user.Roles);

        var request = new { Roles = new List<string> { role1.Name, role2.Name } };

        // Act
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/users/{user.Id}/roles", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedUser = await response.Content.ReadFromJsonAsync<UserDetailDto>();
        
        Assert.Contains(role1.Name, updatedUser!.Roles);
        Assert.Contains(role2.Name, updatedUser.Roles);
        
        // Verify via Get
        var verifyResponse = await _httpClient.GetAsync($"/api/admin/users/{user.Id}");
        var fetchedUser = await verifyResponse.Content.ReadFromJsonAsync<UserDetailDto>();
        Assert.Contains(role1.Name, fetchedUser!.Roles);
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

    private async Task<RoleDetailDto> CreateTestRoleAsync()
    {
        var request = new CreateRoleDto
        {
            Name = $"{TEST_PREFIX}role_{Guid.NewGuid()}",
            Description = "Test Role"
        };
        
        var response = await _httpClient.PostAsJsonAsync("/api/admin/roles", request);
        response.EnsureSuccessStatusCode();

        var role = await response.Content.ReadFromJsonAsync<RoleDetailDto>();
        _createdRoleIds.Add(role!.Id.ToString());
        return role;
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[]
        {
            "users.read", "users.create", "users.update", "users.delete",
            "roles.read", "roles.create", "roles.update", "roles.delete"
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
