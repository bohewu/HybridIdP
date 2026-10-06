using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Web.IdP.Controllers.Account;

namespace Tests.Web.IdP.UnitTests.Attributes;

public sealed class AccountAntiforgeryTests
{
    [Theory]
    [InlineData(typeof(PasskeyController), nameof(PasskeyController.MakeCredentialOptions))]
    [InlineData(typeof(PasskeyController), nameof(PasskeyController.MakeCredential))]
    [InlineData(typeof(PasskeyController), nameof(PasskeyController.AssertionOptionsPost))]
    [InlineData(typeof(PasskeyController), nameof(PasskeyController.MakeAssertion))]
    [InlineData(typeof(PasskeyController), nameof(PasskeyController.DeletePasskey))]
    [InlineData(typeof(MfaController), nameof(MfaController.BeginReauthentication))]
    [InlineData(typeof(MfaController), nameof(MfaController.GenerateRecoveryCodes))]
    public async Task AccountMutation_ShouldRejectMissingToken_BeforeExecutingAction(Type controllerType, string methodName)
    {
        using var services = CreateServices();
        var http = CreateHttpContext(services, null);
        http.Request.Headers.Authorization = "Bearer unrelated-token";
        var context = await ValidateAsync(services, http, controllerType, methodName);

        Assert.IsAssignableFrom<BadRequestResult>(context.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Identity.Application")]
    [InlineData("Identity.TwoFactorUserId")]
    public async Task PasskeyLogin_ShouldAcceptMatchingToken_ForAnonymousFullAndPartialPrincipals(string? authenticationType)
    {
        using var services = CreateServices();
        var issuing = CreateHttpContext(services, authenticationType);
        var tokens = services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(issuing);
        var request = CreateHttpContext(services, authenticationType);
        request.Request.Headers.Cookie = issuing.Response.Headers.SetCookie.Single()!.Split(';')[0];
        request.Request.Headers["X-XSRF-TOKEN"] = tokens.RequestToken;

        var context = await ValidateAsync(services, request, typeof(PasskeyController), nameof(PasskeyController.MakeAssertion));

        Assert.Null(context.Result);
    }

    [Theory]
    [InlineData("invalid-token", false)]
    [InlineData(null, true)]
    public async Task PasskeyLogin_ShouldRejectInvalidOrDifferentSubjectToken(string? suppliedToken, bool differentSubject)
    {
        using var services = CreateServices();
        var issuing = CreateHttpContext(services, "Identity.Application");
        var tokens = services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(issuing);
        var request = CreateHttpContext(services, "Identity.Application", differentSubject ? "another-user" : "current-user");
        request.Request.Headers.Cookie = issuing.Response.Headers.SetCookie.Single()!.Split(';')[0];
        request.Request.Headers["X-XSRF-TOKEN"] = suppliedToken ?? tokens.RequestToken;

        var context = await ValidateAsync(services, request, typeof(PasskeyController), nameof(PasskeyController.MakeAssertion));

        Assert.IsAssignableFrom<BadRequestResult>(context.Result);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
        services.AddMvc();
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider services, string? authenticationType, string subject = "current-user")
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Method = "POST";
        if (authenticationType != null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], authenticationType));
        return context;
    }

    private static async Task<AuthorizationFilterContext> ValidateAsync(
        IServiceProvider services, HttpContext http, Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)!;
        var factory = (IFilterFactory?)method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() ??
            controllerType.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>();
        Assert.NotNull(factory);
        var filter = Assert.IsAssignableFrom<IAsyncAuthorizationFilter>(factory.CreateInstance(services));
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = controllerType.GetTypeInfo(), MethodInfo = method };
        var context = new AuthorizationFilterContext(new ActionContext(http, new(), descriptor), [filter]);
        await filter.OnAuthorizationAsync(context);
        return context;
    }
}
