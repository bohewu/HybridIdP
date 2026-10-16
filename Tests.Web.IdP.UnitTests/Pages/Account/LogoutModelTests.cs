using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Core.Application;
using Core.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP;
using Web.IdP.Attributes;
using Web.IdP.Pages.Account;
using Web.IdP.Services;
using Web.IdP.Services.Localization;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class LogoutModelTests
{
    [Theory]
    [InlineData("en-US", "Please confirm logout again")]
    [InlineData("zh-TW", "請重新確認登出")]
    public async Task Logout_ShouldRejectOldIdentityToken_ThenRequireFreshConfirmation(string culture, string notice)
    {
        await using var host = await LogoutHost.CreateAsync();
        using var client = host.CreateClient();
        SetUser(client, "previous-user");
        var oldToken = await ReadTokenAsync(client);
        var currentUser = "current-user<img>";
        SetUser(client, currentUser);

        var rejected = await PostAsync(client, oldToken, "https://untrusted.example/return");

        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/Account/Logout?confirmationExpired=True", rejected.Headers.Location?.OriginalString);
        host.SignIn.Verify(value => value.SignOutAsync(), Times.Never);

        var recovery = await client.GetAsync(rejected.Headers.Location + "&culture=" + culture);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var html = await recovery.Content.ReadAsStringAsync();
        Assert.Contains(notice, WebUtility.HtmlDecode(html));
        Assert.Contains(HtmlEncoder.Default.Encode(currentUser), html);
        Assert.DoesNotContain(currentUser, html);
        Assert.DoesNotContain("untrusted.example", html);
        Assert.Contains("method=\"post\"", html);
        host.SignIn.Verify(value => value.SignOutAsync(), Times.Never);

        // The page is informational until the user submits its newly issued token.
        var confirmed = await PostAsync(client, ExtractToken(html));
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        host.SignIn.Verify(value => value.SignOutAsync(), Times.Once);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tampered")]
    [InlineData("foreign-browser")]
    public async Task Logout_ShouldNeverSignOut_WhenTokenIsInvalid(string invalidTokenKind)
    {
        await using var host = await LogoutHost.CreateAsync();
        using var client = host.CreateClient();
        SetUser(client, "current-user");
        var token = await ReadTokenAsync(client);
        if (invalidTokenKind == "missing") token = null;
        if (invalidTokenKind == "tampered") token = "not-an-antiforgery-token";
        if (invalidTokenKind == "foreign-browser")
        {
            using var otherBrowser = host.CreateClient();
            SetUser(otherBrowser, "current-user");
            token = await ReadTokenAsync(otherBrowser);
        }

        var response = await PostAsync(client, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Logout?confirmationExpired=True", response.Headers.Location?.OriginalString);
        host.SignIn.Verify(value => value.SignOutAsync(), Times.Never);
    }

    [Theory]
    [InlineData("en-US", "You are not currently signed in.")]
    [InlineData("zh-TW", "您目前尚未登入。")]
    public async Task Logout_ShouldExplainAnonymousStaleSubmission_WithoutLogoutForm(string culture, string explanation)
    {
        await using var host = await LogoutHost.CreateAsync();
        using var client = host.CreateClient();
        SetUser(client, "previous-user");
        var token = await ReadTokenAsync(client);
        client.DefaultRequestHeaders.Remove("X-Test-User");

        var rejected = await PostAsync(client, token);
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        var recovery = await client.GetAsync(rejected.Headers.Location + "&culture=" + culture);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var html = await recovery.Content.ReadAsStringAsync();
        Assert.Contains(explanation, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("method=\"post\"", html);
        Assert.Contains("href=\"/Account/Logout?confirmationExpired=true\"", html);
        Assert.Contains("href=\"/Account/Login?returnUrl=%2FAccount%2FLogout%3FconfirmationExpired%3Dtrue\"", html);
        host.SignIn.Verify(value => value.SignOutAsync(), Times.Never);
    }

    [Fact]
    public async Task Logout_ShouldNotSignOutOnRecoveryGet_OrAlterOtherPageAntiforgeryFailures()
    {
        await using var host = await LogoutHost.CreateAsync();
        using var client = host.CreateClient();
        SetUser(client, "current-user");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Logout?confirmationExpired=true&returnUrl=https://untrusted.example")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Login", new FormUrlEncodedContent([]))).StatusCode);
        host.SignIn.Verify(value => value.SignOutAsync(), Times.Never);
    }

    [Fact]
    public void RecoveryFilter_ShouldLeaveUnrelatedBadRequestUnchanged()
    {
        var badRequest = new BadRequestResult();
        var context = new ResultExecutingContext(
            new ActionContext(new DefaultHttpContext(), new(), new ActionDescriptor()), [], badRequest, new object());
        context.HttpContext.Request.Method = "POST";

        new LogoutAntiforgeryRecoveryAttribute().OnResultExecuting(context);

        Assert.Same(badRequest, context.Result);
    }

    private static void SetUser(HttpClient client, string name)
    {
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", name);
    }

    private static async Task<string> ReadTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ExtractToken(await response.Content.ReadAsStringAsync());
    }

    private static string ExtractToken(string html)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "The real Razor form must publish an antiforgery token.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string? token, string? returnUrl = null)
    {
        var form = new Dictionary<string, string>();
        if (token != null) form["__RequestVerificationToken"] = token;
        if (returnUrl != null) form["returnUrl"] = returnUrl;
        return client.PostAsync("/Account/Logout", new FormUrlEncodedContent(form));
    }

    // Uses the existing bounded Kestrel/Razor pattern: real antiforgery and page
    // filters, synthetic identity, no database, provider, SMTP or external server.
    private sealed class LogoutHost(WebApplication app, Mock<SignInManager<ApplicationUser>> signIn) : IAsyncDisposable
    {
        public Mock<SignInManager<ApplicationUser>> SignIn { get; } = signIn;

        public HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { BaseAddress = new Uri(app.Urls.Single()) };

        public static async Task<LogoutHost> CreateAsync()
        {
            var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Web.IdP"));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(LogoutModel).Assembly.FullName,
                ContentRootPath = contentRoot,
                EnvironmentName = "Test"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["AllowedHosts"] = "127.0.0.1";
            builder.Logging.ClearProviders();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddRazorPages();
            builder.Services.AddSingleton<IStringLocalizer<SharedResource>>(
                new JsonStringLocalizer<SharedResource>([Path.Combine(contentRoot, "Resources")]));
            builder.Services.AddSingleton(Mock.Of<IBrandingService>(service => service.GetProductNameAsync(default) == Task.FromResult("HybridIdP")));
            builder.Services.AddSingleton(Mock.Of<IViteManifestService>());
            var users = new Mock<UserManager<ApplicationUser>>(
                Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
            var signIn = new Mock<SignInManager<ApplicationUser>>(
                users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                null!, null!, null!, null!);
            signIn.Setup(value => value.SignOutAsync()).Returns(Task.CompletedTask);
            builder.Services.AddSingleton(signIn.Object);
            var app = builder.Build();
            app.UseRequestLocalization(new RequestLocalizationOptions().AddSupportedCultures("en-US", "zh-TW")
                .AddSupportedUICultures("en-US", "zh-TW").SetDefaultCulture("en-US"));
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-User", out var name))
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, name.ToString()), new Claim(ClaimTypes.Name, name.ToString())
                    ], IdentityConstants.ApplicationScheme));
                await next(context);
            });
            app.MapRazorPages();
            await app.StartAsync();
            return new LogoutHost(app, signIn);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
