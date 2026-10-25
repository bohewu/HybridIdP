using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Core.Application;
using Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Web.IdP.Extensions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tests.SystemTests;

// Real production OpenIddict registration and HTTP middleware, isolated in-process stores.
// 204 means native protocol validation reached the fixture endpoint, not user login/token issuance.
public sealed class ClientProtocolPolicyPipelineTests
{
    [Theory]
    [InlineData("inherit", "basic", true)]
    [InlineData("inherit", "post", true)]
    [InlineData("client_secret_basic", "basic", true)]
    [InlineData("client_secret_basic", "post", false)]
    [InlineData("client_secret_post", "post", true)]
    [InlineData("client_secret_post", "basic", false)]
    public async Task Token_ShouldEnforceCredentialOrigin(string policy, string method, bool accepted)
    {
        await using var app = await CreateHostAsync(tokenAuth: policy);
        using var client = CreateClient(app);
        using var request = TokenRequest(method);
        // This non-standard, client-chosen parameter must not spoof credential provenance.
        var body = await request.Content!.ReadAsStringAsync();
        request.Content = new StringContent(body + "&token_endpoint_auth_method=" + policy,
            Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await client.SendAsync(request);
        if (accepted) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        else await AssertJsonErrorAsync(response, "invalid_client");
    }

    [Theory]
    [InlineData("basic", "wrong", false)]
    [InlineData("post", "wrong", false)]
    [InlineData("basic", "<client-secret>", true)]
    public async Task Token_ShouldRetainNativeCredentialAndMixedMethodRejection(string method, string secret, bool mixed)
    {
        await using var app = await CreateHostAsync();
        using var client = CreateClient(app);
        using var request = TokenRequest(method, secret, mixed);
        using var response = await client.SendAsync(request);
        await AssertJsonErrorAsync(response, mixed ? "invalid_request" : "invalid_client");
    }

    [Theory]
    [InlineData("inherit", null, false, true)]
    [InlineData("inherit", "plain", false, true)]
    [InlineData("inherit", "S256", false, true)]
    [InlineData("S256", null, false, true)]
    [InlineData("S256", "S256", false, true)]
    [InlineData("S256", "plain", false, false)]
    [InlineData("S256", "omitted", false, false)]
    [InlineData("S256", null, true, false)]
    public async Task Authorization_ShouldRestrictOnlySuppliedPkce(string policy, string? method, bool required, bool accepted)
    {
        await using var app = await CreateHostAsync(pkce: policy, requirePkce: required);
        using var client = CreateClient(app);
        using var response = await client.GetAsync(AuthorizationUrl(method: method));
        await AssertAuthorizationOutcomeAsync(response, accepted);
    }

    [Theory]
    [InlineData("inherit", null, true)]
    [InlineData("inherit", "query", true)]
    [InlineData("inherit", "form_post", true)]
    [InlineData("query", null, true)]
    [InlineData("query", "query", true)]
    [InlineData("query", "form_post", false)]
    [InlineData("form_post", null, false)]
    [InlineData("form_post", "query", false)]
    [InlineData("form_post", "form_post", true)]
    public async Task Authorization_ShouldRetainDefaultModeAndEnforceExplicitPolicy(string policy, string? mode, bool accepted)
    {
        await using var app = await CreateHostAsync(mode: policy);
        using var client = CreateClient(app);
        using var response = await client.GetAsync(AuthorizationUrl(mode));
        await AssertAuthorizationOutcomeAsync(response, accepted);
        if (policy == "form_post" && mode is null)
        {
            // A validation-stage rejection is handled locally by native OpenIddict.
            // It must not silently produce a form_post callback for the omitted mode.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.DoesNotContain("<form", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("S256", "form_post", true)]
    [InlineData("plain", "form_post", false)]
    [InlineData("S256", "query", false)]
    [InlineData("S256", null, false)]
    public async Task Par_ShouldValidatePoliciesAndRestoreApprovedParameters(string method, string? mode, bool accepted)
    {
        await using var app = await CreateHostAsync(pkce: "S256", mode: "form_post");
        using var client = CreateClient(app);
        var parameters = AuthorizationParameters(mode, method);
        parameters["client_secret"] = "<client-secret>";
        using var response = await client.PostAsync("/connect/par", new FormUrlEncodedContent(parameters));
        if (!accepted)
        {
            await AssertJsonErrorAsync(response, "invalid_request");
            return;
        }
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var requestUri = json.RootElement.GetProperty("request_uri").GetString()!;
        // Raw outer parameters must not override authenticated PAR response_mode/PKCE.
        using var authorization = await client.GetAsync("/connect/authorize?client_id=policy-client&request_uri=" +
            Uri.EscapeDataString(requestUri) + "&response_mode=query&code_challenge_method=plain");
        Assert.Equal(HttpStatusCode.NoContent, authorization.StatusCode);
    }

    [Fact]
    public async Task Authorization_ShouldRetainRedirectAndPermissionValidation()
    {
        await using var app = await CreateHostAsync(pkce: "S256", mode: "query");
        using var client = CreateClient(app);
        using var response = await client.GetAsync(AuthorizationUrl("query", "S256")
            .Replace(Uri.EscapeDataString("https://client.example/callback"), Uri.EscapeDataString("https://unregistered.example/callback")));
        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using var scope = app.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await manager.FindByClientIdAsync("policy-client"))!;
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.Permissions.Remove(Permissions.Endpoints.Authorization);
        await manager.UpdateAsync(application, descriptor);
        using var forbidden = await client.GetAsync(AuthorizationUrl("query", "S256"));
        Assert.NotEqual(HttpStatusCode.NoContent, forbidden.StatusCode);
    }

    [Fact]
    public async Task Discovery_ShouldKeepServerWideCapabilities()
    {
        await using var app = await CreateHostAsync(pkce: "S256", tokenAuth: "client_secret_basic", mode: "form_post");
        using var client = CreateClient(app);
        using var response = await client.GetAsync("/.well-known/openid-configuration");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] Values(string name) => json.RootElement.GetProperty(name).EnumerateArray().Select(v => v.GetString()!).ToArray();
        Assert.Contains("plain", Values("code_challenge_methods_supported"));
        Assert.Contains("S256", Values("code_challenge_methods_supported"));
        Assert.Contains("client_secret_basic", Values("token_endpoint_auth_methods_supported"));
        Assert.Contains("client_secret_post", Values("token_endpoint_auth_methods_supported"));
        Assert.Contains("query", Values("response_modes_supported"));
        Assert.Contains("form_post", Values("response_modes_supported"));
    }

    private static async Task<WebApplication> CreateHostAsync(
        string pkce = "inherit", string tokenAuth = "inherit", string mode = "inherit", bool requirePkce = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenIddict:UseEphemeralKeysForTesting"] = "true"
        });
        builder.Logging.ClearProviders();
        // Exercise the same registration used by the product. Only persistence and key storage are isolated.
        builder.Services.AddCustomIdentityAndAccess(builder.Configuration, builder.Environment,
            "SqlServer", "Server=(local);Database=unused");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.RemoveAll<ApplicationDbContext>();
        builder.Services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
        var databaseName = "client-protocol-" + Guid.NewGuid().ToString("N");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName).UseOpenIddict<Guid>());
        var app = builder.Build();
        try
        {
            app.UseAuthentication();
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });
            await app.StartAsync();
            using var scope = app.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = "policy-client", ClientSecret = "<client-secret>", ClientType = ClientTypes.Confidential,
                ApplicationType = ApplicationTypes.Web, ConsentType = ConsentTypes.Explicit,
                RedirectUris = { new Uri("https://client.example/callback") },
                Permissions = { Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                    Permissions.Endpoints.PushedAuthorization, Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.ClientCredentials, Permissions.ResponseTypes.Code }
            };
            ClientProtocolPolicy.Apply(descriptor.Properties, pkce, tokenAuth, mode, true);
            if (requirePkce) descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            await manager.CreateAsync(descriptor);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static HttpRequestMessage TokenRequest(string method, string secret = "<client-secret>", bool mixed = false)
    {
        var parameters = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
        if (method == "post" || mixed)
        {
            parameters["client_id"] = "policy-client";
            parameters["client_secret"] = secret;
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(parameters) };
        if (method == "basic") request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes("policy-client:" + secret)));
        return request;
    }

    private static Dictionary<string, string> AuthorizationParameters(string? mode = null, string? method = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = "policy-client", ["response_type"] = "code",
            ["redirect_uri"] = "https://client.example/callback", ["scope"] = "openid"
        };
        if (mode is not null) parameters["response_mode"] = mode;
        if (method is not null)
        {
            parameters["code_challenge"] = new string('A', 43);
            if (method != "omitted") parameters["code_challenge_method"] = method;
        }
        return parameters;
    }

    private static string AuthorizationUrl(string? mode = null, string? method = null) =>
        "/connect/authorize?" + string.Join("&", AuthorizationParameters(mode, method)
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

    private static async Task AssertAuthorizationOutcomeAsync(HttpResponseMessage response, bool accepted)
    {
        if (accepted) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        else
        {
            Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Contains("invalid_request", response.Headers.Location?.ToString() ?? await response.Content.ReadAsStringAsync());
        }
    }

    private static async Task AssertJsonErrorAsync(HttpResponseMessage response, string error)
    {
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, json.RootElement.GetProperty("error").GetString());
    }
}
