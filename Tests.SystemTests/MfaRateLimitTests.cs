using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Tests.SystemTests;

public partial class MfaApiTests
{
    [Fact(Skip = "Slow test - requires 60s rate limit cooldown. Run manually when needed.")]
    public async Task EmailMfa_SendCode_RateLimiting_Works()
    {
        // Arrange
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
        
        // 1. Enable Email MFA (may already be enabled from other tests)
        var enableResponse = await _httpClient.PostAsync("/api/account/mfa/email/enable", null);
        // Don't assert on enable - it may already be enabled

        // 2. Wait for any existing rate limit cooldown from previous test runs
        await Task.Delay(1000); // Small delay to ensure clean state
        
        // Try sending - if rate limited, wait and retry once
        var sendResponse1 = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
        if (sendResponse1.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var rateLimitResult = await sendResponse1.Content.ReadFromJsonAsync<SendEmailMfaCodeResponse>();
            if (rateLimitResult?.RemainingSeconds > 0)
            {
                // Wait for cooldown plus small buffer
                await Task.Delay((rateLimitResult.RemainingSeconds + 2) * 1000);
                sendResponse1 = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
            }
        }
        
        // Now it should succeed
        Assert.Equal(HttpStatusCode.OK, sendResponse1.StatusCode);
        var result1 = await sendResponse1.Content.ReadFromJsonAsync<SendEmailMfaCodeResponse>();
        Assert.True(result1!.Success, "First send should succeed");

        // 3. Send Code - Immediately After (Should Fail with Rate Limit)
        var sendResponse2 = await _httpClient.PostAsync("/api/account/mfa/email/send", null);
        
        // Assert: Should return 429 Too Many Requests
        Assert.Equal(HttpStatusCode.TooManyRequests, sendResponse2.StatusCode);
        
        var result2 = await sendResponse2.Content.ReadFromJsonAsync<SendEmailMfaCodeResponse>();
        Assert.NotNull(result2);
        Assert.False(result2.Success);
        Assert.True(result2.RemainingSeconds > 0 && result2.RemainingSeconds <= 60, 
            $"RemainingSeconds should be between 1-60, got {result2.RemainingSeconds}");

        // Cleanup: Disable Email MFA
        await _httpClient.PostAsync("/api/account/mfa/email/disable", null);
    }
}

public class SendEmailMfaCodeResponse
{
    public bool Success { get; set; }
    public int RemainingSeconds { get; set; }
}
