using System.Net;
using Xunit;

namespace Tests.SystemTests;

/// <summary>
/// Basic tests for MFA Setup API endpoints (/api/account/mfa-setup/*).
/// These endpoints ONLY accept TwoFactorUserIdScheme (partial auth during MFA enrollment).
/// Full E2E testing is done manually via browser since the flow requires login page interaction.
/// </summary>
public class MfaSetupApiTests : IClassFixture<WebIdPServerFixture>
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;

    public MfaSetupApiTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverFixture.BaseUrl) };
    }

    [Fact]
    public async Task MfaSetup_Status_NoAuth_Returns401()
    {
        await _serverFixture.EnsureServerRunningAsync();
        
        var response = await _httpClient.GetAsync("/api/account/mfa-setup/status");
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MfaSetup_Policy_NoAuth_Returns401()
    {
        await _serverFixture.EnsureServerRunningAsync();
        
        var response = await _httpClient.GetAsync("/api/account/mfa-setup/policy");
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MfaSetup_TotpSetup_NoAuth_Returns401()
    {
        await _serverFixture.EnsureServerRunningAsync();
        
        var response = await _httpClient.GetAsync("/api/account/mfa-setup/totp/setup");
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MfaSetup_Passkeys_NoAuth_Returns401()
    {
        await _serverFixture.EnsureServerRunningAsync();
        
        var response = await _httpClient.GetAsync("/api/account/mfa-setup/passkeys");
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
