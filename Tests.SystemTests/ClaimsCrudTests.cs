using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

public class ClaimsCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;
    private readonly List<int> _createdClaimIds = new();
    private const string TEST_PREFIX = "test_claim_";

    public ClaimsCrudTests(WebIdPServerFixture serverFixture)
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
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupCreatedClaimsAsync();
        _httpClient?.Dispose();
    }

    // ===== Happy Path Tests =====

    [Fact]
    public async Task GetClaims_ReturnsListWithTotalCount()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/claims?skip=0&take=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("items", out _));
        Assert.True(result.TryGetProperty("totalCount", out _));
    }

    [Fact]
    public async Task CreateClaim_ValidData_ReturnsCreated()
    {
        // Arrange
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var request = new
        {
            name = $"{TEST_PREFIX}{shortId}",
            displayName = "Test Claim Display",
            description = "A test claim for system tests",
            claimType = $"test_type_{shortId}",
            userPropertyPath = "CustomData",
            dataType = "String"
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/admin/claims", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("id", out var idProp));
        var claimId = idProp.GetInt32();
        _createdClaimIds.Add(claimId);
    }

    [Fact]
    public async Task GetClaim_ExistingId_ReturnsClaim()
    {
        // Arrange
        var claimId = await CreateTestClaimAsync();

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/claims/{claimId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task UpdateClaim_ValidData_ReturnsOk()
    {
        // Arrange
        var claimId = await CreateTestClaimAsync();
        var updateRequest = new
        {
            displayName = "Updated Display Name",
            description = "Updated description"
        };

        // Act
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/claims/{claimId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteClaim_ExistingId_ReturnsOk()
    {
        // Arrange
        var claimId = await CreateTestClaimAsync();
        _createdClaimIds.Remove(claimId);

        // Act
        var response = await _httpClient.DeleteAsync($"/api/admin/claims/{claimId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===== Failure Path Tests =====

    [Fact]
    public async Task GetClaim_NonExistentId_ReturnsNotFound()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/claims/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteClaim_NonExistentId_ReturnsNotFound()
    {
        // Act
        var response = await _httpClient.DeleteAsync("/api/admin/claims/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetClaims_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        { BaseAddress = new Uri(_serverFixture.BaseUrl) };

        // Act
        var response = await httpClient.GetAsync("/api/admin/claims");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Helper Methods =====

    private async Task<int> CreateTestClaimAsync()
    {
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var request = new
        {
            name = $"{TEST_PREFIX}{shortId}",
            displayName = "Test Claim",
            description = "A test claim",
            claimType = $"test_type_{shortId}",
            userPropertyPath = "CustomData",
            dataType = "String"
        };
        var response = await _httpClient.PostAsJsonAsync("/api/admin/claims", request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        var claimId = result.GetProperty("id").GetInt32();
        _createdClaimIds.Add(claimId);
        return claimId;
    }

    private async Task CleanupCreatedClaimsAsync()
    {
        foreach (var claimId in _createdClaimIds.ToList())
        {
            try
            {
                await _httpClient.DeleteAsync($"/api/admin/claims/{claimId}");
            }
            catch { /* Ignore cleanup errors */ }
        }
        _createdClaimIds.Clear();
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/admin/claims?take=100&search={TEST_PREFIX}");
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
                            var id = idProp.GetInt32();
                            await _httpClient.DeleteAsync($"/api/admin/claims/{id}");
                        }
                    }
                }
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[] { "claims.read", "claims.create", "claims.update", "claims.delete" };
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
