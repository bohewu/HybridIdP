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
    public async Task ExternalLinkConfirmation_ShouldShareLoginSourceBudgetAcrossAccountNamesBeforeVerification()
    {
        var metadata = typeof(Web.IdP.Pages.Account.ExternalLoginConfirmationModel)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), true)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Single();
        Assert.Equal("login", metadata.PolicyName);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true", ["RateLimiting:LoginPermitLimit"] = "5",
            ["RateLimiting:LoginWindowSeconds"] = "60", ["RateLimiting:QueueLimit"] = "0"
        });
        builder.Services.AddCustomRateLimiting(builder.Configuration);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        var credentialChecks = 0;
        app.MapPost("/Account/ExternalLoginConfirmation", () => { credentialChecks++; return Results.Ok(); })
            .WithMetadata(metadata);
        app.MapPost("/Account/Login", () => { credentialChecks++; return Results.Ok(); }).RequireRateLimiting("login");
        app.MapGet("/Account/ExternalLoginCallback", () => Results.Ok());
        await app.StartAsync();
        using var client = app.GetTestClient();
        for (var index = 0; index < 5; index++)
        {
            using var response = await client.PostAsync(index == 0 ? "/Account/Login" : "/Account/ExternalLoginConfirmation?handler=Link",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Login"] = $"account-{index}", ["Input.Password"] = "synthetic" }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var exhausted = await client.PostAsync("/Account/ExternalLoginConfirmation?handler=Link",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Login"] = "another-account" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);
        Assert.Equal(5, credentialChecks);
        using var callback = await client.GetAsync("/Account/ExternalLoginCallback");
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
    }

    [Fact]
    public void NativeRecoveryEnabled_WithoutRateLimiting_FailsClosed()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "false",
            ["ForgotPasswordRecovery:NativeRecoveryEnabled"] = "true"
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Services.AddCustomRateLimiting(builder.Configuration));

        Assert.Contains("requires rate limiting", exception.Message, StringComparison.Ordinal);
    }

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
