using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using AuthenticationSchemes = TestClient.Constants.AuthenticationSchemes;
using TestClient.Controllers;
using TestClient.Options;
using TestClient.Services;

namespace Tests.Web.IdP.UnitTests.Samples;

public class PushedAuthorizationErrorTests
{
    [Theory]
    [InlineData("invalid_request")]
    [InlineData("invalid_client")]
    [InlineData("unauthorized_client")]
    [InlineData("invalid_scope")]
    [InlineData("unsupported_response_type")]
    public async Task Challenge_ShouldExplainRejectedPar_WithoutExposingUpstreamDetails(string error)
    {
        using var response = JsonResponse(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
        {
            error,
            error_description = "private-description <script>secret</script>",
            error_uri = "https://private.example/token-detail"
        }));
        var requests = 0;
        using var backchannel = new HttpClient(new ResponseHandler(async request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://idp.example/connect/par", request.RequestUri!.AbsoluteUri);
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
            Assert.Equal("code", form["response_type"]);
            // The framework omits query because it is the default for response_type=code.
            Assert.False(form.ContainsKey("response_mode"));
            Assert.Equal("S256", form["code_challenge_method"]);
            Assert.False(form.ContainsKey("client_secret"));
            return response;
        }));
        using var provider = CreateProvider(backchannel);
        var context = NewContext(provider);

        await CreatePipeline(provider)(context);

        Assert.Equal(1, requests);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/Account/AuthError?error=par_request_rejected", context.Response.Headers.Location.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.DoesNotContain("private-", context.Response.Headers.ToString());
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        Assert.DoesNotContain(context.Response.Headers.SetCookie, value => value!.StartsWith(".AspNetCore.Cookies="));
    }

    [Fact]
    public async Task Challenge_ShouldKeepFrameworkParRedirectAndPkce_WhenPushSucceeds()
    {
        using var backchannel = new HttpClient(new ResponseHandler(async request =>
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
            Assert.Equal("S256", form["code_challenge_method"]);
            Assert.False(string.IsNullOrEmpty(form["state"]));
            Assert.False(string.IsNullOrEmpty(form["nonce"]));
            Assert.False(string.IsNullOrEmpty(form["code_challenge"]));
            return JsonResponse(HttpStatusCode.Created, "{\"request_uri\":\"urn:example:par:accepted\",\"expires_in\":60}");
        }));
        using var provider = CreateProvider(backchannel);
        var context = NewContext(provider);

        await CreatePipeline(provider)(context);

        Assert.Equal(302, context.Response.StatusCode);
        var location = new Uri(context.Response.Headers.Location.ToString());
        Assert.Equal("https://idp.example/connect/authorize", location.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("sample-client", query["client_id"]);
        Assert.Equal("urn:example:par:accepted", query["request_uri"]);
        Assert.False(query.ContainsKey("code_verifier"));
        Assert.False(query.ContainsKey("response_mode"));
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Theory]
    [InlineData("server_error")]
    [InlineData("unknown_error")]
    public async Task Challenge_ShouldPropagateUnexpectedProtocolError(string error)
    {
        using var backchannel = new HttpClient(new ResponseHandler(_ => Task.FromResult(
            JsonResponse(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { error })))));
        using var provider = CreateProvider(backchannel);
        var context = NewContext(provider);

        await Assert.ThrowsAsync<OpenIdConnectProtocolException>(() => CreatePipeline(provider)(context));

        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Challenge_ShouldPropagateTransportFailure()
    {
        var failure = new HttpRequestException("synthetic transport failure");
        using var backchannel = new HttpClient(new ResponseHandler(_ => throw failure));
        using var provider = CreateProvider(backchannel);
        var context = NewContext(provider);

        Assert.Same(failure, await Assert.ThrowsAsync<HttpRequestException>(() => CreatePipeline(provider)(context)));
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_ShouldPropagateUnrelatedProtocolException_WithoutPushMarker()
    {
        var failure = new OpenIdConnectProtocolException("invalid_request");
        failure.Data["error"] = "invalid_request";
        var middleware = new PushedAuthorizationErrorMiddleware(_ => throw failure);
        var context = new DefaultHttpContext();

        Assert.Same(failure, await Assert.ThrowsAsync<OpenIdConnectProtocolException>(() => middleware.InvokeAsync(context)));
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Callback_ShouldKeepRemoteFailureHandling_WithoutParExplanation()
    {
        using var backchannel = new HttpClient(new ResponseHandler(_ => throw new InvalidOperationException("No backchannel request expected.")));
        using var provider = CreateProvider(backchannel);
        var context = NewContext(provider);
        context.Request.Path = "/signin-oidc";
        context.Request.QueryString = new QueryString("?error=invalid_request");

        await CreatePipeline(provider)(context);

        Assert.Equal("/callback-failure", context.Response.Headers.Location.ToString());
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Theory]
    [InlineData("par_request_rejected", "par_request_rejected")]
    [InlineData("invalid_request", "invalid_request")]
    [InlineData("private-description", "authentication_failed")]
    public void AuthError_ShouldAllowOnlyLocalErrorCodes(string input, string expected)
    {
        using var client = new HttpClient();
        var options = Microsoft.Extensions.Options.Options.Create(new OidcDemoOptions());
        var controller = new AccountController(new OidcDemoService(client, options), options);

        controller.AuthError(input);

        Assert.Equal(expected, controller.ViewData["ErrorMessage"]);
    }

    private static ServiceProvider CreateProvider(HttpClient backchannel)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = AuthenticationSchemes.Cookies;
            options.DefaultChallengeScheme = AuthenticationSchemes.OpenIdConnect;
        }).AddCookie(AuthenticationSchemes.Cookies).AddOpenIdConnect(AuthenticationSchemes.OpenIdConnect, options =>
        {
            options.Authority = "https://idp.example";
            options.ClientId = "sample-client";
            options.ResponseType = "code";
            options.ResponseMode = "query";
            options.UsePkce = true;
            options.Backchannel = backchannel;
            options.Configuration = new OpenIdConnectConfiguration
            {
                Issuer = "https://idp.example",
                AuthorizationEndpoint = "https://idp.example/connect/authorize",
                PushedAuthorizationRequestEndpoint = "https://idp.example/connect/par"
            };
            options.Events.OnPushAuthorization = PushedAuthorizationErrorMiddleware.MarkPushStartedAsync;
            options.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect("/callback-failure");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext NewContext(IServiceProvider provider) => new()
    {
        RequestServices = provider,
        Request = { Scheme = "https", Host = new HostString("client.example"), Path = "/Account/Login" }
    };

    private static RequestDelegate CreatePipeline(IServiceProvider provider)
    {
        var app = new ApplicationBuilder(provider);
        app.UseMiddleware<PushedAuthorizationErrorMiddleware>();
        app.UseAuthentication();
        app.Run(context => context.ChallengeAsync(AuthenticationSchemes.OpenIdConnect,
            new AuthenticationProperties { RedirectUri = "/Account/Profile" }));
        return app.Build();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
