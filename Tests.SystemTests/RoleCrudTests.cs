using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Application.DTOs;
using Xunit;

namespace Tests.SystemTests;

[Collection("SystemTests")]
public class RoleCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly List<string> _createdRoleIds = new();
    private string? _adminToken;
    
    // Test data prefix
    private const string TEST_PREFIX = "test_role_";

    public RoleCrudTests(WebIdPServerFixture serverFixture)
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
        
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var roleId in _createdRoleIds)
        {
            await _httpClient.DeleteAsync($"/api/admin/roles/{roleId}");
        }
        await CleanupTestDataAsync();
        _httpClient.Dispose();
    }

    [Fact]
    public async Task CreateRole_ValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateRoleDto
        {
            Name = $"{TEST_PREFIX}create_{Guid.NewGuid()}",
            Description = "Test Role Description",
            Permissions = new List<string> { "users.read" }
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/roles", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var createdRole = await response.Content.ReadFromJsonAsync<RoleDetailDto>();
        Assert.NotNull(createdRole);
        _createdRoleIds.Add(createdRole.Id.ToString());
        
        Assert.Equal(request.Name, createdRole.Name);
        Assert.Equal(request.Description, createdRole.Description);
    }

    [Fact]
    public async Task GetRole_AfterCreate_ReturnsRole()
    {
        // Arrange
        var request = new CreateRoleDto
        {
            Name = $"{TEST_PREFIX}get_{Guid.NewGuid()}",
            Description = "Test Role for Get",
            Permissions = new List<string> { "users.read" }
        };

        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/roles", request);
        createResponse.EnsureSuccessStatusCode();
        var createdRole = await createResponse.Content.ReadFromJsonAsync<RoleDetailDto>();
        _createdRoleIds.Add(createdRole.Id.ToString());

        // Act
        var getResponse = await _httpClient.GetAsync($"/api/admin/roles/{createdRole.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetchedRole = await getResponse.Content.ReadFromJsonAsync<RoleDetailDto>();
        Assert.Equal(createdRole.Id, fetchedRole.Id);
        Assert.Equal(request.Name, fetchedRole.Name);
    }

    [Fact]
    public async Task UpdateRole_ValidData_ReturnsOk()
    {
        // Arrange
        var createRequest = new CreateRoleDto
        {
            Name = $"{TEST_PREFIX}update_{Guid.NewGuid()}",
            Description = "Original Description"
        };

        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/roles", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var createdRole = await createResponse.Content.ReadFromJsonAsync<RoleDetailDto>();
        _createdRoleIds.Add(createdRole.Id.ToString());

        var updateRequest = new UpdateRoleDto
        {
            Name = createdRole.Name + "_updated",
            Description = "Updated Description",
            Permissions = new List<string> { "roles.read" }
        };

        // Act
        var updateResponse = await _httpClient.PutAsJsonAsync($"/api/admin/roles/{createdRole.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedRole = await updateResponse.Content.ReadFromJsonAsync<RoleDetailDto>();
        Assert.Equal(updateRequest.Name, updatedRole.Name);
        Assert.Equal(updateRequest.Description, updatedRole.Description);
        Assert.Contains("roles.read", updatedRole.Permissions);
    }

    [Fact]
    public async Task CreateRole_MissingName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateRoleDto
        {
            Name = "", // Invalid
            Description = "Test Role Description"
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/roles", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_ValidId_ReturnsNoContent()
    {
        // Arrange
        var request = new CreateRoleDto
        {
            Name = $"{TEST_PREFIX}delete_{Guid.NewGuid()}",
            Description = "To be deleted"
        };

        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/roles", request);
        createResponse.EnsureSuccessStatusCode();
        var createdRole = await createResponse.Content.ReadFromJsonAsync<RoleDetailDto>();
        _createdRoleIds.Add(createdRole.Id.ToString());

        // Act
        var deleteResponse = await _httpClient.DeleteAsync($"/api/admin/roles/{createdRole.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        
        // Verify gone
        var getResponse = await _httpClient.GetAsync($"/api/admin/roles/{createdRole.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetAvailablePermissions_ReturnsOk_AndList()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/roles/permissions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var permissions = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(permissions);
        Assert.NotEmpty(permissions);
        Assert.Contains("users.read", permissions);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[]
        {
            "roles.read", "roles.create", "roles.update", "roles.delete",
            "users.read" // Required to assign permissions if validated
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

    private async Task CleanupTestDataAsync()
    {
        // Add logic to cleanup roles by prefix if necessary, 
        // but individual test cleanup should suffice if tests pass.
        // Implementing 'Get All & Filter' is expensive/complex here without a direct DB context or search API helper.
        // Relying on DisposeAsync.
        await Task.CompletedTask;
    }
}
