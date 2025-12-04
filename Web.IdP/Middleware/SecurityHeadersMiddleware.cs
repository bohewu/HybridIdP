namespace Web.IdP.Middleware;

/// <summary>
/// Middleware to add security headers to HTTP responses for CSP compliance and security best practices
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip security headers for OAuth/OpenIdConnect endpoints to avoid CSP conflicts
        if (context.Request.Path.StartsWithSegments("/connect"))
        {
            await _next(context);
            return;
        }

        // Content Security Policy (CSP)
        // Allow Bootstrap CDN, Bootstrap Icons CDN, Cloudflare Turnstile, and self
        var scriptSrc = "'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com";
        var styleSrc = "'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com";
        var styleSrcElem = "'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com";
        var styleSrcAttr = "'none'";
        var connectSrc = "'self' https://challenges.cloudflare.com";
        var frameSrc = "https://challenges.cloudflare.com";

        // In development, allow Vite HMR and source maps with more permissive policies
        if (_env.IsDevelopment())
        {
            scriptSrc += " 'unsafe-eval' 'unsafe-inline' http://localhost:5173"; // Vite needs eval, inline, and localhost dev server
            styleSrc += " 'unsafe-inline'"; // Allow inline styles for Vue HMR
            styleSrcElem += " 'unsafe-inline'"; // Allow inline <style> tags for Vue HMR
            styleSrcAttr = "'unsafe-inline'"; // Allow inline style attributes for Vue HMR
            connectSrc += " ws: wss: http://localhost:5173 https://cdn.jsdelivr.net"; // WebSocket for HMR, SignalR, Vite, and source maps
        }
        else
        {
            connectSrc += " wss: https://cdn.jsdelivr.net"; // Production: SignalR WebSocket and source maps
        }

        var cspParts = new[]
        {
            "default-src 'self'",
            $"script-src {scriptSrc}",
            $"script-src-elem {scriptSrc}",
            $"style-src {styleSrc}",
            $"style-src-elem {styleSrcElem}",
            $"style-src-attr {styleSrcAttr}",
            "font-src 'self' https://cdn.jsdelivr.net data:",
            "img-src 'self' data: https:",
            $"connect-src {connectSrc}",
            $"frame-src {frameSrc}",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "object-src 'none'"
        };

        context.Response.Headers.Append("Content-Security-Policy", string.Join("; ", cspParts));

        // X-Content-Type-Options: Prevent MIME type sniffing
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

        // X-Frame-Options: Prevent clickjacking
        context.Response.Headers.Append("X-Frame-Options", "DENY");

        // X-XSS-Protection: Enable XSS filter (legacy browsers)
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

        // Referrer-Policy: Control referrer information
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // Permissions-Policy: Disable unnecessary browser features
        var permissionsPolicy = new[]
        {
            "camera=()",
            "microphone=()",
            "geolocation=()",
            "payment=()",
            "usb=()",
            "magnetometer=()",
            "gyroscope=()",
            "accelerometer=()"
        };
        context.Response.Headers.Append("Permissions-Policy", string.Join(", ", permissionsPolicy));

        // Strict-Transport-Security (HSTS): Force HTTPS (only in production)
        if (!_env.IsDevelopment())
        {
            context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
        }

        // Remove server header for security
        context.Response.Headers.Remove("Server");
        context.Response.Headers.Remove("X-Powered-By");

        await _next(context);
    }
}

/// <summary>
/// Extension method to register SecurityHeadersMiddleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
