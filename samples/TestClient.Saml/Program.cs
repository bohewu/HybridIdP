using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using TestClient.Saml;
using TestClient.Saml.Protocol;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "__Host-SamlSample.Session";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
    options.SlidingExpiration = false;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("demo-password", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var options = builder.Configuration.GetSection("Saml").Get<SamlFederationOptions>() ?? new();
var demo = builder.Configuration.GetSection("DemoIdentity").Get<DemoIdentityOptions>() ?? new();
if (options.Idp.Enabled && ((!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test")) ||
    !demo.AllowSyntheticIdentity || string.IsNullOrWhiteSpace(demo.Username) || demo.Password.Length < 16 ||
    !Guid.TryParseExact(demo.Subject, "N", out var subject) || subject == Guid.Empty))
    throw new InvalidOperationException("Sample IdP requires Development/Test, explicit synthetic identity opt-in, a configured username, a private password of at least 16 characters, and a stable nonempty N-format subject GUID.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(demo);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SamlFederationRuntime>();
builder.Services.AddSingleton<BrowserStateStore>();
builder.Services.AddSingleton<SamlIdpProtocol>();
builder.Services.AddSingleton<SamlSpResponseValidator>();
var app = builder.Build();
_ = app.Services.GetRequiredService<SamlFederationRuntime>(); // Fail enabled misconfiguration before accepting requests.
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; script-src 'self'; form-action https:; base-uri 'none'; frame-ancestors 'none'";
    if (!context.Request.IsHttps) { context.Response.StatusCode = 400; return; }
    try { await next(); }
    catch (Exception error) when (error is not OperationCanceledException)
    {
        // Never return assertion XML, keys, credentials or exception detail to the browser/log.
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = 400;
        context.Response.ContentType = "text/plain";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync("The SAML exchange was rejected. Return to the demo center and start a fresh sign-in. Check the configured trust and message lifetime.");
    }
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
