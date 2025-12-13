using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Application.DTOs;
using Xunit;

namespace Tests.SystemTests;

public class ClientCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private string? _adminToken;
    private readonly List<string> _createdClientIds = new();
    private const string TEST_PREFIX = "test_client_";
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ClientCrudTests(WebIdPServerFixture serverFixture, Xunit.Abstractions.ITestOutputHelper output)
    {
        _serverFixture = serverFixture;
        _output = output;
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
        await CleanupCreatedClientsAsync();
        await CleanupTestDataAsync();
        _httpClient?.Dispose();
    }

    [Fact]
    public async Task CreateClient_ValidData_ReturnsCreated()
    {
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}{Guid.NewGuid()}",
            ClientSecret: "TestSecret123!",
            DisplayName: "Test Client",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: new List<string> { "https://localhost:5000/callback" },
            PostLogoutRedirectUris: new List<string> { "https://localhost:5000/logout" },
            Permissions: new List<string> { "openid", "profile" }
        );

        var response = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Create Response: {response.StatusCode} - {content}");
        var created = JsonSerializer.Deserialize<CreateClientResponse>(content, _jsonOptions);
        Assert.NotNull(created);
        Assert.Equal(request.ClientId, created.ClientId);
        // Note: Server may generate a new secret or return null depending on implementation
        // For confidential clients, a secret should be present (either provided or generated)
        _createdClientIds.Add(created.Id);
    }

    [Fact]
    public async Task GetClient_ExistingId_ReturnsClient()
    {
        var clientIdVal = $"{TEST_PREFIX}get_{Guid.NewGuid()}";
        var request = new CreateClientRequest(
            ClientId: clientIdVal,
            ClientSecret: "TestSecret123!",
            DisplayName: "Get Client",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: new List<string>(),
            PostLogoutRedirectUris: new List<string>(),
            Permissions: new List<string> { "openid" }
        );
        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        var getResponse = await _httpClient.GetAsync($"/api/admin/clients/{created.Id}");
        
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ClientDetail>(_jsonOptions);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(clientIdVal, fetched.ClientId);
    }

    [Fact]
    public async Task UpdateClient_ValidData_ReturnsOk()
    {
        var clientIdVal = $"{TEST_PREFIX}upd_{Guid.NewGuid()}";
        var createRequest = new CreateClientRequest(
            ClientId: clientIdVal,
            ClientSecret: "Secret1",
            DisplayName: "Original Name",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: new List<string>(),
            PostLogoutRedirectUris: new List<string>(),
            Permissions: new List<string>()
        );
        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/clients", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);
        
        var updateRequest = new UpdateClientRequest(
            ClientId: clientIdVal, // ClientId usually immutable but maybe updateable? 
            ClientSecret: null, // Don't update secret
            DisplayName: "Updated Name",
            Type: "confidential",
            ConsentType: "implicit",
            RedirectUris: new List<string> { "https://newsite.com" },
            PostLogoutRedirectUris: new List<string>(),
            Permissions: new List<string> { "openid", "email" }
        );

        var response = await _httpClient.PutAsJsonAsync($"/api/admin/clients/{created.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify update
        var getResponse = await _httpClient.GetAsync($"/api/admin/clients/{created.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<ClientDetail>(_jsonOptions);
        Assert.Equal("Updated Name", fetched!.DisplayName);
        Assert.Equal("implicit", fetched.ConsentType);
        Assert.Contains("https://newsite.com", fetched.RedirectUris);
        Assert.Contains("email", fetched.Permissions); // Note: Permissions might need full OpenIddict permission string format (e.g. 'lst:email'?) - actually likely prefixes unless simplified. 
        // Based on basic logic, it's just strings. 
    }

    [Fact]
    public async Task DeleteClient_ValidId_ReturnsOk()
    {
         var clientIdVal = $"{TEST_PREFIX}del_{Guid.NewGuid()}";
         var request = new CreateClientRequest(
            ClientId: clientIdVal,
            ClientSecret: null, // Public clients don't have secrets
            DisplayName: "Del",
            ApplicationType: "native",
            Type: "public",
            ConsentType: "explicit",
            RedirectUris: null,
            PostLogoutRedirectUris: null,
            Permissions: null
        );
        var createResponse = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        var delResponse = await _httpClient.DeleteAsync($"/api/admin/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, delResponse.StatusCode);

        var getResponse = await _httpClient.GetAsync($"/api/admin/clients/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
    
    [Fact]
    public async Task RegenerateSecret_ConfidentialClient_ReturnsNewSecret()
    {
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}sec_{Guid.NewGuid()}",
            ClientSecret: "OldSecret",
            DisplayName: "Secret Client",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        var rResponse = await _httpClient.PostAsync($"/api/admin/clients/{created.Id}/regenerate-secret", null);
        Assert.Equal(HttpStatusCode.OK, rResponse.StatusCode);
        
        using var doc = await JsonDocument.ParseAsync(await rResponse.Content.ReadAsStreamAsync());
        var newSecret = doc.RootElement.GetProperty("clientSecret").GetString();
        Assert.NotNull(newSecret);
        Assert.NotEqual("OldSecret", newSecret);
    }

    [Fact]
    public async Task GetClients_ReturnsList()
    {
        // Arrange - create a test client first
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}list_{Guid.NewGuid()}",
            ClientSecret: "Secret123",
            DisplayName: "List Test",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        // Act
        var response = await _httpClient.GetAsync("/api/admin/clients?take=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("items", out var items));
        Assert.True(items.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetAllowedScopes_ReturnsScopes()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}scope_{Guid.NewGuid()}",
            ClientSecret: "Secret123",
            DisplayName: "Scope Test",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/clients/{created.Id}/scopes");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetAllowedScopes_UpdatesScopes()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}setscope_{Guid.NewGuid()}",
            ClientSecret: "Secret123",
            DisplayName: "Set Scope Test",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        var scopeRequest = new { scopes = new List<string> { "openid", "profile" } };

        // Act
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/clients/{created.Id}/scopes", scopeRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRequiredScopes_ReturnsScopes()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}reqscope_{Guid.NewGuid()}",
            ClientSecret: "Secret123",
            DisplayName: "Req Scope Test",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/clients/{created.Id}/required-scopes");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetRequiredScopes_UpdatesScopes()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}setreq_{Guid.NewGuid()}",
            ClientSecret: "Secret123",
            DisplayName: "Set Req Scope Test",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        var scopeRequest = new { scopes = new List<string> { "openid" } };

        // Act
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/clients/{created.Id}/required-scopes", scopeRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidateScopes_ReturnsAllowedScopes()
    {
        // Arrange
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}validate_{Guid.NewGuid()}",
            ClientSecret: "Secret123",
            DisplayName: "Validate Scope Test",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        // Set some allowed scopes first
        var setRequest = new { scopes = new List<string> { "openid", "profile", "email" } };
        await _httpClient.PutAsJsonAsync($"/api/admin/clients/{created.Id}/scopes", setRequest);

        // Act - validate which of requested scopes are allowed
        var validateRequest = new { requestedScopes = new List<string> { "openid", "profile", "offline_access" } };
        var response = await _httpClient.PostAsJsonAsync($"/api/admin/clients/{created.Id}/scopes/validate", validateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===== Failure Path Tests =====

    [Fact]
    public async Task GetClient_NonExistentId_ReturnsNotFound()
    {
        var fakeId = Guid.NewGuid().ToString();
        var response = await _httpClient.GetAsync($"/api/admin/clients/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateClient_NonExistentId_ReturnsNotFound()
    {
        var fakeId = Guid.NewGuid().ToString();
        var updateRequest = new UpdateClientRequest(
            ClientId: "fake",
            ClientSecret: null,
            DisplayName: "Updated",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null,
            PostLogoutRedirectUris: null,
            Permissions: null
        );
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/clients/{fakeId}", updateRequest);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteClient_NonExistentId_ReturnsNotFound()
    {
        var fakeId = Guid.NewGuid().ToString();
        var response = await _httpClient.DeleteAsync($"/api/admin/clients/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateClient_DuplicateClientId_ReturnsConflict()
    {
        // Arrange - create first client
        var clientId = $"{TEST_PREFIX}dup_{Guid.NewGuid()}";
        var request = new CreateClientRequest(
            ClientId: clientId,
            ClientSecret: "Secret123",
            DisplayName: "First",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        // Act - try to create duplicate
        var duplicateRequest = new CreateClientRequest(
            ClientId: clientId, // Same ID
            ClientSecret: "Secret456",
            DisplayName: "Duplicate",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var response = await _httpClient.PostAsJsonAsync("/api/admin/clients", duplicateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateClient_MissingClientId_ReturnsBadRequest()
    {
        var request = new CreateClientRequest(
            ClientId: "", // Empty
            ClientSecret: "Secret123",
            DisplayName: "Missing ID",
            ApplicationType: "web",
            Type: "confidential",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );

        var response = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegenerateSecret_PublicClient_ReturnsBadRequest()
    {
        // Arrange - create a public client (no secret allowed)
        var request = new CreateClientRequest(
            ClientId: $"{TEST_PREFIX}pub_{Guid.NewGuid()}",
            ClientSecret: null,
            DisplayName: "Public Client",
            ApplicationType: "native",
            Type: "public",
            ConsentType: "explicit",
            RedirectUris: null, PostLogoutRedirectUris: null, Permissions: null
        );
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/clients", request);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<CreateClientResponse>(_jsonOptions);
        _createdClientIds.Add(created!.Id);

        // Act - try to regenerate secret for public client
        var response = await _httpClient.PostAsync($"/api/admin/clients/{created.Id}/regenerate-secret", null);

        // Assert - should fail for public clients
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[] { "clients.read", "clients.create", "clients.update", "clients.delete" };
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

    private async Task CleanupCreatedClientsAsync()
    {
        foreach (var id in _createdClientIds)
        {
            try { await _httpClient.DeleteAsync($"/api/admin/clients/{id}"); } catch {}
        }
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/admin/clients?take=100");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var items = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("items");
                foreach (var item in items.EnumerateArray())
                {
                    if (item.GetProperty("clientId").GetString()!.StartsWith(TEST_PREFIX))
                    {
                        var id = item.GetProperty("id").GetString();
                        await _httpClient.DeleteAsync($"/api/admin/clients/{id}");
                    }
                }
            }
        }
        catch {}
    }
}
