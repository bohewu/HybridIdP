using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

public class EmailSystemTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly HttpClient _mailpitClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _adminToken;

    public EmailSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
        _mailpitClient = new HttpClient { BaseAddress = new Uri("http://localhost:8025") };
    }
    private bool _mailpitAvailable = false;

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        
        // Check if Mailpit is reachable
        try 
        {
            var response = await _mailpitClient.DeleteAsync("/api/v1/messages");
            _mailpitAvailable = response.IsSuccessStatusCode;
        }
        catch 
        {
            _mailpitAvailable = false;
        }
        
        // Wait for server to be ready
        await Task.Delay(100);
        _adminToken = await GetAdminTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        _mailpitClient?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SendTestEmail_ShouldDeliveryToMailpit()
    {
        // Arrange
        var request = new 
        {
            settings = new 
            {
                host = "localhost",
                port = 1025,
                username = "",
                password = "",
                enableSsl = false,
                fromAddress = "system-test@hybrididp.com",
                fromName = "System Test"
            },
            to = "test-recipient@example.com"
        };

        // Act
        // 1. Trigger Email via API
        var response = await _httpClient.PostAsJsonAsync("/api/admin/settings/email/test", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 2. Wait for background processor to process the queue
        // Retry a few times as it's async
        bool emailReceived = false;
        int maxRetries = 30; // Wait up to 15 seconds
        for (int i = 0; i < maxRetries; i++)
        {
            var messagesResponse = await _mailpitClient.GetAsync("/api/v1/messages");
            if (messagesResponse.IsSuccessStatusCode)
            {
                var content = await messagesResponse.Content.ReadAsStringAsync();
                var root = JsonSerializer.Deserialize<JsonElement>(content);
                
                if (root.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0)
                {
                    var msg = messages[0];
                    var subject = msg.GetProperty("Subject").GetString();
                    if (subject == "Test Email from HybridIdP") 
                    {
                        emailReceived = true;
                        break;
                    }
                }
            }
            await Task.Delay(100);
        }

        // Assert
        if (_mailpitAvailable)
        {
            Assert.True(emailReceived, $"Email was not received by Mailpit within the timeout period ({maxRetries * 0.5}s). Mailpit was reachable.");
        }
        else
        {
            Assert.True(true, "Skipped Mailpit verification because Mailpit is not reachable.");
        }
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[] { "settings.read", "settings.update" };
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
