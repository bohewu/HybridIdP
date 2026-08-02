using System.Net;
using System.Text.Json;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public sealed class PublicOriginHostSecuritySystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _httpClient;

    public PublicOriginHostSecuritySystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri(serverFixture.BaseUrl)
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
    public async Task Discovery_ShouldRejectUntrustedHost()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/.well-known/openid-configuration");
        request.Headers.Host = "attacker.invalid";

        using var response = await _httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_ShouldRejectUntrustedHostWithoutRedirecting()
    {
        var redirectUri = Uri.EscapeDataString($"{_serverFixture.BaseUrl}/signin-oidc");
        var authorizeUri =
            $"/connect/authorize?client_id=testclient-public&redirect_uri={redirectUri}" +
            "&response_type=code&scope=openid&code_challenge=" + new string('A', 43) +
            "&code_challenge_method=S256";
        using var request = new HttpRequestMessage(HttpMethod.Get, authorizeUri);
        request.Headers.Host = "attacker.invalid";

        using var response = await _httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Discovery_ShouldRetainConfiguredLocalOriginForTrustedHost()
    {
        using var response = await _httpClient.GetAsync(
            "/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var content = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(content);
        Assert.Equal(
            $"{_serverFixture.BaseUrl}/",
            document.RootElement.GetProperty("issuer").GetString());
    }
}
