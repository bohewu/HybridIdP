using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tests.SystemTests;

[Collection("Shared Server")]
public sealed class PendingDirectorySettlementPageSystemTests : IAsyncLifetime
{
    private readonly WebIdPServerFixture _serverFixture;
    private readonly HttpClient _client;

    public PendingDirectorySettlementPageSystemTests(WebIdPServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _client = new HttpClient(handler) { BaseAddress = new Uri(serverFixture.BaseUrl) };
    }

    public Task InitializeAsync() => _serverFixture.EnsureServerRunningAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("en-US", "Confirm AD account status", "One-time code")]
    [InlineData("zh-TW", "確認 AD 帳號狀態", "一次性處理碼")]
    public async Task Page_RendersLocalizedFormAtStablePathWithoutUrlBoundSecretFields(
        string culture,
        string title,
        string continuationLabel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/PendingDirectorySettlement");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        using var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains($"<html lang=\"{culture}\"", html);
        Assert.Contains(HtmlEncoder.Default.Encode(title), html);
        Assert.Contains(HtmlEncoder.Default.Encode(continuationLabel), html);
        Assert.Contains("data-pending-directory-settlement", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("action=\"/api/account/pending-directory-settlement/claim\"", html);
        Assert.DoesNotContain("name=\"continuation\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preparationId", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attemptId", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("directoryObjectId", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousApis_AreHttpReachableAndRequireTheRenderedAntiforgeryToken()
    {
        using var pageResponse = await _client.GetAsync("/Account/PendingDirectorySettlement");
        var html = await pageResponse.Content.ReadAsStringAsync();
        pageResponse.EnsureSuccessStatusCode();
        var antiforgeryToken = ExtractAntiforgeryToken(html);

        using var missingTokenResponse = await _client.PostAsJsonAsync(
            "/api/account/pending-directory-settlement/claim",
            new { continuation = "invalid-or-expired" });
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        using var claimRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/account/pending-directory-settlement/claim")
        {
            Content = JsonContent.Create(new { continuation = "invalid-or-expired" })
        };
        claimRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        using var claimResponse = await _client.SendAsync(claimRequest);
        var claimBody = await claimResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, claimResponse.StatusCode);
        Assert.Equal("unavailable", claimBody.GetProperty("outcome").GetString());
        Assert.Equal(["outcome"], claimBody.EnumerateObject().Select(property => property.Name));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"");
        Assert.True(match.Success, "The pending settlement page must render an antiforgery token.");
        return match.Groups[1].Value;
    }
}
