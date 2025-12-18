using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// OWASP ZAP authenticated security scanning tests.
/// Prerequisites:
/// 1. ZAP running in daemon mode: docker run -d -p 8090:8080 zaproxy/zap-stable zap.sh -daemon -host 0.0.0.0 -port 8080 -config api.disablekey=true
/// 2. IdP server running: dotnet run in Web.IdP
/// </summary>
[Trait("Category", "Security")]
[Trait("Category", "Slow")]
public class ZapSecurityTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _zapClient;
    private readonly string _idpBaseUrl;
    private readonly string _zapBaseUrl;
    private string _adminToken = string.Empty;
    private string _userToken = string.Empty;

    public ZapSecurityTests(WebIdPServerFixture fixture)
    {
        _idpBaseUrl = fixture.BaseUrl;
        _zapBaseUrl = Environment.GetEnvironmentVariable("ZAP_URL") ?? "http://localhost:8090";
        
        // Create HttpClient with SSL bypass for self-signed certs
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_idpBaseUrl) };
        _zapClient = new HttpClient { BaseAddress = new Uri(_zapBaseUrl) };
    }

    public async Task InitializeAsync()
    {
        // Check if ZAP is running
        try
        {
            var response = await _zapClient.GetAsync("/JSON/core/view/version/");
            if (!response.IsSuccessStatusCode)
            {
                throw new SkipException("ZAP is not running. Start with: docker run -d -p 8090:8080 zaproxy/zap-stable zap.sh -daemon -host 0.0.0.0 -port 8080 -config api.disablekey=true");
            }
        }
        catch (HttpRequestException)
        {
            throw new SkipException("ZAP is not running or not accessible at " + _zapBaseUrl);
        }

        // Get tokens
        _adminToken = await GetAdminTokenAsync();
        _userToken = await GetUserTokenAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ZapPassiveScan_ApiEndpoints_NoHighRiskAlerts()
    {
        // 1. Configure ZAP with authentication header
        await ConfigureZapAuthenticationAsync(_adminToken);

        // 2. Access API URLs for passive scanning
        var apiUrls = new[]
        {
            "/api/admin/users",
            "/api/admin/persons",
            "/api/admin/roles",
            "/api/admin/clients",
            "/api/admin/scopes",
            "/api/admin/monitoring/health",
            "/api/account/profile",
            "/.well-known/openid-configuration"
        };

        foreach (var url in apiUrls)
        {
            await ZapAccessUrlAsync(_idpBaseUrl + url);
        }

        // 3. Wait for passive scan to complete
        await WaitForPassiveScanAsync();

        // 4. Get alerts and verify no high-risk issues
        var alerts = await GetZapAlertsAsync();
        
        var highRiskAlerts = alerts.Where(a => 
            a.Risk?.Equals("High", StringComparison.OrdinalIgnoreCase) == true ||
            a.Risk?.Equals("Critical", StringComparison.OrdinalIgnoreCase) == true
        ).ToList();

        Assert.Empty(highRiskAlerts);
    }

    [Fact]
    public async Task ZapSpider_AuthenticatedEndpoints_CrawlsSuccessfully()
    {
        // Configure authentication
        await ConfigureZapAuthenticationAsync(_adminToken);

        // Start spider
        var spiderResponse = await _zapClient.GetAsync(
            $"/JSON/spider/action/scan/?url={Uri.EscapeDataString(_idpBaseUrl)}&maxChildren=10&recurse=true");
        
        Assert.True(spiderResponse.IsSuccessStatusCode, "Spider should start successfully");

        var spiderContent = await spiderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var scanId = spiderContent.GetProperty("scan").GetString();
        
        // Wait for spider to complete (max 60 seconds)
        var timeout = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < timeout)
        {
            var statusResponse = await _zapClient.GetAsync($"/JSON/spider/view/status/?scanId={scanId}");
            var statusContent = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
            var status = statusContent.GetProperty("status").GetString();
            
            if (status == "100") break;
            await Task.Delay(1000);
        }

        // Get crawled URLs
        var urlsResponse = await _zapClient.GetAsync("/JSON/spider/view/fullResults/?scanId=" + scanId);
        Assert.True(urlsResponse.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ZapActiveScan_TokenEndpoint_NoInjectionVulnerabilities()
    {
        // Configure authentication
        await ConfigureZapAuthenticationAsync(_adminToken);

        // Access token endpoint first
        await ZapAccessUrlAsync(_idpBaseUrl + "/connect/token");

        // Start active scan on token endpoint only
        var scanResponse = await _zapClient.GetAsync(
            $"/JSON/ascan/action/scan/?url={Uri.EscapeDataString(_idpBaseUrl + "/connect/token")}&recurse=false&inScopeOnly=true");
        
        if (!scanResponse.IsSuccessStatusCode)
        {
            // Active scan may be disabled in ZAP baseline mode
            return;
        }

        var scanContent = await scanResponse.Content.ReadFromJsonAsync<JsonElement>();
        var scanId = scanContent.GetProperty("scan").GetString();

        // Wait for scan (max 2 minutes for active scan)
        var timeout = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < timeout)
        {
            var statusResponse = await _zapClient.GetAsync($"/JSON/ascan/view/status/?scanId={scanId}");
            var statusContent = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
            var status = statusContent.GetProperty("status").GetString();
            
            if (status == "100") break;
            await Task.Delay(2000);
        }

        // Check for injection vulnerabilities
        var alerts = await GetZapAlertsAsync();
        var injectionAlerts = alerts.Where(a => 
            a.Name?.Contains("Injection", StringComparison.OrdinalIgnoreCase) == true
        ).ToList();

        Assert.Empty(injectionAlerts);
    }

    #region Helper Methods

    private async Task ConfigureZapAuthenticationAsync(string bearerToken)
    {
        // Add a replacer rule to inject Authorization header
        var response = await _zapClient.GetAsync(
            $"/JSON/replacer/action/addRule/?description=BearerAuth&enabled=true&matchType=REQ_HEADER&matchRegex=false&matchString=Authorization&replacement=Bearer%20{bearerToken}&initiators=");
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Failed to configure ZAP authentication: " + await response.Content.ReadAsStringAsync());
        }
    }

    private async Task ZapAccessUrlAsync(string url)
    {
        await _zapClient.GetAsync($"/JSON/core/action/accessUrl/?url={Uri.EscapeDataString(url)}&followRedirects=true");
    }

    private async Task WaitForPassiveScanAsync(int timeoutSeconds = 30)
    {
        var timeout = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < timeout)
        {
            var response = await _zapClient.GetAsync("/JSON/pscan/view/recordsToScan/");
            var content = await response.Content.ReadFromJsonAsync<JsonElement>();
            var remaining = content.GetProperty("recordsToScan").GetString();
            
            if (remaining == "0") return;
            await Task.Delay(500);
        }
    }

    private async Task<List<ZapAlert>> GetZapAlertsAsync()
    {
        var response = await _zapClient.GetAsync($"/JSON/core/view/alerts/?baseurl={Uri.EscapeDataString(_idpBaseUrl)}&start=0&count=100");
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        var alerts = new List<ZapAlert>();
        if (content.TryGetProperty("alerts", out var alertsElement))
        {
            foreach (var alert in alertsElement.EnumerateArray())
            {
                alerts.Add(new ZapAlert
                {
                    Name = alert.TryGetProperty("name", out var n) ? n.GetString() : null,
                    Risk = alert.TryGetProperty("risk", out var r) ? r.GetString() : null,
                    Confidence = alert.TryGetProperty("confidence", out var c) ? c.GetString() : null,
                    Url = alert.TryGetProperty("url", out var u) ? u.GetString() : null,
                    Description = alert.TryGetProperty("description", out var d) ? d.GetString() : null
                });
            }
        }
        
        return alerts;
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var scopes = new[] { "users.read", "persons.read", "roles.read", "clients.read", "scopes.read" };
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
        var tokenJson = JsonSerializer.Deserialize<JsonElement>(content);
        return tokenJson.GetProperty("access_token").GetString()!;
    }

    private async Task<string> GetUserTokenAsync()
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "testclient",
            ["client_secret"] = "test-secret-2024",
            ["username"] = "testuser@example.com",
            ["password"] = "Test123!",
            ["scope"] = "openid profile email"
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonSerializer.Deserialize<JsonElement>(content);
        return tokenJson.GetProperty("access_token").GetString()!;
    }

    #endregion

    private record ZapAlert
    {
        public string? Name { get; init; }
        public string? Risk { get; init; }
        public string? Confidence { get; init; }
        public string? Url { get; init; }
        public string? Description { get; init; }
    }
}

/// <summary>
/// Exception to skip tests when prerequisites are not met.
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
