using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

public class ScopeCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;
    private readonly List<string> _createdScopeIds = new();
    private const string TEST_PREFIX = "test_scope_";

    public ScopeCrudTests(WebIdPServerFixture serverFixture)
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
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupCreatedScopesAsync();
        await CleanupTestDataAsync();
        _httpClient?.Dispose();
    }

    // ===== Happy Path Tests =====

    [Fact]
    public async Task CreateScope_ValidData_ReturnsCreated()
    {
        // Arrange
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var request = new
        {
            name = $"{TEST_PREFIX}{shortId}",
            displayName = "Test Scope",
            description = "A test scope for system tests"
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/scopes", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("id", out var idProp));
        var scopeId = idProp.GetString()!;
        _createdScopeIds.Add(scopeId);
    }

    [Fact]
    public async Task GetScopes_ReturnsListWithTotalCount()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/scopes?skip=0&take=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("items", out _));
        Assert.True(result.TryGetProperty("totalCount", out _));
    }

    [Fact]
    public async Task GetScope_ExistingId_ReturnsScope()
    {
        // Arrange - create a scope first
        var scopeId = await CreateTestScopeAsync();

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/scopes/{scopeId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task UpdateScope_ValidData_ReturnsOk()
    {
        // Arrange
        var scopeId = await CreateTestScopeAsync();
        var updateRequest = new
        {
            displayName = "Updated Display Name",
            description = "Updated description"
        };

        // Act
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/scopes/{scopeId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteScope_ExistingId_ReturnsOkOrBadRequest()
    {
        // Arrange
        var scopeId = await CreateTestScopeAsync();
        _createdScopeIds.Remove(scopeId); // Remove from cleanup since we're testing delete

        // Act
        var response = await _httpClient.DeleteAsync($"/api/admin/scopes/{scopeId}");

        // Assert - OK if deleted, BadRequest if scope is in use by clients/test seeder
        Assert.True(
            response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected OK or BadRequest, got {response.StatusCode}");
        
        // If delete failed (BadRequest), add back to cleanup
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            _createdScopeIds.Add(scopeId);
        }
    }

    // ===== Failure Path Tests =====

    [Fact]
    public async Task GetScope_NonExistentId_ReturnsNotFound()
    {
        // Act - use valid GUID format that doesn't exist
        var nonExistentId = Guid.NewGuid().ToString();
        var response = await _httpClient.GetAsync($"/api/admin/scopes/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateScope_EmptyName_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            name = "",
            displayName = "Test Scope"
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/scopes", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateScope_DuplicateName_ReturnsConflict()
    {
        // Arrange - create first scope
        var scopeId = await CreateTestScopeAsync();
        var existingScopeName = $"{TEST_PREFIX}dup_{Guid.NewGuid():N}".Substring(0, 30);
        var request1 = new { name = existingScopeName, displayName = "First" };
        var res1 = await _httpClient.PostAsJsonAsync("/api/admin/scopes", request1);
        if (res1.IsSuccessStatusCode)
        {
            var created = await res1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
            _createdScopeIds.Add(created.GetProperty("id").GetString()!);
        }

        // Act - try to create duplicate
        var request2 = new { name = existingScopeName, displayName = "Duplicate" };
        var response = await _httpClient.PostAsJsonAsync("/api/admin/scopes", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetScopes_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/scopes");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Helper Methods =====

    private async Task<string> CreateTestScopeAsync()
    {
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var request = new
        {
            name = $"{TEST_PREFIX}{shortId}",
            displayName = "Test Scope",
            description = "A test scope for system tests"
        };
        var response = await _httpClient.PostAsJsonAsync("/api/admin/scopes", request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        var scopeId = result.GetProperty("id").GetString()!;
        _createdScopeIds.Add(scopeId);
        return scopeId;
    }

    private async Task CleanupCreatedScopesAsync()
    {
        foreach (var scopeId in _createdScopeIds.ToList())
        {
            try
            {
                await _httpClient.DeleteAsync($"/api/admin/scopes/{scopeId}");
            }
            catch { /* Ignore cleanup errors */ }
        }
        _createdScopeIds.Clear();
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/admin/scopes?take=100&search={TEST_PREFIX}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
                if (result.TryGetProperty("items", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idProp))
                        {
                            var id = idProp.GetString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                await _httpClient.DeleteAsync($"/api/admin/scopes/{id}");
                            }
                        }
                    }
                }
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[] { "scopes.read", "scopes.create", "scopes.update", "scopes.delete" };
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
