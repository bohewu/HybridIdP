using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Web.IdP.Attributes;

/// <summary>
/// CSRF validation attribute that only validates antiforgery tokens for cookie-authenticated requests.
/// Bearer token requests are skipped because:
/// 1. Bearer tokens must be explicitly added by client code
/// 2. Attackers cannot access tokens stored in browser memory/localStorage (same-origin policy)
/// 3. CSRF attacks only work with credentials that browsers automatically send (cookies)
/// 
/// SECURITY NOTE: We check the actual authentication scheme AFTER authentication runs,
/// not just the presence of Authorization header (which could be faked by attackers).
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

        // Check the ACTUAL authentication scheme used, not just headers
        // This runs AFTER authentication, so we can trust the auth result
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Check if authenticated via Bearer token (JWT)
            var authScheme = user.Identity.AuthenticationType;
            if (authScheme == "Bearer" ||  // JWT Bearer tokens
                authScheme == "AuthenticationTypes.Federation" ||  // Federated tokens
                authScheme?.Contains("Jwt", StringComparison.OrdinalIgnoreCase) == true)
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
