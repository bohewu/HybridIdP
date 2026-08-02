using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Web.IdP.Extensions;

namespace Tests.SystemTests;

public sealed class RateLimitingSystemTests
{
    [Fact]
    public async Task TokenPolicy_ShouldShareLimitAcrossUnauthenticatedClientIdsFromSameSource()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:TokenPermitLimit"] = "3",
            ["RateLimiting:TokenWindowSeconds"] = "60",
            ["RateLimiting:QueueLimit"] = "0"
        });
        builder.Services.AddCustomRateLimiting(builder.Configuration);

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapPost("/connect/token", () => Results.Ok())
            .RequireRateLimiting("token");
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var firstResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "untrusted-client-a"
            }));
        using var changedClientResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "untrusted-client-b"
            }));
        using var basicRequest = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent([])
        };
        basicRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("untrusted-client-c:")));
        using var basicResponse = await client.SendAsync(basicRequest);
        using var missingClientResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changedClientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, basicResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            missingClientResponse.StatusCode);
    }

    [Fact]
    public async Task AuthorizePolicy_ShouldShareLimitAcrossUntrustedClientIdsFromSameSource()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:AuthorizePermitLimit"] = "2",
            ["RateLimiting:AuthorizeWindowSeconds"] = "60",
            ["RateLimiting:QueueLimit"] = "0"
        });
        builder.Services.AddCustomRateLimiting(builder.Configuration);

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], () => Results.Ok())
            .RequireRateLimiting("authorize");
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var firstResponse = await client.GetAsync(
            "/connect/authorize?client_id=untrusted-client-a");
        using var formClientResponse = await client.PostAsync(
            "/connect/authorize",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "untrusted-client-b"
            }));
        using var missingClientResponse = await client.GetAsync(
            "/connect/authorize");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, formClientResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            missingClientResponse.StatusCode);
    }
}
