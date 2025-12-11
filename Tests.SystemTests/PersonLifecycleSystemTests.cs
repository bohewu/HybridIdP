using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for Person Lifecycle Management (Phase 18)
/// Tests API directly without browser - much faster than E2E
/// </summary>
public class PersonLifecycleSystemTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _baseUrl = "https://localhost:7035";
    
    public PersonLifecycleSystemTests()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _client = new HttpClient(handler) { BaseAddress = new Uri(_baseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    public async Task CreatePerson_WithActiveStatus_CanAuthenticate()
    {
        // Arrange
        await LoginAsAdminAsync();
        var firstName = $"Active_{DateTime.UtcNow.Ticks}";
        
        // Act
        var person = await CreatePersonAsync(firstName, "Active");
        
        // Assert
        Assert.NotNull(person);
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.True(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithSuspendedStatus_CannotAuthenticate()
    {
        await LoginAsAdminAsync();
        var firstName = $"Suspended_{DateTime.UtcNow.Ticks}";
        
        var person = await CreatePersonAsync(firstName, "Suspended");
        
        Assert.Equal("Suspended", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithPendingStatus_CannotAuthenticate()
    {
        await LoginAsAdminAsync();
        var firstName = $"Pending_{DateTime.UtcNow.Ticks}";
        
        var person = await CreatePersonAsync(firstName, "Pending");
        
        Assert.Equal("Pending", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithTerminatedStatus_CannotAuthenticate()
    {
        await LoginAsAdminAsync();
        var firstName = $"Terminated_{DateTime.UtcNow.Ticks}";
        
        var person = await CreatePersonAsync(firstName, "Terminated");
        
        Assert.Equal("Terminated", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_ActiveWithFutureStartDate_CannotAuthenticate()
    {
        await LoginAsAdminAsync();
        var firstName = $"FutureStart_{DateTime.UtcNow.Ticks}";
        var futureDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        
        var person = await CreatePersonAsync(firstName, "Active", startDate: futureDate);
        
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_ActiveWithPastEndDate_CannotAuthenticate()
    {
        await LoginAsAdminAsync();
        var firstName = $"PastEnd_{DateTime.UtcNow.Ticks}";
        var pastDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        
        var person = await CreatePersonAsync(firstName, "Active", endDate: pastDate);
        
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromActiveToSuspended_Works()
    {
        await LoginAsAdminAsync();
        var firstName = $"ToSuspend_{DateTime.UtcNow.Ticks}";
        
        // Create Active person
        var person = await CreatePersonAsync(firstName, "Active");
        var personId = person.GetProperty("id").GetString();
        Assert.True(person.GetProperty("canAuthenticate").GetBoolean());

        // Update to Suspended
        var updatePayload = new
        {
            firstName,
            lastName = "User",
            status = "Suspended"
        };
        
        var updateResponse = await _client.PutAsJsonAsync($"/api/admin/people/{personId}", updatePayload);
        Assert.True(updateResponse.IsSuccessStatusCode, await updateResponse.Content.ReadAsStringAsync());

        // Verify
        var getResponse = await _client.GetAsync($"/api/admin/people/{personId}");
        var updated = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        
        Assert.Equal("Suspended", updated.GetProperty("status").GetString());
        Assert.False(updated.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromSuspendedToActive_Works()
    {
        await LoginAsAdminAsync();
        var firstName = $"ToActivate_{DateTime.UtcNow.Ticks}";
        
        var person = await CreatePersonAsync(firstName, "Suspended");
        var personId = person.GetProperty("id").GetString();
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());

        var updatePayload = new
        {
            firstName,
            lastName = "User",
            status = "Active"
        };
        
        var updateResponse = await _client.PutAsJsonAsync($"/api/admin/people/{personId}", updatePayload);
        Assert.True(updateResponse.IsSuccessStatusCode);

        var getResponse = await _client.GetAsync($"/api/admin/people/{personId}");
        var updated = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        
        Assert.Equal("Active", updated.GetProperty("status").GetString());
        Assert.True(updated.GetProperty("canAuthenticate").GetBoolean());
    }

    private async Task LoginAsAdminAsync()
    {
        // Get login page to get anti-forgery token
        var loginPage = await _client.GetAsync("/Account/Login");
        var html = await loginPage.Content.ReadAsStringAsync();
        
        // Extract anti-forgery token
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(
            html, 
            @"name=""__RequestVerificationToken""\s+value=""([^""]+)"""
        );
        var token = tokenMatch.Success ? tokenMatch.Groups[1].Value : "";
        
        // Login
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Login"] = "admin@hybridauth.local",
            ["Input.Password"] = "Admin@123",
            ["__RequestVerificationToken"] = token
        });
        
        var loginResponse = await _client.PostAsync("/Account/Login", loginContent);
        
        // Should redirect on success
        Assert.True(loginResponse.StatusCode == HttpStatusCode.OK || 
                    loginResponse.StatusCode == HttpStatusCode.Redirect ||
                    loginResponse.StatusCode == HttpStatusCode.Found,
                    $"Login failed: {loginResponse.StatusCode}");
    }

    private async Task<JsonElement> CreatePersonAsync(
        string firstName, 
        string status, 
        string? startDate = null, 
        string? endDate = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = firstName,
            ["lastName"] = "User",
            ["status"] = status,
            ["department"] = "Test Department",
            ["jobTitle"] = "Test Title",
            ["startDate"] = startDate,
            ["endDate"] = endDate
        };
        
        var response = await _client.PostAsJsonAsync("/api/admin/people", payload);
        var content = await response.Content.ReadAsStringAsync();
        
        Assert.True(response.IsSuccessStatusCode, $"Create failed: {response.StatusCode} - {content}");
        
        return JsonDocument.Parse(content).RootElement;
    }
}
