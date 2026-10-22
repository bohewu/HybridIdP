using Microsoft.Extensions.Localization;
using Web.IdP.Services;

namespace Web.IdP.Middleware;

public sealed class SessionStateUnavailableMiddleware(RequestDelegate next,
    ILogger<SessionStateUnavailableMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IStringLocalizer<SharedResource> localizer)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (exception is SessionStateUnavailableException or Core.Application.EmailMfaCooldownUnavailableException)
        {
            logger.LogWarning("Session state confirmation failed; the affected request must be retried.");
            if (context.Response.HasStarted)
            {
                context.Abort();
                return;
            }

            // Discard any queued completion redirect or newly issued cookie.
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.RetryAfter = "5";
            var message = localizer["SessionStateUnavailableRetry"].Value;
            if (context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.StartsWithSegments("/connect"))
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "temporarily_unavailable",
                    error_description = message,
                    message
                });
            }
            else
            {
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(message);
            }
        }
    }
}
