using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Core.Domain.Constants;
using Xunit;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public class ConsentPageSystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    private const string ClientId = "testclient-public";
    private string RedirectUri => _serverFixture.BaseUrl + "/signin-oidc";
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
    public async Task ConsentPage_PreventsFramingWithoutChangingBackchannelErrors()
    {
        var (codeChallenge, _) = GeneratePkce();
        var authorizeUrl = $"/CoNnEcT/AuThOrIzE?client_id={ClientId}&redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&response_type=code&scope={HttpUtility.UrlEncode(DefaultScopes)}&prompt=consent&code_challenge={codeChallenge}&code_challenge_method=S256";

        using var consentResponse = await _httpClient.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.OK, consentResponse.StatusCode);
        Assert.Equal("text/html", consentResponse.Content.Headers.ContentType?.MediaType);
        Assert.True(
            consentResponse.Headers.TryGetValues(
                "Content-Security-Policy",
                out var contentSecurityPolicies),
            "The interactive consent page must declare a frame-ancestors policy.");
        Assert.Contains(
            "frame-ancestors 'none'",
            string.Join("; ", contentSecurityPolicies),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "DENY",
            Assert.Single(consentResponse.Headers.GetValues("X-Frame-Options")));

        var consentHtml = await consentResponse.Content.ReadAsStringAsync();
        var denyFields = ExtractHiddenInputs(consentHtml);
        denyFields.Add(new KeyValuePair<string, string>("submit", "deny"));
        using var redirectResponse = await _httpClient.PostAsync(
            "/connect/authorize",
            new FormUrlEncodedContent(denyFields));
        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        Assert.False(redirectResponse.Headers.Contains("Content-Security-Policy"));
        Assert.False(redirectResponse.Headers.Contains("X-Frame-Options"));

        using var tokenResponse = await _httpClient.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, tokenResponse.StatusCode);
        Assert.Equal("application/json", tokenResponse.Content.Headers.ContentType?.MediaType);
        Assert.False(tokenResponse.Headers.Contains("Content-Security-Policy"));
        Assert.False(tokenResponse.Headers.Contains("X-Frame-Options"));
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
    public async Task Consent_Allow_RequiresAntiforgeryAndPreservesValidSubmission()
    {
        var (codeChallenge, _) = GeneratePkce();
        var authorizeUrl = $"/connect/authorize?client_id={ClientId}&redirect_uri={HttpUtility.UrlEncode(RedirectUri)}&response_type=code&scope={HttpUtility.UrlEncode(DefaultScopes)}&prompt=consent&code_challenge={codeChallenge}&code_challenge_method=S256";

        using var getResponse = await _httpClient.GetAsync(authorizeUrl);
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        var validFields = ExtractHiddenInputs(html);
        validFields.Add(new KeyValuePair<string, string>("submit", "allow"));
        foreach (var scope in DefaultScopes.Split(' '))
        {
            validFields.Add(new KeyValuePair<string, string>("granted_scopes", scope));
        }

        var missingTokenFields = validFields
            .Where(field => field.Key != "__RequestVerificationToken")
            .ToList();
        using (var missingTokenResponse = await _httpClient.PostAsync(
                   "/connect/authorize",
                   new FormUrlEncodedContent(missingTokenFields)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);
            Assert.Null(missingTokenResponse.Headers.Location);
        }

        var invalidTokenFields = validFields
            .Where(field => field.Key != "__RequestVerificationToken")
            .Append(new KeyValuePair<string, string>(
                "__RequestVerificationToken",
                "invalid-antiforgery-token"));
        using (var invalidTokenResponse = await _httpClient.PostAsync(
                   "/connect/authorize",
                   new FormUrlEncodedContent(invalidTokenFields)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidTokenResponse.StatusCode);
            Assert.Null(invalidTokenResponse.Headers.Location);
        }

        var missingIntentFields = validFields
            .Where(field => field.Key != "consent_intent")
            .ToList();
        using (var missingIntentResponse = await _httpClient.PostAsync(
                   "/connect/authorize",
                   new FormUrlEncodedContent(missingIntentFields)))
        {
            await AssertInvalidConsentIntentAsync(missingIntentResponse);
        }

        var unknownIntentFields = validFields
            .Where(field => field.Key != "consent_intent")
            .Append(new KeyValuePair<string, string>(
                "consent_intent",
                "unknown-consent-intent"));
        using (var unknownIntentResponse = await _httpClient.PostAsync(
                   "/connect/authorize",
                   new FormUrlEncodedContent(unknownIntentFields)))
        {
            await AssertInvalidConsentIntentAsync(unknownIntentResponse);
        }

        using var validResponse = await _httpClient.PostAsync(
            "/connect/authorize",
            new FormUrlEncodedContent(validFields));
        var validResponseBody = await validResponse.Content.ReadAsStringAsync();
        Assert.True(
            validResponse.StatusCode == HttpStatusCode.Redirect,
            $"Expected consent redirect but received {validResponse.StatusCode}: {validResponseBody}");
        Assert.NotNull(validResponse.Headers.Location);
        Assert.True(
            validResponse.Headers.Location.ToString().StartsWith(
                RedirectUri,
                StringComparison.Ordinal),
            "Expected the consent response to redirect to the registered URI.");
        var redirectParameters = HttpUtility.ParseQueryString(
            validResponse.Headers.Location.Query);
        Assert.True(
            !string.IsNullOrWhiteSpace(redirectParameters["code"]),
            "Expected the consent redirect to contain an authorization code.");
        Assert.Null(redirectParameters["error"]);

        using var replayResponse = await _httpClient.PostAsync(
            "/connect/authorize",
            new FormUrlEncodedContent(validFields));
        await AssertInvalidConsentIntentAsync(replayResponse);
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

        // 2. Post Credentials - Using multitest@hybridauth.local which is now guaranteed clean in seeder
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Login", "consent@hybridauth.local"),
            new KeyValuePair<string, string>("Input.Password", "Consent@123"),
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

        var hiddenInputs = Regex.Matches(html, @"<input[^>]*type=""hidden""[^>]*>");
        foreach (Match hiddenInput in hiddenInputs)
        {
            var name = Regex.Match(hiddenInput.Value, @"name=""([^""]+)""").Groups[1].Value;
            var value = Regex.Match(hiddenInput.Value, @"value=""([^""]*)""").Groups[1].Value;
            if (!string.IsNullOrEmpty(name))
            {
                inputs.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        return inputs.Distinct().ToList();
    }

    private static async Task AssertInvalidConsentIntentAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "invalid_consent_intent",
            payload.RootElement.GetProperty("error").GetString());
    }

    #endregion
}
