using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Infrastructure.Configuration;
using Infrastructure.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Web.IdP.Extensions;

namespace Tests.Web.IdP.UnitTests.Extensions;

public class HealthEndpointBoundaryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Health_ShouldKeepPublicLivenessFreeOfDependencyWork(bool trustedPeer)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "false", ["AllowedHosts"] = "*" });
        builder.Logging.ClearProviders();
        var calls = 0;
        builder.Services.AddHealthChecks().AddCheck("dependency", () =>
        {
            Interlocked.Increment(ref calls);
            return HealthCheckResult.Unhealthy("fixture dependency unavailable");
        });
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("DependencyReadiness", policy => policy.RequireAssertion(context =>
            context.Resource is HttpContext http && ForwardedHeadersHelper.IsTrustedReadinessAddress(http.Connection.RemoteIpAddress, new ProxyOptions()))));
        builder.Services.AddCustomRateLimiting(builder.Configuration);
        builder.Services.AddControllers();
        builder.Services.AddRazorPages();
        builder.Services.AddSignalR();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(trustedPeer ? "127.0.0.1" : "203.0.113.99");
            await next(context);
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCustomEndpoints(builder.Configuration);
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(0, calls);
        var readiness = await client.GetAsync("/health/ready");
        Assert.Equal(trustedPeer ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Unauthorized, readiness.StatusCode);
        Assert.Equal(trustedPeer ? 1 : 0, calls);
        await app.StopAsync();
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
