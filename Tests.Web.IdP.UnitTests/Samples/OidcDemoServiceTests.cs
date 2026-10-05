using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using TestClient.Options;
using TestClient.Services;

namespace Tests.Web.IdP.UnitTests.Samples;

public class OidcDemoServiceTests
{
    [Theory]
    [InlineData(400, "{\"error\":\"invalid_scope\",\"error_description\":\"private-detail\"}", true)]
    [InlineData(200, "{\"error\":\"invalid_scope\"}", false)]
    [InlineData(400, "{\"error\":\"invalid_client\",\"error_description\":\"private-detail\"}", false)]
    [InlineData(400, "not-json", false)]
    public async Task ProbeInvalidScopeAsync_ShouldAcceptOnlyExpectedProtocolError_WithoutExposingEndpointBody(int status, string body, bool expected)
    {
        using var client = new HttpClient(new ResponseHandler(async request =>
        {
            Assert.Equal("/connect/par", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            var form = await request.Content!.ReadAsStringAsync();
            Assert.Contains("client_id=testclient-public", form);
            Assert.Contains("scope=openid+profile+api%3Ainvalid%3Aread", form);
            Assert.Contains("code_challenge_method=S256", form);
            var response = JsonResponse(body);
            response.StatusCode = (HttpStatusCode)status;
            return response;
        }));
        var service = new OidcDemoService(client, Microsoft.Extensions.Options.Options.Create(
            new OidcDemoOptions { Scopes = ["openid", "profile"] }));

        var result = await service.ProbeInvalidScopeAsync("https://localhost:7001/signin-oidc", default);

        Assert.Equal(expected, result.Success);
        Assert.Equal(expected ? "invalid_scope" : null, result.ExpectedProtocolError);
        Assert.Equal(body == "not-json" ? (int?)null : status, result.StatusCode);
        Assert.DoesNotContain("private-detail", result.Message);
        Assert.Null(result.UserInfoJson);
    }

    [Fact]
    public async Task RefreshAsync_ShouldUseRotatedTokenForNextRefresh_AndPreserveValidatedIdentity()
    {
        var bodies = new List<string>();
        var properties = SavedSession();
        using var client = new HttpClient(new ResponseHandler(async request =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            var count = bodies.Count;
            return JsonResponse(JsonSerializer.Serialize(new
            {
                access_token = $"access-{count}",
                refresh_token = $"refresh-{count}",
                token_type = "Bearer",
                expires_in = 300,
                id_token = "unvalidated-replacement"
            }));
        }));
        var service = CreateService(client);

        Assert.True((await service.RefreshAsync(properties, default)).Success);
        Assert.True((await service.RefreshAsync(properties, default)).Success);

        Assert.Contains("refresh_token=initial-refresh", bodies[0]);
        Assert.Contains("refresh_token=refresh-1", bodies[1]);
        Assert.DoesNotContain("scope=", bodies[1]);
        Assert.Equal("refresh-2", properties.GetTokenValue("refresh_token"));
        Assert.Equal("access-2", properties.GetTokenValue("access_token"));
        Assert.Equal("validated-id", properties.GetTokenValue("id_token"));
        Assert.Equal("2", properties.Items["demo_refresh_count"]);
        Assert.True(DateTimeOffset.Parse(properties.GetTokenValue("expires_at")!) > DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"new\",\"token_type\":\"Basic\",\"expires_in\":300}")]
    [InlineData("{\"access_token\":\"new\",\"token_type\":\"Bearer\",\"expires_in\":0}")]
    [InlineData("not-json")]
    public async Task RefreshAsync_ShouldRetainSavedTokens_WhenResponseIsInvalid(string body)
    {
        using var client = new HttpClient(new ResponseHandler(_ => Task.FromResult(JsonResponse(body))));
        var properties = SavedSession();

        var result = await CreateService(client).RefreshAsync(properties, default);

        Assert.False(result.Success);
        Assert.Equal("initial-access", properties.GetTokenValue("access_token"));
        Assert.Equal("initial-refresh", properties.GetTokenValue("refresh_token"));
        Assert.False(properties.Items.ContainsKey("demo_refresh_count"));
    }

    [Fact]
    public async Task RefreshAsync_ShouldPreserveRefreshToken_WhenNoReplacementIsIssued()
    {
        using var client = new HttpClient(new ResponseHandler(_ => Task.FromResult(JsonResponse(
            "{\"access_token\":\"new\",\"token_type\":\"Bearer\",\"expires_in\":300}"))));
        var properties = SavedSession();
        var result = await CreateService(client).RefreshAsync(properties, default);
        Assert.True(result.Success);
        Assert.False(result.RefreshTokenRotated);
        Assert.Equal("initial-refresh", properties.GetTokenValue("refresh_token"));
    }

    [Fact]
    public async Task RefreshAsync_ShouldPropagateCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new HttpClient(new ResponseHandler(_ => throw new OperationCanceledException(cancellation.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(client).RefreshAsync(SavedSession(), cancellation.Token));
    }

    [Fact]
    public async Task ReadUserInfoAsync_ShouldUseBearerToken_AndNotExposeRejectedResponseBody()
    {
        using var client = new HttpClient(new ResponseHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("initial-access", request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive-server-detail") });
        }));
        var result = await CreateService(client).ReadUserInfoAsync("initial-access", default);
        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.DoesNotContain("sensitive-server-detail", result.Message);
        Assert.Null(result.UserInfoJson);
    }

    private static OidcDemoService CreateService(HttpClient client) => new(client,
        Microsoft.Extensions.Options.Options.Create(new OidcDemoOptions()));

    private static AuthenticationProperties SavedSession()
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = "initial-access" },
            new AuthenticationToken { Name = "refresh_token", Value = "initial-refresh" },
            new AuthenticationToken { Name = "id_token", Value = "validated-id" }
        ]);
        return properties;
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
