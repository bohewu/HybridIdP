using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Domain.Constants;
using OtpNet;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// System tests for MFA API endpoints.
/// Uses seeded testuser@hybridauth.local with password flow (testclient-public).
/// </summary>
[Trait("Category", "Slow")]
[Collection("Shared Server")]
public partial class MfaApiTests : IAsyncLifetime
{
    private record MfaVerifyResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public List<string>? RecoveryCodes { get; init; }
    }

    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;
    private string? _userToken;

    // Use seeded admin user from UserSeeder (same as UserinfoFlowTests)
    private const string TEST_USER_EMAIL = "admin@hybridauth.local";
    private const string TEST_USER_PASSWORD = "Admin@123";

    public MfaApiTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
        await Task.Delay(100);
        
        // Get token for seeded test user using password flow with testclient-public
        _userToken = await GetUserTokenAsync(TEST_USER_EMAIL, TEST_USER_PASSWORD);
        
        // PRE-CLEANUP: Ensure MFA is disabled before tests start (critical for test isolation)
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
            
            // Disable TOTP MFA if enabled
            await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = TEST_USER_PASSWORD });
            
            // Disable Email MFA if enabled
            await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
            
            // Small delay to ensure cleanup completes
            await Task.Delay(50);
        }
        catch
        {
            // Ignore pre-cleanup errors (MFA might already be disabled)
        }
    }

    public async Task DisposeAsync()
    {
        // Ensure MFA is disabled after all tests in this class (cleanup for test isolation)
        try
        {
            if (_userToken != null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
                
                // Try to disable TOTP MFA
                await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = TEST_USER_PASSWORD });
                
                // Try to disable Email MFA
                await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        
        _httpClient.Dispose();
    }

    [Fact]
    public async Task GetMfaStatus_ValidUser_ReturnsStatus()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.GetAsync("/api/account/mfa/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.NotNull(status);
        Assert.False(status.TwoFactorEnabled); // Should be disabled initially
    }

    [Fact]
    public async Task VerifyAmrClaimsInToken()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act - Call UserInfo endpoint to see claims
        var response = await _httpClient.GetAsync("/connect/userinfo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userInfo = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        // OpenIddict maps Claims.AuthenticationMethodReference to "amr"
        // Since we did password login, it should have "pwd" amr
        if (userInfo.TryGetProperty("amr", out var amrProperty))
        {
            var amrs = amrProperty.ValueKind == JsonValueKind.Array 
                ? amrProperty.EnumerateArray().Select(x => x.GetString()).ToList()
                : new List<string?> { amrProperty.GetString() };
            
            Assert.Contains(AuthConstants.Amr.Password, amrs);
            // TokenService NO LONGER hardcodes "mfa" for password grant (unless MFA was performed)
            // Assert.Contains("mfa", amrs); 
        }
        else
        {
            Assert.Fail("UserInfo does not contain 'amr' claim");
        }
    }

    [Fact]
    public async Task TotpEnrollment_GenericBearerToken_ReturnsForbidden()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        var verifyResponse = await _httpClient.PostAsJsonAsync(
            "/api/account/mfa/verify",
            new { Code = "000000" });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, setupResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task VerifyMfa_GenericBearerToken_ReturnsForbidden()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
        var invalidCode = "000000";

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/verify", new { Code = invalidCode });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TotpEnrollment_FreshInteractiveReauthentication_AllowsEnrollment()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        await MfaEnrollmentTestClient.SignInAsync(
            _httpClient,
            TEST_USER_EMAIL,
            TEST_USER_PASSWORD,
            "/Account/Login");

        var staleApplicationCookieResponse =
            await _httpClient.GetAsync("/api/account/mfa/setup");
        Assert.Equal(HttpStatusCode.Forbidden, staleApplicationCookieResponse.StatusCode);
        var staleSetupFlowResponse =
            await _httpClient.GetAsync("/api/account/mfa-setup/totp/setup");
        Assert.Equal(HttpStatusCode.Forbidden, staleSetupFlowResponse.StatusCode);

        await MfaEnrollmentTestClient.SetCsrfTokenAsync(_httpClient);

        var reauthenticationResponse =
            await _httpClient.PostAsync("/api/account/mfa/reauthenticate", null);
        Assert.Equal(HttpStatusCode.OK, reauthenticationResponse.StatusCode);
        var reauthenticationResult =
            await reauthenticationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var loginUrl = reauthenticationResult.GetProperty("loginUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(loginUrl));

        await MfaEnrollmentTestClient.SignInAsync(
            _httpClient,
            TEST_USER_EMAIL,
            TEST_USER_PASSWORD,
            loginUrl!);
        await MfaEnrollmentTestClient.SetCsrfTokenAsync(_httpClient);

        var setupResponse =
            await _httpClient.GetAsync("/api/account/mfa/setup");
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetupDto>();
        Assert.NotNull(setup);

        var totp = GenerateTotp(setup.SharedKey);
        var verifyResponse = await _httpClient.PostAsJsonAsync(
            "/api/account/mfa/verify",
            new { Code = totp });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verifyResult =
            await verifyResponse.Content.ReadFromJsonAsync<MfaVerifyResponse>();
        Assert.NotNull(verifyResult);
        Assert.True(verifyResult.Success);
        Assert.Equal(10, verifyResult.RecoveryCodes?.Count);

        var reusedProofResponse =
            await _httpClient.GetAsync("/api/account/mfa/setup");
        Assert.True(
            reusedProofResponse.StatusCode is
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _userToken);
        var cleanupResponse = await _httpClient.PostAsJsonAsync(
            "/api/account/mfa/disable",
            new { Password = TEST_USER_PASSWORD });
        Assert.Equal(HttpStatusCode.OK, cleanupResponse.StatusCode);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_MfaNotEnabled_ReturnsBadRequest()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = null;
        await MfaEnrollmentTestClient.SignInAsync(
            _httpClient,
            TEST_USER_EMAIL,
            TEST_USER_PASSWORD,
            "/Account/Login");
        await MfaEnrollmentTestClient.SetCsrfTokenAsync(_httpClient);

        // Act
        var response = await _httpClient.PostAsJsonAsync(
            "/api/account/mfa/recovery-codes",
            new { password = TEST_USER_PASSWORD });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GenerateRecoveryCodes_GenericBearerWithoutFreshProof_ReturnsForbidden()
    {
        await MfaEnrollmentTestClient.AuthorizeAsync(
            _httpClient,
            TEST_USER_EMAIL,
            TEST_USER_PASSWORD);
        var setupResponse =
            await _httpClient.GetAsync("/api/account/mfa-setup/totp/setup");
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetupDto>();
        Assert.NotNull(setup);

        var verifyResponse = await _httpClient.PostAsJsonAsync(
            "/api/account/mfa-setup/totp/verify",
            new { Code = GenerateTotp(setup.SharedKey) });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        using var bearerClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = _httpClient.BaseAddress
        };
        bearerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _userToken);

        var response =
            await bearerClient.PostAsync("/api/account/mfa/recovery-codes", null);

        Assert.True(
            response.StatusCode is
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            using var responseJson = JsonDocument.Parse(responseBody);
            Assert.False(
                responseJson.RootElement.TryGetProperty("recoveryCodes", out var recoveryCodes) &&
                recoveryCodes.ValueKind == JsonValueKind.Array &&
                recoveryCodes.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task DisableMfa_WrongPassword_ReturnsBadRequest()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = "WrongPassword123!" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MfaEndpoints_Unauthorized_Returns401()
    {
        // Arrange - No auth header
        _httpClient.DefaultRequestHeaders.Authorization = null;

        // Act & Assert
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var setupResponse = await _httpClient.GetAsync("/api/account/mfa/setup");
        
        Assert.Equal(HttpStatusCode.Unauthorized, statusResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, setupResponse.StatusCode);
    }

    [Fact]
    public async Task DisableMfa_ValidPassword_DisablesMfa()
    {
        await MfaEnrollmentTestClient.AuthorizeAsync(
            _httpClient,
            TEST_USER_EMAIL,
            TEST_USER_PASSWORD);
        var setupResponse =
            await _httpClient.GetAsync("/api/account/mfa-setup/totp/setup");
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetupDto>();
        
        // Generate valid TOTP
        var totp = GenerateTotp(setup!.SharedKey);
        
        var verifyResponse = await _httpClient.PostAsJsonAsync(
            "/api/account/mfa-setup/totp/verify",
            new { Code = totp });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        // Act - Disable
        var disableResponse = await _httpClient.PostAsJsonAsync("/api/account/mfa/disable", new { Password = TEST_USER_PASSWORD });

        // Assert
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        
        // Verify status is disabled
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _userToken);
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(status!.TwoFactorEnabled);
    }

    [Fact]
    public async Task TotpEnrollment_ImpersonatedCookieWithoutReauthentication_ReturnsForbidden()
    {
        // 1. Authenticate as M2M Admin (Client Credentials) to find user
        // M2M token has explicit scopes permissions.
        var m2mToken = await GetM2MAdminTokenAsync();
        
        // 2. Find Passwordless User ID & Verify Admin Role
        var passwordlessUserEmail = "passwordless@hybridauth.local";
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", m2mToken);
        
        // Debug: Check Admin has roles
        var adminSearch = await _httpClient.GetAsync($"/api/admin/users?search={AuthConstants.DefaultAdmin.Email}");
        Assert.Equal(HttpStatusCode.OK, adminSearch.StatusCode);
        var adminResult = await adminSearch.Content.ReadFromJsonAsync<JsonElement>();
        var adminItems = adminResult.GetProperty("items");
        Assert.True(adminItems.GetArrayLength() > 0, "Admin user not found");
        var adminRoles = adminItems[0].GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("Admin", adminRoles); // Assert Admin has Admin role in DB

        var usersResponse = await _httpClient.GetAsync($"/api/admin/users?search={passwordlessUserEmail}");
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode); 
        var usersResult = await usersResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = usersResult.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "No passwordless user found"); 
        var userId = items[0].GetProperty("id").GetString();

        // 3. Authenticate as Admin User (Password Grant) for Impersonation
        // Impersonate requires a real user, not M2M.
        var adminUserToken = await GetUserTokenAsync(AuthConstants.DefaultAdmin.Email, AuthConstants.DefaultAdmin.Password);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminUserToken);

        // 4. Start Impersonation & Capture Cookie
        var impersonateResponse = await _httpClient.PostAsync($"/api/admin/users/{userId}/impersonate", null);
        if (impersonateResponse.StatusCode != HttpStatusCode.OK)
        {
            var error = await impersonateResponse.Content.ReadAsStringAsync();
            Assert.Fail($"Impersonate failed: {impersonateResponse.StatusCode} {error}");
        }
        Assert.Equal(HttpStatusCode.OK, impersonateResponse.StatusCode);
        
        var cookieHeaders = impersonateResponse.Headers.GetValues("Set-Cookie");
        Assert.NotEmpty(cookieHeaders);
        var cookieContainer = new CookieContainer();
        foreach (var setCookieHeader in cookieHeaders)
        {
            cookieContainer.SetCookies(_httpClient.BaseAddress!, setCookieHeader);
        }

        // 5. Setup MFA as Impersonated User (Using Cookie)
        using var userClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        }) 
        { 
            BaseAddress = _httpClient.BaseAddress 
        };
        await MfaEnrollmentTestClient.SetCsrfTokenAsync(userClient);

        var setupResponse = await userClient.GetAsync("/api/account/mfa/setup");
        Assert.Equal(HttpStatusCode.Forbidden, setupResponse.StatusCode);
        var setupFlowResponse =
            await userClient.GetAsync("/api/account/mfa-setup/totp/setup");
        Assert.Equal(HttpStatusCode.Forbidden, setupFlowResponse.StatusCode);
    }

    #region Email MFA Tests (Phase 20.3)

    [Fact]
    public async Task GetMfaStatus_IncludesEmailMfaEnabled()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.GetAsync("/api/account/mfa/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.NotNull(status);
        Assert.False(status.EmailMfaEnabled); // Should be disabled initially
    }

    [Fact]
    public async Task EmailMfa_EnableDisable_Works()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act - Enable
        var enableResponse = await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        // Verify enabled
        var statusResponse = await _httpClient.GetAsync("/api/account/mfa/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.True(status!.EmailMfaEnabled);

        // Act - Disable
        var disableResponse = await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        // Verify disabled
        var statusResponse2 = await _httpClient.GetAsync("/api/account/mfa/status");
        var status2 = await statusResponse2.Content.ReadFromJsonAsync<MfaStatusDto>();
        Assert.False(status2!.EmailMfaEnabled);
    }

    [Fact]
    public async Task EmailMfa_SendCode_ReturnsSuccess()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // Act
        var response = await _httpClient.PostAsync("/api/account/mfa/email/send", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task EmailMfa_VerifyInvalidCode_ReturnsFalse()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        // First send code
        await _httpClient.PostAsync("/api/account/mfa/email/send", null);

        // Act - Verify with invalid code
        var response = await _httpClient.PostAsJsonAsync("/api/account/mfa/email/verify", new { Code = "000000" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("success").GetBoolean());
    }

    #endregion
    
    private async Task<string> GetM2MAdminTokenAsync()
    {
        var scopes = new[]
        {
            "users.read", "users.impersonate"
        };

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
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }


    #region Helper Methods

    private string GenerateTotp(string secretKey, int offsetSeconds = 0)
    {
        var bytes = Base32Encoding.ToBytes(secretKey.Replace(" ", ""));
        var totp = new Totp(bytes);
        
        if (offsetSeconds == 0)
            return totp.ComputeTotp(); // Current time
        
        // Compute for future/past
        return totp.ComputeTotp(DateTime.UtcNow.AddSeconds(offsetSeconds));
    }

    private async Task<string> GetUserTokenAsync(string username, string password)
    {
        // Use testclient-public which has password flow enabled
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "testclient-public",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid profile roles"
        });

        var response = await _httpClient.PostAsync("/connect/token", tokenRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token request failed with {response.StatusCode}: {errorContent}");
        }
        
        var content = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonDocument.Parse(content);
        return tokenJson.RootElement.GetProperty("access_token").GetString()!;
    }

    #endregion

    #region DTOs

    private record MfaStatusDto
    {
        public bool TwoFactorEnabled { get; init; }
        public bool HasAuthenticator { get; init; }
        public int RecoveryCodesLeft { get; init; }
        public bool HasPassword { get; init; }
        public bool EmailMfaEnabled { get; init; }
    }

    private record MfaSetupDto
    {
        public string SharedKey { get; init; } = "";
        public string AuthenticatorUri { get; init; } = "";
        public string QrCodeDataUri { get; init; } = "";
    }

    private record MfaVerifyResultDto
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public List<string>? RecoveryCodes { get; init; }
    }

    #endregion
}
