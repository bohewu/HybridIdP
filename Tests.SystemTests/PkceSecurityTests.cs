using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Tests for PKCE (Proof Key for Code Exchange) validation.
/// Covers edge cases where PKCE should fail to protect against authorization code interception.
/// </summary>
[Trait("Category", "Quick")]
public class PkceSecurityTests : IClassFixture<WebIdPServerFixture>, IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    private const string ClientId = "testclient-public";
    private const string RedirectUri = "https://localhost:7001/signin-oidc";
    private const string DefaultScopes = "openid profile email";

    public PkceSecurityTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = _cookieContainer
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        await LoginAsStandardUserAsync();
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that token exchange fails when using an incorrect code_verifier.
    /// This is the core security property of PKCE.
    /// </summary>
    [Fact]
    public async Task TokenExchange_WithWrongCodeVerifier_ShouldFail()
    {
        // Arrange
        var (codeChallenge, correctVerifier) = GeneratePkce();
        var wrongVerifier = "completely_wrong_verifier_that_does_not_match"; // Attacker's guess
        
        // Step 1: Get authorization code using correct challenge
        var authCode = await GetAuthorizationCodeWithConsentAsync(codeChallenge);
        Assert.NotNull(authCode);
        
        // Step 2: Try to exchange code with WRONG verifier (attacker scenario)
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = authCode,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = wrongVerifier // WRONG!
        });
        
        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        
        // Assert: Token exchange should fail
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_grant", content.ToLower());
    }

    /// <summary>
    /// Verifies that token exchange fails when code_verifier is omitted.
    /// </summary>
    [Fact]
    public async Task TokenExchange_WithMissingCodeVerifier_ShouldFail()
    {
        // Arrange
        var (codeChallenge, _) = GeneratePkce();
        
        // Step 1: Get authorization code
        var authCode = await GetAuthorizationCodeWithConsentAsync(codeChallenge);
        Assert.NotNull(authCode);
        
        // Step 2: Try to exchange code WITHOUT verifier
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = authCode,
            ["redirect_uri"] = RedirectUri
            // code_verifier is MISSING
        });
        
        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        
        // Assert: Token exchange should fail
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // OpenIddict returns invalid_request when verifier is missing
        Assert.True(
            content.Contains("invalid_grant") || content.Contains("invalid_request"),
            $"Expected invalid_grant or invalid_request but got: {content}");
    }

    /// <summary>
    /// Verifies that token exchange succeeds with correct code_verifier.
    /// This is the control case to ensure our test setup is correct.
    /// </summary>
    [Fact]
    public async Task TokenExchange_WithCorrectCodeVerifier_ShouldSucceed()
    {
        // Arrange
        var (codeChallenge, correctVerifier) = GeneratePkce();
        
        // Step 1: Get authorization code
        var authCode = await GetAuthorizationCodeWithConsentAsync(codeChallenge);
        Assert.NotNull(authCode);
        
        // Step 2: Exchange code with CORRECT verifier
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = authCode,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = correctVerifier // Correct!
        });
        
        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        
        // Assert: Token exchange should succeed
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("access_token", content);
    }

    #region Helpers

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

    private async Task<string?> GetAuthorizationCodeWithConsentAsync(string codeChallenge)
    {
        var authorizeUrl = $"/connect/authorize?client_id={ClientId}&redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&response_type=code&scope={HttpUtility.UrlEncode(DefaultScopes)}&prompt=consent&code_challenge={codeChallenge}&code_challenge_method=S256";
        
        // Get consent page
        var getResponse = await _httpClient.GetAsync(authorizeUrl);
        if (!getResponse.IsSuccessStatusCode)
        {
            var errorContent = await getResponse.Content.ReadAsStringAsync();
            throw new Exception($"Failed to get consent page: {getResponse.StatusCode}. Content: {errorContent}");
        }
        
        var html = await getResponse.Content.ReadAsStringAsync();
        
        // Submit consent (allow)
        var formData = ExtractHiddenInputs(html);
        formData.Add(new KeyValuePair<string, string>("submit", "allow"));
        foreach (var scope in DefaultScopes.Split(' '))
        {
            formData.Add(new KeyValuePair<string, string>("granted_scopes", scope));
        }

        var content = new FormUrlEncodedContent(formData);
        var response = await _httpClient.PostAsync(authorizeUrl, content);
        
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Expected redirect after consent but got {response.StatusCode}. Content: {responseContent}");
        }
        
        var location = response.Headers.Location?.ToString();
        if (location == null) return null;
        
        // Extract code from redirect URL
        var uri = new Uri(location);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        return queryParams["code"];
    }

    private async Task LoginAsStandardUserAsync()
    {
        var response = await _httpClient.GetAsync("/Account/Login");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", "multitest@hybridauth.local"),
            new KeyValuePair<string, string>("Input.Password", "MultiTest@123"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        var loginResponse = await _httpClient.PostAsync("/Account/Login", formData);
        
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

        return inputs.Distinct().ToList();
    }

    #endregion
}
