using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests.SystemTests;

public class AuthCodeSystemTests
{
    private readonly HttpClient _httpClient;
    private const string Authority = "https://localhost:7035";
    // Using 'testclient-public' which is seeded with explicit consent
    private const string ClientId = "testclient-public"; 
    // Must match the RedirectUris in ClientSeeder exactly
    private const string RedirectUri = "https://localhost:7001/signin-oidc";

    public AuthCodeSystemTests()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false, 
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(Authority) };
    }

    [Fact]
    public async Task AuthCodeFlow_Pkce_Login_Success()
    {
        // 1. Generate PKCE Verifier and Challenge
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);
        var state = Guid.NewGuid().ToString();
        var nonce = Guid.NewGuid().ToString();

        // 2. Start Authorization Request
        // Removed offline_access as it wasn't explicitly seeded for this client's scope permissions
        var authUrl = $"{Authority}/connect/authorize?" +
                      $"response_type=code&" +
                      $"client_id={ClientId}&" +
                      $"redirect_uri={WebUtility.UrlEncode(RedirectUri)}&" +
                      $"scope=openid%20profile%20email%20roles&" +
                      $"state={state}&" +
                      $"nonce={nonce}&" +
                      $"code_challenge={codeChallenge}&" +
                      $"code_challenge_method=S256";

        var authResponse = await _httpClient.GetAsync(authUrl);
        
        // Should redirect to Login page
        Assert.Equal(HttpStatusCode.Redirect, authResponse.StatusCode);
        var loginUrl = authResponse.Headers.Location?.ToString();
        Assert.NotNull(loginUrl);
        if (loginUrl.StartsWith("/")) loginUrl = $"{Authority}{loginUrl}";
        Assert.Contains("/Account/Login", loginUrl);

        // 3. Load Login Page
        var loginPageResponse = await _httpClient.GetAsync(loginUrl);
        loginPageResponse.EnsureSuccessStatusCode();
        var loginPageContent = await loginPageResponse.Content.ReadAsStringAsync();
        var verificationToken = ExtractAntiForgeryToken(loginPageContent);
        
        // 4. Submit Login Form
        // Using "testuser@hybridauth.local" seeded user
        var loginData = new Dictionary<string, string>
        {
            { "Input.Email", "testuser@hybridauth.local" }, 
            { "Input.Password", "Test@123" },
            { "__RequestVerificationToken", verificationToken },
            { "Input.RememberMe", "false" }
        };
        
        // Post to the URL we got (which includes returnUrl)
        var loginPostResponse = await _httpClient.PostAsync(loginUrl, new FormUrlEncodedContent(loginData));

        // 5. Handle Redirects (Login -> Consent -> Callback)
        if (loginPostResponse.StatusCode != HttpStatusCode.Redirect)
        {
            var failedContent = await loginPostResponse.Content.ReadAsStringAsync();
            Assert.Fail($"Login POST failed. Status: {loginPostResponse.StatusCode}. Content fragment: {failedContent.Substring(0, Math.Min(failedContent.Length, 1000))}");
        }

        Assert.Equal(HttpStatusCode.Redirect, loginPostResponse.StatusCode);
        var nextUrl = loginPostResponse.Headers.Location?.ToString();
        if (nextUrl != null && nextUrl.StartsWith("/")) nextUrl = $"{Authority}{nextUrl}";

        string? authCode = null;
        int maxRedirects = 5;
        
        while (maxRedirects-- > 0 && nextUrl != null)
        {
            // If we reached the client redirect URI, we are done
            if (nextUrl.StartsWith(RedirectUri))
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(new Uri(nextUrl).Query);
                authCode = queryParams["code"];
                break;
            }
            
            // Fetch next page
            var response = await _httpClient.GetAsync(nextUrl);
            
            if (response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.SeeOther)
            {
                 var loc = response.Headers.Location?.ToString();
                 if (loc != null)
                 {
                     nextUrl = loc.StartsWith("/") ? $"{Authority}{loc}" : loc;
                     continue;
                 }
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync();
                
                // Check if it's Consent Page
                if (content.Contains("Authorize Application") || content.Contains("is requesting access"))
                {
                     // Extract Token and Hidden Inputs
                     verificationToken = ExtractAntiForgeryToken(content);
                     
                     // We need to resubmit all hidden inputs + 'submit=allow'
                     var consentData = new Dictionary<string, string>();
                     consentData["__RequestVerificationToken"] = verificationToken;
                     consentData["submit"] = "allow";
                     
                     // Extract other hidden inputs (important for OpenIddict flow state)
                     // Simple regex to find <input type="hidden" name="..." value="..." />
                     var hiddenInputs = Regex.Matches(content, @"<input\s+type=""hidden""\s+name=""([^""]+)""\s+value=""([^""]*)""\s*/>");
                     foreach (Match match in hiddenInputs)
                     {
                         if (match.Groups[1].Value != "__RequestVerificationToken")
                         {
                             consentData[match.Groups[1].Value] = match.Groups[2].Value;
                         }
                     }

                     // Post to /connect/authorize
                     // The form action usually points to "Authorize" action.
                     // Assuming it posts to current URL or /connect/authorize
                     
                     var consentPostResponse = await _httpClient.PostAsync($"{Authority}/connect/authorize", new FormUrlEncodedContent(consentData));
                     
                     Assert.Equal(HttpStatusCode.Redirect, consentPostResponse.StatusCode);
                     var consentLoc = consentPostResponse.Headers.Location?.ToString();
                     if (consentLoc != null)
                         nextUrl = consentLoc.StartsWith("/") ? $"{Authority}{consentLoc}" : consentLoc;
                     
                     continue;
                }
            }
            
            // If we get here, valid loop but no redirect or unknown page
            break; 
        }
        
        Assert.NotNull(authCode);

        // 6. Exchange Code for Tokens
        var tokenRequest = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "client_id", ClientId },
            { "code", authCode },
            { "redirect_uri", RedirectUri },
            { "code_verifier", codeVerifier }
        };

        var tokenResponse = await _httpClient.PostAsync("/connect/token", new FormUrlEncodedContent(tokenRequest));
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
        
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        Assert.Contains("access_token", tokenContent);
        Assert.Contains("id_token", tokenContent);
    }

    private string GenerateCodeVerifier()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
        var bytes = new byte[128];
        RandomNumberGenerator.Fill(bytes);
        var result = new StringBuilder(128);
        foreach (var b in bytes)
        {
            result.Append(chars[b % chars.Length]);
        }
        return result.ToString();
    }

    private string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(challengeBytes);
    }

    private string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
    
    private string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        return match.Success ? match.Groups[1].Value : throw new Exception("Anti-forgery token not found");
    }
}
