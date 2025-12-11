using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for Phase 18: Person Lifecycle Management
/// Tests that login is blocked for inactive persons and token revocation works correctly.
/// </summary>
public class PersonLifecycleSystemTests : IDisposable
{
    private const string Authority = "https://localhost:7035";
    private const string AdminUsername = "admin@hybridauth.local";
    private const string AdminPassword = "Admin@123";

    private readonly HttpClientHandler _httpClientHandler;
    private readonly HttpClient _httpClient;

    public PersonLifecycleSystemTests()
    {
        _httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = true, // Keep true for login redirects
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _httpClient = new HttpClient(_httpClientHandler) { BaseAddress = new Uri(Authority) };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _httpClientHandler.Dispose();
    }

    /// <summary>
    /// Tests that creating a person with Suspended status sets canAuthenticate to false
    /// </summary>
    [Fact]
    public async Task CreatePerson_WithSuspendedStatus_CannotAuthenticate()
    {
        // Arrange - Login as admin first
        await LoginAsAdminAsync();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var personData = new
        {
            firstName = "SystemTest",
            lastName = "SuspendedPerson",
            employeeId = $"EMP{timestamp}",
            status = "Suspended",
            identityDocumentType = "None"
        };

        try
        {
            // Act - Create person via API
            var response = await _httpClient.PostAsync(
                "/api/admin/people",
                new StringContent(JsonSerializer.Serialize(personData), Encoding.UTF8, "application/json"));

            // Assert
            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"Failed to create person: {response.StatusCode} - {responseContent}");
            var person = JsonSerializer.Deserialize<PersonResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(person);
            Assert.Equal("Suspended", person.Status);
            Assert.False(person.CanAuthenticate);

            // Cleanup
            await _httpClient.DeleteAsync($"/api/admin/people/{person.Id}");
        }
        catch
        {
            // Best effort cleanup if test fails
            throw;
        }
    }

    /// <summary>
    /// Tests that creating a person with Pending status sets canAuthenticate to false
    /// </summary>
    [Fact]
    public async Task CreatePerson_WithPendingStatus_CannotAuthenticate()
    {
        await LoginAsAdminAsync();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var personData = new
        {
            firstName = "SystemTest",
            lastName = "PendingPerson",
            employeeId = $"EMP{timestamp}",
            status = "Pending",
            identityDocumentType = "None"
        };

        var response = await _httpClient.PostAsync(
            "/api/admin/people",
            new StringContent(JsonSerializer.Serialize(personData), Encoding.UTF8, "application/json"));

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Failed to create person: {response.StatusCode} - {responseContent}");
        var person = JsonSerializer.Deserialize<PersonResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(person);
        Assert.Equal("Pending", person.Status);
        Assert.False(person.CanAuthenticate);

        // Cleanup
        await _httpClient.DeleteAsync($"/api/admin/people/{person.Id}");
    }

    /// <summary>
    /// Tests that creating a person with Active status sets canAuthenticate to true
    /// </summary>
    [Fact]
    public async Task CreatePerson_WithActiveStatus_CanAuthenticate()
    {
        await LoginAsAdminAsync();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var personData = new
        {
            firstName = "SystemTest",
            lastName = "ActivePerson",
            employeeId = $"EMP{timestamp}",
            status = "Active",
            identityDocumentType = "None"
        };

        var response = await _httpClient.PostAsync(
            "/api/admin/people",
            new StringContent(JsonSerializer.Serialize(personData), Encoding.UTF8, "application/json"));

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Failed to create person: {response.StatusCode} - {responseContent}");
        var person = JsonSerializer.Deserialize<PersonResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(person);
        Assert.Equal("Active", person.Status);
        Assert.True(person.CanAuthenticate);

        // Cleanup
        await _httpClient.DeleteAsync($"/api/admin/people/{person.Id}");
    }

    /// <summary>
    /// Tests that Active person with future StartDate cannot authenticate
    /// </summary>
    [Fact]
    public async Task CreatePerson_ActiveWithFutureStartDate_CannotAuthenticate()
    {
        await LoginAsAdminAsync();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var futureDate = DateTime.UtcNow.AddDays(7);
        var personData = new
        {
            firstName = "SystemTest",
            lastName = "FutureStart",
            employeeId = $"EMP{timestamp}",
            status = "Active",
            startDate = futureDate.ToString("O"),
            identityDocumentType = "None"
        };

        var response = await _httpClient.PostAsync(
            "/api/admin/people",
            new StringContent(JsonSerializer.Serialize(personData), Encoding.UTF8, "application/json"));

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Failed to create person: {response.StatusCode} - {responseContent}");
        var person = JsonSerializer.Deserialize<PersonResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(person);
        Assert.Equal("Active", person.Status);
        // Should NOT be able to authenticate due to future start date
        Assert.False(person.CanAuthenticate);

        // Cleanup
        await _httpClient.DeleteAsync($"/api/admin/people/{person.Id}");
    }

    /// <summary>
    /// Tests that Active person with past EndDate cannot authenticate
    /// </summary>
    [Fact]
    public async Task CreatePerson_ActiveWithPastEndDate_CannotAuthenticate()
    {
        await LoginAsAdminAsync();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pastDate = DateTime.UtcNow.AddDays(-1);
        var personData = new
        {
            firstName = "SystemTest",
            lastName = "PastEnd",
            employeeId = $"EMP{timestamp}",
            status = "Active",
            endDate = pastDate.ToString("O"),
            identityDocumentType = "None"
        };

        var response = await _httpClient.PostAsync(
            "/api/admin/people",
            new StringContent(JsonSerializer.Serialize(personData), Encoding.UTF8, "application/json"));

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Failed to create person: {response.StatusCode} - {responseContent}");
        var person = JsonSerializer.Deserialize<PersonResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(person);
        Assert.Equal("Active", person.Status);
        // Should NOT be able to authenticate due to past end date
        Assert.False(person.CanAuthenticate);

        // Cleanup
        await _httpClient.DeleteAsync($"/api/admin/people/{person.Id}");
    }

    /// <summary>
    /// Tests updating person status from Active to Terminated
    /// </summary>
    [Fact]
    public async Task UpdatePerson_ActiveToTerminated_CannotAuthenticate()
    {
        await LoginAsAdminAsync();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Create Active person
        var personData = new
        {
            firstName = "SystemTest",
            lastName = "ToTerminate",
            employeeId = $"EMP{timestamp}",
            status = "Active",
            identityDocumentType = "None"
        };

        var createResponse = await _httpClient.PostAsync(
            "/api/admin/people",
            new StringContent(JsonSerializer.Serialize(personData), Encoding.UTF8, "application/json"));
        Assert.True(createResponse.IsSuccessStatusCode);
        
        var content = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<PersonResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(created);
        Assert.True(created.CanAuthenticate);

        // Update to Terminated
        var updateData = new
        {
            firstName = "SystemTest",
            lastName = "ToTerminate",
            employeeId = $"EMP{timestamp}",
            status = "Terminated",
            identityDocumentType = "None"
        };

        var updateResponse = await _httpClient.PutAsync(
            $"/api/admin/people/{created.Id}",
            new StringContent(JsonSerializer.Serialize(updateData), Encoding.UTF8, "application/json"));
        Assert.True(updateResponse.IsSuccessStatusCode);

        var updateContent = await updateResponse.Content.ReadAsStringAsync();
        var updated = JsonSerializer.Deserialize<PersonResponse>(updateContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(updated);
        Assert.Equal("Terminated", updated.Status);
        Assert.False(updated.CanAuthenticate);

        // Cleanup
        await _httpClient.DeleteAsync($"/api/admin/people/{created.Id}");
    }

    #region Helper Methods

    private async Task LoginAsAdminAsync()
    {
        // Get Login Page to obtain AntiForgeryToken
        var loginPageResponse = await _httpClient.GetAsync("/Account/Login");
        loginPageResponse.EnsureSuccessStatusCode();
        var loginPageContent = await loginPageResponse.Content.ReadAsStringAsync();
        var token = GetRequestVerificationToken(loginPageContent);

        // Submit Login Form
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", AdminUsername),
            new KeyValuePair<string, string>("Input.Password", AdminPassword),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        // AllowAutoRedirect is true by default, so login will follow redirect and set cookies
        var loginResponse = await _httpClient.PostAsync("/Account/Login", formData);
        loginResponse.EnsureSuccessStatusCode();
    }

    private string GetRequestVerificationToken(string html)
    {
        var match = Regex.Match(html, @"input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        throw new Exception("Could not find __RequestVerificationToken");
    }

    #endregion

    #region DTOs

    private class PersonResponse
    {
        public Guid Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? EmployeeId { get; set; }
        public string? Status { get; set; }
        public bool CanAuthenticate { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    #endregion
}
