using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Tests.SystemTests;

public partial class MfaApiTests
{
    [Fact]
    public async Task EmailMfa_SendCode_RateLimiting_Works()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
        
        // 1. Enable Email MFA
        var enableResponse = await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        enableResponse.EnsureSuccessStatusCode();

        // 2. Send Code - First Attempt (Should Success)
        var sendResponse1 = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
        sendResponse1.EnsureSuccessStatusCode();
        var result1 = await sendResponse1.Content.ReadFromJsonAsync<SendEmailMfaCodeResponse>();
        Assert.True(result1.Success);

        // 3. Send Code - Immediately After (Should Fail with Rate Limit)
        var sendResponse2 = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
        
        // Assert: Should return 429 Too Many Requests
        Assert.Equal(HttpStatusCode.TooManyRequests, sendResponse2.StatusCode);
        
        var result2 = await sendResponse2.Content.ReadFromJsonAsync<SendEmailMfaCodeResponse>();
        Assert.False(result2.Success);
        Assert.True(result2.RemainingSeconds > 0 && result2.RemainingSeconds <= 60);

        // Cleanup: Disable Email MFA
        await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
    }
}

public class SendEmailMfaCodeResponse
{
    public bool Success { get; set; }
    public int RemainingSeconds { get; set; }
}
