namespace Web.IdP.Middleware;

using Microsoft.Extensions.Options;
using Web.IdP.Options;

/// <summary>
/// Middleware to add security headers to HTTP responses for CSP compliance and security best practices
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;
    private readonly CspExtensionOptions _cspOptions;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env, IOptions<CspExtensionOptions> cspOptions)
    {
        _next = next;
        _env = env;
        _cspOptions = cspOptions.Value;
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
        var scriptSrc = "'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com 'unsafe-eval'";
        var styleSrc = "'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com https://fonts.googleapis.com 'unsafe-inline'";
        var styleSrcElem = "'self' https://cdn.jsdelivr.net https://challenges.cloudflare.com https://fonts.googleapis.com 'unsafe-inline'";
        var styleSrcAttr = "'unsafe-inline'";
        var connectSrc = "'self' https://challenges.cloudflare.com";
        var frameSrc = "https://challenges.cloudflare.com";

        // In development, allow Vite HMR and source maps with more permissive policies
        if (_env.IsDevelopment())
        {
            scriptSrc += " 'unsafe-eval' 'unsafe-inline' http://localhost:5173"; // Vite needs eval, inline, and localhost dev server
            styleSrc += " 'unsafe-inline' http://localhost:5173"; // Allow inline styles for Vue HMR and dev server styles
            styleSrcElem += " 'unsafe-inline' http://localhost:5173"; // Allow inline <style> tags for Vue HMR and dev server styles
            styleSrcAttr = "'unsafe-inline'"; // Allow inline style attributes for Vue HMR
            connectSrc += " ws: wss: http://localhost:5173 https://cdn.jsdelivr.net"; // WebSocket for HMR, SignalR, Vite, and source maps
        }
        else
        {
            connectSrc += " wss: https://cdn.jsdelivr.net"; // Production: SignalR WebSocket and source maps
        }

        var mergedScriptSrc = MergeDirectiveSources(scriptSrc, _cspOptions.GetValidatedScriptSrc());
        var mergedScriptSrcElem = MergeDirectiveSources(mergedScriptSrc, _cspOptions.GetValidatedScriptSrcElem());
        var mergedStyleSrc = MergeDirectiveSources(styleSrc, _cspOptions.GetValidatedStyleSrc());
        var mergedStyleSrcElem = MergeDirectiveSources(mergedStyleSrcElem: styleSrcElem, extraSources: _cspOptions.GetValidatedStyleSrcElem(), inheritedSources: _cspOptions.GetValidatedStyleSrc());
        var mergedConnectSrc = MergeDirectiveSources(connectSrc, _cspOptions.GetValidatedConnectSrc());
        var mergedFrameSrc = MergeDirectiveSources(frameSrc, _cspOptions.GetValidatedFrameSrc());

        var cspParts = new List<string>
        {
            "default-src 'self'",
            $"script-src {mergedScriptSrc}",
            $"script-src-elem {mergedScriptSrcElem}",
            $"style-src {mergedStyleSrc}",
            $"style-src-elem {mergedStyleSrcElem}",
            $"style-src-attr {styleSrcAttr}",
            "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com data:",
            "img-src 'self' data: https:",
            $"connect-src {mergedConnectSrc}",
            $"frame-src {mergedFrameSrc}",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self' https:",
            "object-src 'none'"
        };

        cspParts.AddRange(_cspOptions.GetValidatedAdditionalDirectives());

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

    private static string MergeDirectiveSources(string baseSources, IEnumerable<string> extraSources)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Tokenize(baseSources))
        {
            if (seen.Add(source))
            {
                values.Add(source);
            }
        }

        foreach (var source in extraSources)
        {
            if (seen.Add(source))
            {
                values.Add(source);
            }
        }

        return string.Join(' ', values);
    }

    private static string MergeDirectiveSources(string mergedStyleSrcElem, IEnumerable<string> extraSources, IEnumerable<string> inheritedSources)
    {
        return MergeDirectiveSources(
            MergeDirectiveSources(mergedStyleSrcElem, inheritedSources),
            extraSources);
    }

    private static IEnumerable<string> Tokenize(string sources)
    {
        return sources.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
