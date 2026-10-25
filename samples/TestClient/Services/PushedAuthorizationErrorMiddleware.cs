using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TestClient.Services;

public sealed class PushedAuthorizationErrorMiddleware(RequestDelegate next)
{
    private static readonly object PushStartedKey = new();

    public static Task MarkPushStartedAsync(PushedAuthorizationContext context)
    {
        // This event precedes the framework's PAR POST, not callback processing.
        context.HttpContext.Items[PushStartedKey] = true;
        return Task.CompletedTask;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OpenIdConnectProtocolException exception) when (
            !context.Response.HasStarted && context.Items.ContainsKey(PushStartedKey) &&
            exception.Data["error"] is "invalid_request" or "invalid_client" or
                "unauthorized_client" or "invalid_scope" or "unsupported_response_type")
        {
            // Never copy the exception message, description or response body to the browser.
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Redirect(context.Request.PathBase + "/Account/AuthError?error=par_request_rejected");
        }
        finally
        {
            context.Items.Remove(PushStartedKey);
        }
    }
}
