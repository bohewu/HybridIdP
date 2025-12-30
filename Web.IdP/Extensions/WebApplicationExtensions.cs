using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Web.IdP.Middleware;
using Web.IdP.Options;
using Core.Application.Options;
using global::Infrastructure.Options;
using System.Globalization;
using HealthChecks.UI.Client;

namespace Web.IdP.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseCustomPipeline(this WebApplication app, IConfiguration configuration)
    {
        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // Add security headers middleware
        app.UseSecurityHeaders();

        // Configure Forwarded Headers Middleware (Phase 17)
        var proxyOptions = new ProxyOptions();
        configuration.GetSection(ProxyOptions.Section).Bind(proxyOptions);

        if (proxyOptions.Enabled)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };

            // Add KnownProxies
            if (!string.IsNullOrEmpty(proxyOptions.KnownProxies))
            {
                foreach (var ipOrCidr in proxyOptions.KnownProxies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    // Check for CIDR notation (e.g., 10.0.0.0/8)
                    if (ipOrCidr.Contains('/'))
                    {
                        var parts = ipOrCidr.Split('/');
                        if (parts.Length == 2 && 
                            IPAddress.TryParse(parts[0], out var networkIp) && 
                            int.TryParse(parts[1], out var prefixLength))
                        {
                            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(networkIp, prefixLength));
                        }
                    }
                    // Check for simple IP
                    else if (IPAddress.TryParse(ipOrCidr, out var address))
                    {
                        options.KnownProxies.Add(address);
                    }
                }
            }

            options.ForwardLimit = proxyOptions.ForwardLimit;
            options.RequireHeaderSymmetry = proxyOptions.RequireHeaderSymmetry;

            app.UseForwardedHeaders(options);
        }

        app.UseRouting();

        var supportedCultures = new[] { "zh-TW", "en-US" };
        var localizationOptions = new RequestLocalizationOptions()
            .SetDefaultCulture("zh-TW")
            .AddSupportedCultures(supportedCultures)
            .AddSupportedUICultures(supportedCultures);

        app.UseRequestLocalization(localizationOptions);

        // Use rate limiting middleware if enabled
        var rateLimitingOptions = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.Section).Bind(rateLimitingOptions);

        if (rateLimitingOptions.Enabled)
        {
            app.UseRateLimiter();
        }

        app.UseStaticFiles();

        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapCustomEndpoints(this WebApplication app, IConfiguration configuration)
    {
        // Protect Prometheus metrics endpoint with IP whitelist authorization
        var observabilityOptions = new ObservabilityOptions();
        configuration.GetSection(ObservabilityOptions.MonitoringSection).Bind(observabilityOptions);
        configuration.GetSection(ObservabilityOptions.ObservabilitySection).Bind(observabilityOptions);

        if (observabilityOptions.PrometheusEnabled)
        {
            app.MapPrometheusScrapingEndpoint()
                .RequireAuthorization("PrometheusMetrics");
        }

        // Map Health Checks Endpoints
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        app.MapControllers();
        app.MapHub<global::Infrastructure.Hubs.MonitoringHub>("/monitoringHub");
        app.MapRazorPages();

        return app;
    }
}
