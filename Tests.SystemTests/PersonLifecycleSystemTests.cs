using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Person lifecycle tests - marked as Slow due to multiple API calls per test.
/// Run with: dotnet test --filter "Category!=Slow" to skip.
/// </summary>
[Trait("Category", "Slow")]
public class PersonLifecycleSystemTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly HttpClient _httpClient;
    private readonly WebIdPServerFixture _serverFixture;
    private string? _adminToken;

    // Use M2M client for stable API access (avoids cookie/session issues)
    private const string M2M_CLIENT_ID = "testclient-admin";
    private const string M2M_CLIENT_SECRET = "admin-test-secret-2024";

    public PersonLifecycleSystemTests(WebIdPServerFixture fixture)
    {
        _serverFixture = fixture;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(fixture.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        _adminToken = await GetM2MAdminTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    #region Status Tests

    [Fact]
    public async Task CreatePerson_WithActiveStatus_CanAuthenticate()
    {
        var person = await CreateTestPersonAsync("Active");
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.True(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithSuspendedStatus_CannotAuthenticate()
    {
        var person = await CreateTestPersonAsync("Suspended");
        Assert.Equal("Suspended", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithPendingStatus_CannotAuthenticate()
    {
        var person = await CreateTestPersonAsync("Pending");
        Assert.Equal("Pending", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithTerminatedStatus_CannotAuthenticate()
    {
        var person = await CreateTestPersonAsync("Terminated");
        Assert.Equal("Terminated", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    #endregion

    #region Date-based Tests

    [Fact]
    public async Task CreatePerson_ActiveWithFutureStartDate_CannotAuthenticate()
    {
        var futureDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var person = await CreateTestPersonAsync("Active", startDate: futureDate);
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_ActiveWithPastEndDate_CannotAuthenticate()
    {
        var pastDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var person = await CreateTestPersonAsync("Active", endDate: pastDate);
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromActiveToSuspended_Works()
    {
        // Create active person
        var person = await CreateTestPersonAsync("Active");
        var personId = person.GetProperty("id").GetString();
        var firstName = person.GetProperty("firstName").GetString();

        // Update to suspended
        var updateResponse = await _httpClient.PutAsJsonAsync($"/api/admin/people/{personId}", new
        {
            firstName,
            lastName = "User",
            status = "Suspended"
        });
        Assert.True(updateResponse.IsSuccessStatusCode, $"Update failed: {updateResponse.StatusCode}");

        // Verify
        var getResponse = await _httpClient.GetAsync($"/api/admin/people/{personId}");
        Assert.True(getResponse.IsSuccessStatusCode);
        var updated = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("Suspended", updated.GetProperty("status").GetString());
        Assert.False(updated.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromSuspendedToActive_Works()
    {
        // Create suspended person
        var person = await CreateTestPersonAsync("Suspended");
        var personId = person.GetProperty("id").GetString();
        var firstName = person.GetProperty("firstName").GetString();

        // Update to active
        var updateResponse = await _httpClient.PutAsJsonAsync($"/api/admin/people/{personId}", new
        {
            firstName,
            lastName = "User",
            status = "Active"
        });
        Assert.True(updateResponse.IsSuccessStatusCode, $"Update failed: {updateResponse.StatusCode}");

        // Verify
        var getResponse = await _httpClient.GetAsync($"/api/admin/people/{personId}");
        Assert.True(getResponse.IsSuccessStatusCode);
        var updated = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("Active", updated.GetProperty("status").GetString());
        Assert.True(updated.GetProperty("canAuthenticate").GetBoolean());
    }

    #endregion

    #region Helper Methods

    private async Task<JsonElement> CreateTestPersonAsync(string status, string? startDate = null, string? endDate = null)
    {
        var firstName = $"Test_{status}_{DateTime.UtcNow.Ticks}";

        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = firstName,
            ["lastName"] = "User",
            ["status"] = status,
            ["department"] = "Test Department",
            ["jobTitle"] = "Test Title"
        };

        if (startDate != null) payload["startDate"] = startDate;
        if (endDate != null) payload["endDate"] = endDate;

        var response = await _httpClient.PostAsJsonAsync("/api/admin/people", payload);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Create person failed: {response.StatusCode} - {content}");

        return JsonDocument.Parse(content).RootElement;
    }

    private async Task<string> GetM2MAdminTokenAsync()
    {
        var scopes = new[] { "persons.read", "persons.write" };
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = M2M_CLIENT_ID,
            ["client_secret"] = M2M_CLIENT_SECRET,
            ["scope"] = string.Join(" ", scopes)
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement.GetProperty("access_token").GetString()!;
    }

    #endregion
}
