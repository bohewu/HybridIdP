using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Core.Application.DTOs;
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

    // Use M2M client for stable API access
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
        Assert.Equal("Active", person.Status);
        Assert.True(person.CanAuthenticate);
    }

    [Fact]
    public async Task CreatePerson_WithSuspendedStatus_CannotAuthenticate()
    {
        var person = await CreateTestPersonAsync("Suspended");
        Assert.Equal("Suspended", person.Status);
        Assert.False(person.CanAuthenticate);
    }

    [Fact]
    public async Task CreatePerson_WithPendingStatus_CannotAuthenticate()
    {
        var person = await CreateTestPersonAsync("Pending");
        Assert.Equal("Pending", person.Status);
        Assert.False(person.CanAuthenticate);
    }

    [Fact]
    public async Task CreatePerson_WithTerminatedStatus_CannotAuthenticate()
    {
        var person = await CreateTestPersonAsync("Terminated");
        Assert.Equal("Terminated", person.Status);
        Assert.False(person.CanAuthenticate);
    }

    #endregion

    #region Date-based Tests

    [Fact]
    public async Task CreatePerson_ActiveWithFutureStartDate_CannotAuthenticate()
    {
        var futureDate = DateTime.UtcNow.AddDays(1);
        var person = await CreateTestPersonAsync("Active", startDate: futureDate);
        Assert.Equal("Active", person.Status);
        Assert.False(person.CanAuthenticate);
    }

    [Fact]
    public async Task CreatePerson_ActiveWithPastEndDate_CannotAuthenticate()
    {
        var pastDate = DateTime.UtcNow.AddDays(-1);
        var person = await CreateTestPersonAsync("Active", endDate: pastDate);
        Assert.Equal("Active", person.Status);
        Assert.False(person.CanAuthenticate);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromActiveToSuspended_Works()
    {
        // Create active person
        var person = await CreateTestPersonAsync("Active");
        var personId = person.Id;

        // Update to suspended
        var updateDto = new PersonDto
        {
            FirstName = person.FirstName,
            LastName = person.LastName,
            Status = "Suspended",
            Department = person.Department,
            JobTitle = person.JobTitle
        };

        var updateResponse = await _httpClient.PutAsJsonAsync($"/api/admin/people/{personId}", updateDto);
        Assert.True(updateResponse.IsSuccessStatusCode, $"Update failed: {updateResponse.StatusCode}");

        var updated = await updateResponse.Content.ReadFromJsonAsync<PersonResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal("Suspended", updated.Status);
        Assert.False(updated.CanAuthenticate);
    }

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromSuspendedToActive_Works()
    {
        // Create suspended person
        var person = await CreateTestPersonAsync("Suspended");
        var personId = person.Id;

        // Update to active
        var updateDto = new PersonDto
        {
            FirstName = person.FirstName,
            LastName= person.LastName,
            Status = "Active",
            Department = person.Department,
            JobTitle = person.JobTitle
        };

        var updateResponse = await _httpClient.PutAsJsonAsync($"/api/admin/people/{personId}", updateDto);
        Assert.True(updateResponse.IsSuccessStatusCode, $"Update failed: {updateResponse.StatusCode}");

        var updated = await updateResponse.Content.ReadFromJsonAsync<PersonResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal("Active", updated.Status);
        Assert.True(updated.CanAuthenticate);
    }

    #endregion

    #region Helper Methods

    private async Task<PersonResponseDto> CreateTestPersonAsync(
        string status, 
        DateTime? startDate = null, 
        DateTime? endDate = null)
    {
        var dto = new PersonDto
        {
            FirstName = $"Test_{status}_{DateTime.UtcNow.Ticks}",
            LastName = "User",
            Status = status,
            Department = "Test Department",
            JobTitle = "Test Title",
            StartDate = startDate,
            EndDate = endDate
        };

        var response = await _httpClient.PostAsJsonAsync("/api/admin/people", dto);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Create person failed: {response.StatusCode} - {content}");

        var createdPerson = await response.Content.ReadFromJsonAsync<PersonResponseDto>();
        Assert.NotNull(createdPerson);
        return createdPerson;
    }

    private async Task<string> GetM2MAdminTokenAsync()
    {
        var scopes = new[] { "persons.read", "persons.create", "persons.update" };
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = M2M_CLIENT_ID,
            ["client_secret"] = M2M_CLIENT_SECRET,
            ["scope"] = string.Join(" ", scopes)
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        var content = await response.Content.ReadAsStringAsync();
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Token request failed: {response.StatusCode} - {content}");
        }

        // Parse JSON manually to avoid deserialization issues
        var json = System.Text.Json.JsonDocument.Parse(content);
        var accessToken = json.RootElement.GetProperty("access_token").GetString();
        
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new Exception("Access token is null or empty");
        }
        
        return accessToken;
    }

    #endregion
}
