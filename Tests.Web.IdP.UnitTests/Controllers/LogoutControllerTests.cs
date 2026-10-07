using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Controllers.Connect;

namespace Tests.Web.IdP.UnitTests.Controllers;

public sealed class LogoutControllerTests
{
    private const string ApplicationCookieName = "LogoutTests.Application";
    private const string RegisteredRedirect = "https://rp.example/signout?source=app&value=a%2Bb";

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Logout_ShouldRetainApplicationCookie_WhenRpRequestHasNoLocalConfirmation(string method)
    {
        using var services = CreateServices();
        var session = await IssueSessionAsync(services);
        using var requestScope = services.CreateScope();
        var http = await CreateRequestAsync(requestScope.ServiceProvider, method, session.ApplicationCookie);

        var result = await CreateController(http).Logout();

        Assert.Equal("~/Views/Connect/Logout.cshtml", Assert.IsType<ViewResult>(result).ViewName);
        Assert.True(http.User.Identity!.IsAuthenticated);
        AssertCookieRetained(http);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Logout_ShouldIgnoreQueryConfirmation_EvenWithMatchingAntiforgeryToken(string method)
    {
        using var services = CreateServices();
        var session = await IssueSessionAsync(services);
        using var requestScope = services.CreateScope();
        var http = await CreateRequestAsync(requestScope.ServiceProvider, method, session.ApplicationCookie, session.AntiforgeryCookie);
        http.Request.QueryString = new QueryString("?logout_confirmation=true");
        http.Request.Headers["X-XSRF-TOKEN"] = session.Token;

        Assert.IsType<ViewResult>(await CreateController(http).Logout());
        AssertCookieRetained(http);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("foreign-browser")]
    [InlineData("different-user")]
    public async Task Logout_ShouldRejectConfirmation_WithoutBrowserAndUserBoundToken(string failure)
    {
        using var services = CreateServices();
        var victim = await IssueSessionAsync(services);
        var foreign = await IssueSessionAsync(services, failure == "different-user" ? "another-user" : "victim");
        var form = new Dictionary<string, StringValues> { ["logout_confirmation"] = "true" };
        if (failure != "missing")
            form["__RequestVerificationToken"] = failure == "invalid" ? "invalid-token" : foreign.Token;
        var antiforgeryCookie = failure == "different-user" ? foreign.AntiforgeryCookie : victim.AntiforgeryCookie;
        using var requestScope = services.CreateScope();
        var http = await CreateRequestAsync(requestScope.ServiceProvider, "POST", victim.ApplicationCookie, antiforgeryCookie, form);

        Assert.IsType<BadRequestResult>(await CreateController(http).Logout());
        Assert.True(http.User.Identity!.IsAuthenticated);
        AssertCookieRetained(http);
    }

    [Fact]
    public async Task Logout_ShouldRejectInvalidConfirmationValue_EvenWithMatchingToken()
    {
        using var services = CreateServices();
        var session = await IssueSessionAsync(services);
        var form = new Dictionary<string, StringValues>
        {
            ["logout_confirmation"] = "false",
            ["__RequestVerificationToken"] = session.Token
        };
        using var requestScope = services.CreateScope();
        var http = await CreateRequestAsync(requestScope.ServiceProvider, "POST", session.ApplicationCookie, session.AntiforgeryCookie, form);

        Assert.IsType<BadRequestResult>(await CreateController(http).Logout());
        AssertCookieRetained(http);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Logout_ShouldPreserveProtocolParameters_ThroughConfirmedCompletion(string initialMethod)
    {
        using var services = CreateServices();
        var session = await IssueSessionAsync(services);
        var protocolForm = new Dictionary<string, StringValues>
        {
            ["client_id"] = "test-rp",
            ["id_token_hint"] = "synthetic-id-token-hint",
            ["post_logout_redirect_uri"] = RegisteredRedirect,
            ["state"] = "state +/&= value",
            ["logout_hint"] = "opaque-hint",
            ["ui_locales"] = "zh-TW en-US"
        };
        var protocol = new OpenIddictRequest(protocolForm);
        using var initialScope = services.CreateScope();
        var initial = await CreateRequestAsync(initialScope.ServiceProvider, initialMethod, session.ApplicationCookie,
            form: protocolForm, protocolRequest: protocol);
        // The parsed POST request, rather than conflicting query values, owns the roundtrip.
        initial.Request.QueryString = new QueryString("?client_id=wrong-rp&state=wrong-state");

        var view = Assert.IsType<ViewResult>(await CreateController(initial).Logout());
        var model = Assert.IsType<OpenIddictRequest>(view.Model);
        Assert.Same(protocol, model);
        Assert.Equal("test-rp", model.ClientId);
        Assert.Equal("synthetic-id-token-hint", model.IdTokenHint);
        Assert.Equal(RegisteredRedirect, model.PostLogoutRedirectUri);
        Assert.Equal("state +/&= value", model.State);
        AssertCookieRetained(initial);

        protocolForm["logout_confirmation"] = "true";
        protocolForm["__RequestVerificationToken"] = session.Token;
        var confirmationRequest = new OpenIddictRequest(protocolForm);
        using var confirmationScope = services.CreateScope();
        var confirmation = await CreateRequestAsync(confirmationScope.ServiceProvider, "POST", session.ApplicationCookie,
            session.AntiforgeryCookie, protocolForm, confirmationRequest);

        var completion = Assert.IsType<SignOutResult>(await CreateController(confirmation).Logout());

        Assert.Equal([OpenIddictServerAspNetCoreDefaults.AuthenticationScheme], completion.AuthenticationSchemes);
        Assert.Null(completion.Properties?.RedirectUri);
        Assert.Equal(model.ClientId, confirmationRequest.ClientId);
        Assert.Equal(model.IdTokenHint, confirmationRequest.IdTokenHint);
        Assert.Equal(model.PostLogoutRedirectUri, confirmationRequest.PostLogoutRedirectUri);
        Assert.Equal(model.State, confirmationRequest.State);
        Assert.Contains(confirmation.Response.Headers.SetCookie,
            cookie => cookie!.StartsWith(ApplicationCookieName + "=;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Logout_ShouldCompleteProtocolWithoutLocalToken_WhenUnauthenticated(string method)
    {
        using var services = CreateServices();
        using var requestScope = services.CreateScope();
        var http = await CreateRequestAsync(requestScope.ServiceProvider, method);

        var result = Assert.IsType<SignOutResult>(await CreateController(http).Logout());

        Assert.Equal([OpenIddictServerAspNetCoreDefaults.AuthenticationScheme], result.AuthenticationSchemes);
        AssertCookieRetained(http);
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    public async Task LogoutProtocol_ShouldRetainClientAndRedirectValidation(
        bool knownClient, bool registeredRedirect, bool permitted, bool expectedRejection)
    {
        var application = new object();
        var applications = new Mock<IOpenIddictApplicationManager>();
        applications.Setup(manager => manager.FindByClientIdAsync("test-rp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(knownClient ? application : null);
        applications.Setup(manager => manager.ValidatePostLogoutRedirectUriAsync(
                application, RegisteredRedirect, It.IsAny<CancellationToken>()))
            .ReturnsAsync(registeredRedirect);
        applications.Setup(manager => manager.HasPermissionAsync(
                application, OpenIddictConstants.Permissions.Endpoints.EndSession, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permitted);
        using var services = new ServiceCollection().AddSingleton(applications.Object).BuildServiceProvider();
        var transaction = new OpenIddictServerTransaction
        {
            Request = new OpenIddictRequest { ClientId = "test-rp", PostLogoutRedirectUri = RegisteredRedirect },
            EndpointType = OpenIddictServerEndpointType.EndSession,
            Options = new OpenIddictServerOptions(),
            Logger = NullLogger.Instance
        };
        var authentication = new OpenIddictServerEvents.ProcessAuthenticationContext(transaction);
        await ActivatorUtilities.CreateInstance<OpenIddictServerHandlers.ValidateClientId>(services)
            .HandleAsync(authentication);
        var validation = new OpenIddictServerEvents.ValidateEndSessionRequestContext(transaction);
        if (authentication.IsRejected)
        {
            validation.Reject(authentication.Error, authentication.ErrorDescription, authentication.ErrorUri);
        }
        else
        {
            await new OpenIddictServerHandlers.Session.ValidatePostLogoutRedirectUriParameter().HandleAsync(validation);
            await ActivatorUtilities.CreateInstance<OpenIddictServerHandlers.Session.ValidateClientPostLogoutRedirectUri>(services)
                .HandleAsync(validation);
            if (!validation.IsRejected)
                await ActivatorUtilities.CreateInstance<OpenIddictServerHandlers.Session.ValidateEndpointPermissions>(services)
                    .HandleAsync(validation);
        }

        Assert.Equal(expectedRejection, validation.IsRejected);
        if (expectedRejection)
            Assert.NotNull(validation.Error);
    }

    [Fact]
    public void Logout_ShouldAllowInitialRpGetAndPost_WithoutGlobalAntiforgeryFilter()
    {
        var action = typeof(LogoutController).GetMethod(nameof(LogoutController.Logout))!;

        Assert.Equal("~/connect/logout", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("~/connect/logout", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.NotNull(action.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>());
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
        services.AddMvc();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options => options.Cookie.Name = ApplicationCookieName);
        return services.BuildServiceProvider();
    }

    private static async Task<(string ApplicationCookie, string AntiforgeryCookie, string Token)> IssueSessionAsync(
        IServiceProvider services, string subject = "victim")
    {
        using var issuanceScope = services.CreateScope();
        var http = await CreateRequestAsync(issuanceScope.ServiceProvider, "GET");
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, subject)], IdentityConstants.ApplicationScheme));
        await http.SignInAsync(IdentityConstants.ApplicationScheme, http.User);
        var tokens = issuanceScope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
        var cookies = http.Response.Headers.SetCookie.Select(cookie => cookie!.Split(';')[0]).ToArray();
        return (cookies.Single(cookie => cookie.StartsWith(ApplicationCookieName + "=", StringComparison.Ordinal)),
            cookies.Single(cookie => !cookie.StartsWith(ApplicationCookieName + "=", StringComparison.Ordinal)),
            tokens.RequestToken!);
    }

    private static async Task<DefaultHttpContext> CreateRequestAsync(
        IServiceProvider services, string method, string? applicationCookie = null, string? antiforgeryCookie = null,
        Dictionary<string, StringValues>? form = null, OpenIddictRequest? protocolRequest = null)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = method;
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("idp.example");
        http.Request.Path = "/connect/logout";
        http.Request.Headers.Cookie = string.Join("; ", new[] { applicationCookie, antiforgeryCookie }
            .Where(cookie => !string.IsNullOrEmpty(cookie)));
        if (method == "POST")
        {
            http.Request.ContentType = "application/x-www-form-urlencoded";
            http.Request.Form = new FormCollection(form ?? new Dictionary<string, StringValues>());
        }
        if (applicationCookie != null)
            http.User = (await http.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Principal!;
        http.Features.Set(new OpenIddictServerAspNetCoreFeature
        {
            Transaction = new OpenIddictServerTransaction { Request = protocolRequest ?? new OpenIddictRequest() }
        });
        return http;
    }

    private static LogoutController CreateController(HttpContext http) => new(
        http.RequestServices.GetRequiredService<IAntiforgery>())
    {
        ControllerContext = new ControllerContext { HttpContext = http }
    };

    private static void AssertCookieRetained(HttpContext http) => Assert.DoesNotContain(
        http.Response.Headers.SetCookie, cookie => cookie!.StartsWith(ApplicationCookieName + "=", StringComparison.Ordinal));
}
