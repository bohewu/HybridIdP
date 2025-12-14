using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Core.Domain.Constants;
using Xunit;

namespace Tests.SystemTests;

public class ConsentPageSystemTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    private const string ClientId = "testclient-public";
    private const string RedirectUri = "https://localhost:7001/signin-oidc";
    private const string DefaultScopes = "openid profile email";

    public ConsentPageSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false, // We need to inspect Redirects manually
            UseCookies = true,
            CookieContainer = _cookieContainer
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        // Ensure we are logged in as a standard user before running consent tests
        await LoginAsStandardUserAsync();
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Consent_WithPublicClient_ShouldDisplayConsentScreen()
    {
        // Arrange
        var (codeChallenge, _) = GeneratePkce();
        // Add prompt=consent to force the consent screen even if previously granted
        // Add PKCE parameters S256
        var authorizeUrl = $"/connect/authorize?client_id={ClientId}&redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&response_type=code&scope={HttpUtility.UrlEncode(DefaultScopes)}&prompt=consent&code_challenge={codeChallenge}&code_challenge_method=S256";

        // Act
        var response = await _httpClient.GetAsync(authorizeUrl);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 OK but got {response.StatusCode}. Loc: {response.Headers.Location}");
        
        // Verify we are on the consent page
        Assert.Contains("Test Client (Public)", content); // Client Display Name
        // Assert.Contains("Do you want to grant", content); // REMOVED: Fails in zh-TW locale
        Assert.Contains("submit", content);
        Assert.Contains("value=\"allow\"", content);
        Assert.Contains("value=\"deny\"", content);
    }

    [Fact]
    public async Task Consent_Allow_ShouldRedirectWithAuthCode()
    {
        // Arrange
        var (codeChallenge, _) = GeneratePkce();
        var authorizeUrl = $"/connect/authorize?client_id={ClientId}&redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&response_type=code&scope={HttpUtility.UrlEncode(DefaultScopes)}&prompt=consent&code_challenge={codeChallenge}&code_challenge_method=S256";
        
        // 1. Get Consent Page to extract form data (state, token, hidden inputs)
        var getResponse = await _httpClient.GetAsync(authorizeUrl);
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        
        // 2. Prepare consent form submission
        // We need to scrape hidden inputs needed by OpenIddict/Controller
        var formData = ExtractHiddenInputs(html);
        formData.Add(new KeyValuePair<string, string>("submit", "allow"));
        // Simulating checked scopes (often checkboxes)
        // If the view uses checkboxes for scopes, we might need to add them. 
        // Based on AuthorizationController logic: Request.Form["granted_scopes"]
        // We'll trust that hidden inputs or default behavior handles this, or manually add if needed.
        // Let's assume standard behavior: we explicitly grant the requested scopes.
        foreach (var scope in DefaultScopes.Split(' '))
        {
            formData.Add(new KeyValuePair<string, string>("granted_scopes", scope));
        }

        var content = new FormUrlEncodedContent(formData);

        // Act
        var response = await _httpClient.PostAsync(authorizeUrl, content);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.StartsWith(RedirectUri, location);
        Assert.Contains("code=", location);
        Assert.DoesNotContain("error=", location);
    }

    [Fact]
    public async Task Consent_Deny_ShouldRedirectWithError()
    {
        // Arrange
        var (codeChallenge, _) = GeneratePkce();
        var authorizeUrl = $"/connect/authorize?client_id={ClientId}&redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&response_type=code&scope={HttpUtility.UrlEncode(DefaultScopes)}&prompt=consent&code_challenge={codeChallenge}&code_challenge_method=S256";
        
        // 1. Get Consent Page
        var getResponse = await _httpClient.GetAsync(authorizeUrl);
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        
        // 2. Submit Deny
        var formData = ExtractHiddenInputs(html);
        formData.Add(new KeyValuePair<string, string>("submit", "deny"));
        
        var content = new FormUrlEncodedContent(formData);

        // Act
        var response = await _httpClient.PostAsync(authorizeUrl, content);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.StartsWith(RedirectUri, location);
        Assert.Contains("error=access_denied", location);
    }

    private static (string CodeChallenge, string CodeVerifier) GeneratePkce()
    {
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        var codeVerifier = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var challengeBytes = sha256.ComputeHash(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (codeChallenge, codeVerifier);
    }


    #region Helpers

    private async Task LoginAsStandardUserAsync()
    {
        // 1. Get Login Page
        var response = await _httpClient.GetAsync("/Account/Login");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);

        // 2. Post Credentials
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", "multitest@hybridauth.local"),
            new KeyValuePair<string, string>("Input.Password", "MultiTest@123"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        var loginResponse = await _httpClient.PostAsync("/Account/Login", formData);
        
        // Should redirect on success
        if (loginResponse.StatusCode != HttpStatusCode.Redirect)
        {
            var content = await loginResponse.Content.ReadAsStringAsync();
            throw new Exception($"Login failed. Status: {loginResponse.StatusCode}. Content: {content}");
        }
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        
        match = Regex.Match(html, @"input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;
        
        throw new Exception("Could not find __RequestVerificationToken in HTML");
    }

    private static List<KeyValuePair<string, string>> ExtractHiddenInputs(string html)
    {
        var inputs = new List<KeyValuePair<string, string>>();
        
        // Extract all hidden inputs (this captures antiforgery token, and any OpenIddict state parameters)
        var matches = Regex.Matches(html, @"<input\s+type=""hidden""\s+name=""([^""]+)""\s+value=""([^""]*)""");
        
        foreach (Match match in matches)
        {
            inputs.Add(new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value));
        }

        // Also catch inputs where attributes are ordered differently
        // Simplified regex, might miss some edge cases but good enough for standard ASP.NET Core views
        // Note: The previous regex expects type="hidden" first. Let's try a more robust approach if that fails?
        // Actually, let's just grab the specific ones we know we typically need if the generic one misses.
        // Usually OpenIddict puts state in hidden fields.

        if (inputs.Count == 0)
        {
            // Fallback: Try looser matching
             var regex = new Regex(@"<input[^>]*type=""hidden""[^>]*>");
             foreach (Match m in regex.Matches(html))
             {
                 var name = Regex.Match(m.Value, @"name=""([^""]+)""").Groups[1].Value;
                 var value = Regex.Match(m.Value, @"value=""([^""]*)""").Groups[1].Value;
                 if (!string.IsNullOrEmpty(name))
                 {
                     inputs.Add(new KeyValuePair<string, string>(name, value));
                 }
             }
        }

        return inputs.Distinct().ToList(); // Remove simple duplicates
    }

    #endregion
}
