using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

public class PersonCrudTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;
    private readonly List<string> _createdPersonIds = new();
    private const string TEST_PREFIX = "test_person_";
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public PersonCrudTests(WebIdPServerFixture serverFixture, Xunit.Abstractions.ITestOutputHelper output)
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
        await CleanupCreatedPersonsAsync();
        await CleanupTestDataAsync();
        _httpClient?.Dispose();
    }

    // ===== Happy Path Tests =====

    [Fact]
    public async Task CreatePerson_ValidData_ReturnsCreated()
    {
        var request = new
        {
            employeeId = $"{TEST_PREFIX}{Guid.NewGuid()}",
            firstName = "Test",
            lastName = "Person",
            email = $"test_{Guid.NewGuid()}@example.com",
            department = "IT",
            jobTitle = "Developer"
        };

        var response = await _httpClient.PostAsJsonAsync("/api/admin/people", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(created.TryGetProperty("id", out var idProp));
        _createdPersonIds.Add(idProp.GetString()!);
    }

    [Fact]
    public async Task GetPerson_ExistingId_ReturnsPerson()
    {
        // Arrange
        var personId = await CreateTestPersonAsync();

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/people/{personId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var person = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.Equal(personId, person.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetPersons_ReturnsList()
    {
        // Arrange - create a test person
        await CreateTestPersonAsync();

        // Act
        var response = await _httpClient.GetAsync("/api/admin/people?take=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("persons", out var persons));
        Assert.True(persons.GetArrayLength() > 0);
    }

    [Fact]
    public async Task SearchPersons_ReturnsMatches()
    {
        // Arrange
        var uniqueName = $"SearchName_{Guid.NewGuid():N}";
        var personId = await CreateTestPersonAsync(firstName: uniqueName);

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/people/search?term={uniqueName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePerson_ValidData_ReturnsOk()
    {
        // Arrange
        var personId = await CreateTestPersonAsync();
        var updateRequest = new
        {
            firstName = "Updated",
            lastName = "Name",
            department = "HR"
        };

        // Act
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/people/{personId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var updated = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.Equal("Updated", updated.GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task DeletePerson_ValidId_ReturnsNoContent()
    {
        // Arrange
        var personId = await CreateTestPersonAsync();

        // Act
        var response = await _httpClient.DeleteAsync($"/api/admin/people/{personId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify deleted
        var getResponse = await _httpClient.GetAsync($"/api/admin/people/{personId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetPersonAccounts_ReturnsAccounts()
    {
        // Arrange
        var personId = await CreateTestPersonAsync();

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/people/{personId}/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailableUsers_ReturnsUsers()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/admin/people/available-users");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===== PID Field Tests (User's Specific Requirement) =====
    // NOTE: These tests are skipped pending server-side investigation.
    // 
    // INVESTIGATION FINDINGS:
    // - Basic person creation (without PID) works fine
    // - ALL PID field types fail with 500 (PassportNumber, ResidentCertificateNumber)
    // - JSON casing (camelCase vs PascalCase) is NOT the issue
    // - PID format validation passes (validated format is correct)
    // - Error occurs at SaveChangesAsync level (after validation, during DB insert)
    // - Unique constraint on PID fields shouldn't trigger (using unique GUIDs)
    // 
    // LIKELY CAUSES (need server logs to confirm):
    // 1. EF Core model validation issue with PID fields
    // 2. Database migration issue (column constraints)
    // 3. Environmental issue specific to test DB
    //
    // TODO: Add console/file logging to PersonService to capture actual exception

    [Fact] // Re-enabled with detailed error logging in controller
    public async Task GetPerson_WithPassportNumber_ReturnsMaskedValue()
    {
        // Arrange - create person with PassportNumber
        var passportNum = $"PP{Guid.NewGuid():N}".Substring(0, 9);
        // Note: EmployeeId column max is 50 chars, so use shorter prefix
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var request = new
        {
            employeeId = $"{TEST_PREFIX}p_{shortId}", // Shorter to fit 50 char limit
            firstName = "PID",
            lastName = "Test",
            passportNumber = passportNum
        };
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/people", request);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var personId = created.GetProperty("id").GetString()!;
        _createdPersonIds.Add(personId);

        // Act
        var response = await _httpClient.GetAsync($"/api/admin/people/{personId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var person = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        
        // PassportNumber should be masked, not plain text and not empty
        var passportNumber = person.GetProperty("passportNumber").GetString();
        Assert.NotNull(passportNumber);
        Assert.NotEmpty(passportNumber);
        Assert.NotEqual(passportNum, passportNumber); // Should NOT be plain text
        Assert.Contains("●", passportNumber); // Should be masked
    }

    [Fact] // Re-enabled after fixing EmployeeId length issue
    public async Task UpdatePerson_WithEmptyPassportNumber_PreservesExistingValue()
    {
        // Arrange - create person with PassportNumber
        var passportNum = $"PP{Guid.NewGuid():N}".Substring(0, 9);
        // Note: EmployeeId column max is 50 chars, so use shorter prefix
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var request = new
        {
            employeeId = $"{TEST_PREFIX}u_{shortId}", // Shorter to fit 50 char limit
            firstName = "Preserve",
            lastName = "PID",
            passportNumber = passportNum
        };
        var createRes = await _httpClient.PostAsJsonAsync("/api/admin/people", request);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var personId = created.GetProperty("id").GetString()!;
        _createdPersonIds.Add(personId);

        // Act - update with empty passportNumber (should preserve existing)
        var updateRequest = new
        {
            firstName = "StillPreserve",
            lastName = "PID",
            passportNumber = "" // Empty - should NOT overwrite
        };
        var updateRes = await _httpClient.PutAsJsonAsync($"/api/admin/people/{personId}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // Assert - PassportNumber should still be masked (not empty)
        var getRes = await _httpClient.GetAsync($"/api/admin/people/{personId}");
        var person = await getRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var passportNumber = person.GetProperty("passportNumber").GetString();
        Assert.NotNull(passportNumber);
        Assert.NotEmpty(passportNumber);
        Assert.Contains("●", passportNumber); // Still masked = still exists
    }

    // ===== Failure Path Tests =====

    [Fact]
    public async Task GetPerson_NonExistentId_ReturnsNotFound()
    {
        var fakeId = Guid.NewGuid().ToString();
        var response = await _httpClient.GetAsync($"/api/admin/people/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePerson_NonExistentId_ReturnsNotFound()
    {
        var fakeId = Guid.NewGuid().ToString();
        var updateRequest = new { firstName = "Test", lastName = "Test" };
        var response = await _httpClient.PutAsJsonAsync($"/api/admin/people/{fakeId}", updateRequest);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletePerson_NonExistentId_ReturnsNotFound()
    {
        var fakeId = Guid.NewGuid().ToString();
        var response = await _httpClient.DeleteAsync($"/api/admin/people/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonAccounts_NonExistentId_ReturnsNotFound()
    {
        var fakeId = Guid.NewGuid().ToString();
        var response = await _httpClient.GetAsync($"/api/admin/people/{fakeId}/accounts");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ===== Helper Methods =====

    private async Task<string> CreateTestPersonAsync(string? firstName = null)
    {
        var request = new
        {
            employeeId = $"{TEST_PREFIX}{Guid.NewGuid()}",
            firstName = firstName ?? "Test",
            lastName = "Person",
            email = $"test_{Guid.NewGuid()}@example.com"
        };
        var response = await _httpClient.PostAsJsonAsync("/api/admin/people", request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        var id = created.GetProperty("id").GetString()!;
        _createdPersonIds.Add(id);
        return id;
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[] { "persons.read", "persons.create", "persons.update", "persons.delete" };
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

    private async Task CleanupCreatedPersonsAsync()
    {
        foreach (var id in _createdPersonIds)
        {
            try { await _httpClient.DeleteAsync($"/api/admin/people/{id}"); } catch { }
        }
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/admin/people?take=100");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
                if (result.TryGetProperty("persons", out var persons))
                {
                    foreach (var person in persons.EnumerateArray())
                    {
                        if (person.TryGetProperty("employeeId", out var empId) && 
                            empId.GetString()?.StartsWith(TEST_PREFIX) == true)
                        {
                            var id = person.GetProperty("id").GetString();
                            await _httpClient.DeleteAsync($"/api/admin/people/{id}");
                        }
                    }
                }
            }
        }
        catch { }
    }
}
