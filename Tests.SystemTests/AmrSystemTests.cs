using Microsoft.IdentityModel.JsonWebTokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OtpNet;
using Xunit;

namespace Tests.SystemTests;

[Collection("MFA Tests")]
public class AmrSystemTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    
    private const string TEST_USER_EMAIL = "admin@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Admin@123";

    public AmrSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        
        // Ensure MFA is disabled initially
        try {
            var token = await GetPasswordTokenAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = TEST_USER_PASSWORD });
            await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
            _httpClient.DefaultRequestHeaders.Authorization = null;
        } catch { /* Ignore cleanup errors */ }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TokenRequest_PasswordGrant_ReturnsAmrPwd()
    {
        // Act
        var tokenResponse = await GetTokenResponseAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
        
        // Assert
        var idToken = tokenResponse.GetProperty("id_token").GetString()!;
        var handler = new JsonWebTokenHandler();
        var jwtToken = handler.ReadJsonWebToken(idToken);
        
        var amrValues = jwtToken.Claims.Where(c => c.Type == "amr").Select(c => c.Value).ToList();
        if (!amrValues.Contains("pwd"))
        {
            var allClaims = string.Join(", ", jwtToken.Claims.Select(c => $"{c.Type}={c.Value}"));
            throw new Exception($"'pwd' not found in amr. All claims: {allClaims}");
        }
        Assert.Contains("pwd", amrValues);
    }

    [Fact]
    public async Task AuthorizeRequest_WithAcrMfa_NoMfaDone_RedirectsToLogin()
    {
        // Act
        // /connect/authorize?client_id=...&response_type=code&scope=openid&acr_values=mfa
        var redirectUri = WebUtility.UrlEncode("https://localhost:7035/signin-oidc");
        // Add PKCE parameters as they are required by the server configuration
        var url = $"/connect/authorize?client_id=testclient-public&redirect_uri={redirectUri}&response_type=code&scope=openid&acr_values=mfa&code_challenge=xyz&code_challenge_method=S256&nonce=abc&state=123";
        var response = await _httpClient.GetAsync(url);
        
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Authorize request failed: {body}");
        }
        
        // Assert
        // Should be a 302 to /Account/Login
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        var decodedLocation = WebUtility.UrlDecode(location!);
        if (!decodedLocation.Contains("prompt=login"))
        {
            throw new Exception($"Redirect to login missing prompt=login. Location: {location}. Decoded: {decodedLocation}");
        }
        Assert.Contains("prompt=login", decodedLocation);
    }

    private async Task<string> GetPasswordTokenAsync(string username, string password)
    {
        var response = await GetTokenResponseAsync(username, password);
        return response.GetProperty("access_token").GetString()!;
    }

    private async Task<JsonElement> GetTokenResponseAsync(string username, string password)
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "testclient-public",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid profile"
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token request failed: {response.StatusCode} - {error}");
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
