using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public class PasswordGrantSecuritySystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;

    public PasswordGrantSecuritySystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_serverFixture.BaseUrl)
        };
    }

    public async Task InitializeAsync()
    {
        await _serverFixture.EnsureServerRunningAsync();
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PasswordGrant_WithMfaEnabledUser_ReturnsInvalidGrantWithoutTokens()
    {
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "testclient-public",
            ["username"] = "amr-mfa@hybridauth.local",
            ["password"] = "Test@123",
            ["scope"] = "openid profile offline_access"
        });

        using var response = await _httpClient.PostAsync("/connect/token", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_grant", payload.GetProperty("error").GetString());
        Assert.Equal(
            "The username/password couple is invalid.",
            payload.GetProperty("error_description").GetString());
        Assert.False(payload.TryGetProperty("access_token", out _));
        Assert.False(payload.TryGetProperty("refresh_token", out _));
        Assert.False(payload.TryGetProperty("id_token", out _));
    }
}
