using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests.SystemTests;

[Collection("SystemTests")]
public class PersonLifecycleSystemTests 
{
    private readonly HttpClient _client;
    private readonly WebIdPServerFixture _serverFixture;

    public PersonLifecycleSystemTests(WebIdPServerFixture fixture)
    {
        _serverFixture = fixture;
        // Construct client manually as we removed the wrapper fixture
        // Or reuse a shared client from fixture? 
        // WebIdPServerFixture doesn't expose Client.
        // We create one here.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };
        _client = new HttpClient(handler) 
        { 
            BaseAddress = new Uri(fixture.BaseUrl),
            Timeout = TimeSpan.FromSeconds(10) 
        };
    }
    
    // InitializeAsync not strictly needed if we just call Login in the test or helper
    // But LoginAsAdmin is needed.
    // We can call it in constructor or helper.
    // If the server is already running (Collection scope), we can just log in.
    
    private async Task EnsureLoggedInAsync()
    {
         await LoginAsAdminAsync();
    }


    [Fact]
    public async Task CreatePerson_WithActiveStatus_CanAuthenticate()
    {
        var firstName = $"Active_{DateTime.UtcNow.Ticks}";
        var person = await CreatePersonAsync(firstName, "Active");
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.True(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithSuspendedStatus_CannotAuthenticate()
    {
        var firstName = $"Suspended_{DateTime.UtcNow.Ticks}";
        var person = await CreatePersonAsync(firstName, "Suspended");
        Assert.Equal("Suspended", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithPendingStatus_CannotAuthenticate()
    {
        var firstName = $"Pending_{DateTime.UtcNow.Ticks}";
        var person = await CreatePersonAsync(firstName, "Pending");
        Assert.Equal("Pending", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_WithTerminatedStatus_CannotAuthenticate()
    {
        var firstName = $"Terminated_{DateTime.UtcNow.Ticks}";
        var person = await CreatePersonAsync(firstName, "Terminated");
        Assert.Equal("Terminated", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_ActiveWithFutureStartDate_CannotAuthenticate()
    {
        var firstName = $"FutureStart_{DateTime.UtcNow.Ticks}";
        var futureDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var person = await CreatePersonAsync(firstName, "Active", startDate: futureDate);
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task CreatePerson_ActiveWithPastEndDate_CannotAuthenticate()
    {
        var firstName = $"PastEnd_{DateTime.UtcNow.Ticks}";
        var pastDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var person = await CreatePersonAsync(firstName, "Active", endDate: pastDate);
        Assert.Equal("Active", person.GetProperty("status").GetString());
        Assert.False(person.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromActiveToSuspended_Works()
    {
        var firstName = $"ToSuspend_{DateTime.UtcNow.Ticks}";
        var person = await CreatePersonAsync(firstName, "Active");
        var personId = person.GetProperty("id").GetString();
        
        var updateResponse = await _client.PutAsJsonAsync($"/api/admin/people/{personId}", new { 
            firstName, lastName = "User", status = "Suspended" 
        });
        Assert.True(updateResponse.IsSuccessStatusCode);

        var getResponse = await _client.GetAsync($"/api/admin/people/{personId}");
        var updated = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        
        Assert.Equal("Suspended", updated.GetProperty("status").GetString());
        Assert.False(updated.GetProperty("canAuthenticate").GetBoolean());
    }

    [Fact]
    public async Task UpdatePerson_ChangeStatusFromSuspendedToActive_Works()
    {
        var firstName = $"ToActivate_{DateTime.UtcNow.Ticks}";
        var person = await CreatePersonAsync(firstName, "Suspended");
        var personId = person.GetProperty("id").GetString();
        
        var updateResponse = await _client.PutAsJsonAsync($"/api/admin/people/{personId}", new { 
            firstName, lastName = "User", status = "Active" 
        });
        Assert.True(updateResponse.IsSuccessStatusCode);

        var getResponse = await _client.GetAsync($"/api/admin/people/{personId}");
        var updated = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        
        Assert.Equal("Active", updated.GetProperty("status").GetString());
        Assert.True(updated.GetProperty("canAuthenticate").GetBoolean());
    }

    private async Task<JsonElement> CreatePersonAsync(string firstName, string status, string? startDate = null, string? endDate = null)
    {
        await EnsureLoggedInAsync();

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
        Assert.True(response.IsSuccessStatusCode, $"Create failed: {response.StatusCode}");
        return JsonDocument.Parse(content).RootElement;
    }

    private async Task LoginAsAdminAsync()
    {
        var loginPage = await _client.GetAsync("/Account/Login");
        var html = await loginPage.Content.ReadAsStringAsync();
        
        if (!loginPage.IsSuccessStatusCode)
            throw new Exception($"Failed to load login page. Status: {loginPage.StatusCode}");

        var tokenMatch = Regex.Match(html, @"input[^>]+name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        if (!tokenMatch.Success) 
            tokenMatch = Regex.Match(html, @"input[^>]+value=""([^""]+)""[^>]*name=""__RequestVerificationToken""");

        if (!tokenMatch.Success)
            throw new Exception("Anti-forgery token not found.");

        var token = tokenMatch.Groups[1].Value;
        
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Login"] = "admin@hybridauth.local",
            ["Input.Password"] = "Admin@123",
            ["__RequestVerificationToken"] = token,
            ["Input.RememberMe"] = "true" // Keep session alive
        });
        
        var loginResponse = await _client.PostAsync("/Account/Login", loginContent);
        
        if (loginResponse.StatusCode == HttpStatusCode.OK)
        {
             var content = await loginResponse.Content.ReadAsStringAsync();
             if (content.Contains("Input.Login"))
                 throw new Exception("Login failed.");
        }
    }
}
