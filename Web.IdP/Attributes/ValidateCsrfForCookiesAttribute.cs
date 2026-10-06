using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OpenIddict.Validation.AspNetCore;

namespace Web.IdP.Attributes;

/// <summary>
/// CSRF validation attribute that only validates antiforgery tokens for cookie-authenticated requests.
/// Bearer token requests are skipped because:
/// 1. Bearer tokens must be explicitly added by client code
/// 2. Attackers cannot access tokens stored in browser memory/localStorage (same-origin policy)
/// 3. CSRF attacks only work with credentials that browsers automatically send (cookies)
/// 
/// Exemption requires successful OpenIddict authentication and no participating cookie identity.
/// An Authorization header or an identity's authentication-type label is not token validation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ValidateCsrfForCookiesAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var method = httpContext.Request.Method;

        // Only validate for mutating methods (POST, PUT, DELETE, PATCH)
        if (HttpMethods.IsGet(method) ||
            HttpMethods.IsHead(method) ||
            HttpMethods.IsOptions(method) ||
            HttpMethods.IsTrace(method))
        {
            await next();
            return;
        }

        var user = httpContext.User;
        var hasCookieIdentity = user.Identities.Any(identity => identity.IsAuthenticated &&
            (identity.AuthenticationType == IdentityConstants.ApplicationScheme ||
             identity.AuthenticationType == IdentityConstants.TwoFactorUserIdScheme));
        if (user.Identity?.IsAuthenticated == true && !hasCookieIdentity)
        {
            var bearer = await httpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            if (bearer.Succeeded && bearer.Principal?.Identity?.IsAuthenticated == true)
            {
                // Authenticated via Bearer token - CSRF not needed
                await next();
                return;
            }
        }

        // Cookie-authenticated or unauthenticated mutating request - validate CSRF
        var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            await next();
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new
            {
                error = "CSRF token validation failed",
                message = "The required antiforgery token was not provided or is invalid."
            });
        }
    }
}
